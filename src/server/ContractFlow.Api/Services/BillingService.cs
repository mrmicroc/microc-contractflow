using System.Globalization;
using System.Text.Json;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Services;

/// <summary>A money-document rule refused the operation — <see cref="Code"/>
/// is the machine-readable error the controllers return with a 409.</summary>
public class BillingException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Builds the money documents (design §6.1 "month-end close → customer invoice
/// spec + subcontractor statement"). Both generators price days through
/// <see cref="DayBilling"/> — billing with contract-terms rates, compensation
/// with agreement rates — so ledger 3 and ledger 4 never share a rate source,
/// and the quantities frozen at approval equal what is invoiced and paid.
/// Every line carries its explanation (§5).
/// </summary>
public partial class BillingService(AppDbContext db)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly HashSet<TimeEntryCategory> Counted =
        [TimeEntryCategory.Work, TimeEntryCategory.Waiting, TimeEntryCategory.Standby];

    private static (DateOnly From, DateOnly To) MonthRange(int year, int month) =>
        (new DateOnly(year, month, 1), new DateOnly(year, month, 1).AddMonths(1).AddDays(-1));

    /// <summary>Approved rows stay billable/payable after they are invoiced.</summary>
    private static bool Done(TimesheetStatus s) =>
        s is TimesheetStatus.Approved or TimesheetStatus.Invoiced;

    public static List<string> Warnings(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];

    // ── Supplier-invoice matching (§6.2) ────────────────────────────────────

    public record MatchDiff(string Kind, decimal Invoiced, decimal Statement, decimal Diff);

    /// <summary>Compares the supplier invoice's lines per kind against the
    /// statement's — the statement is the authority. An invoice registered
    /// without lines is compared on its total. Tolerance 0.01.</summary>
    public static List<MatchDiff> MatchInvoice(
        IEnumerable<SupplierInvoiceLine> invoiceLines,
        IEnumerable<StatementLine> statementLines,
        decimal? invoiceTotal = null)
    {
        var lines = invoiceLines.ToList();
        var stLines = statementLines.ToList();
        if (lines.Count == 0 && invoiceTotal.HasValue)
        {
            var st = stLines.Sum(l => l.Amount);
            return [new MatchDiff("total", invoiceTotal.Value, st, invoiceTotal.Value - st)];
        }
        var invoiced = lines
            .GroupBy(l => l.Kind).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        var statement = stLines
            .GroupBy(l => l.Kind).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        return invoiced.Keys.Union(statement.Keys)
            .OrderBy(k => k.ToString())
            .Select(k => new MatchDiff(
                k.ToString().ToLowerInvariant(),
                invoiced.GetValueOrDefault(k),
                statement.GetValueOrDefault(k),
                invoiced.GetValueOrDefault(k) - statement.GetValueOrDefault(k)))
            .ToList();
    }

    /// <summary>Within tolerance → Matched, otherwise Discrepancy.</summary>
    public static SupplierInvoiceStatus MatchOutcome(IEnumerable<MatchDiff> diffs) =>
        diffs.All(d => Math.Abs(d.Diff) <= 0.01m)
            ? SupplierInvoiceStatus.Matched
            : SupplierInvoiceStatus.Discrepancy;

    // ── Shared day loading ──────────────────────────────────────────────────

    private record MonthRow(
        Guid Id, Guid ResourceId, DateOnly Date, Guid ProjectId, Guid ContractId,
        Guid? WorkPackageId, EntryBilling Billing, bool Booked, decimal Hours,
        Guid? InvoiceSpecificationId, bool Outside = false)
    {
        public DayRow Day => new(Id, ProjectId, ContractId, WorkPackageId, Billing, Booked, Hours, Outside);
    }

    /// <summary>All approved/invoiced counted rows of the given resources in
    /// [from, to] — every project, so a day prices with its full context.</summary>
    private async Task<List<MonthRow>> MonthRowsAsync(
        IReadOnlyCollection<Guid> resourceIds, DateOnly from, DateOnly to, CancellationToken ct) =>
        await MarkPadCoveredAsync(await db.TimeEntries
            .Where(e => resourceIds.Contains(e.ResourceId) && e.Date >= from && e.Date <= to
                && e.Timesheet != null
                && (e.Timesheet.Status == TimesheetStatus.Approved
                    || e.Timesheet.Status == TimesheetStatus.Invoiced)
                && Counted.Contains(e.Category))
            .Select(e => new MonthRow(
                e.Id, e.ResourceId, e.Date, e.ProjectId, e.Project.ContractId,
                e.BookingId != null ? e.Booking!.WorkPackageId : e.WorkPackageId,
                e.Billing, e.BookingId != null, e.ActualHours, e.InvoiceSpecificationId,
                e.BookingId != null
                    ? e.Booking!.WorkPackage != null
                        && e.Booking.WorkPackage.PricingMode == WorkPackagePricing.OutsideContract
                    : e.WorkPackage != null
                        && e.WorkPackage.PricingMode == WorkPackagePricing.OutsideContract))
            .ToListAsync(ct), ct);

    // ── Customer invoice specification ──────────────────────────────────────

    /// <summary>
    /// (Re)builds the draft spec for project × month from approved timesheets.
    /// A draft is rebuilt in place; once the latest revision has left draft,
    /// <paramref name="supplement"/> creates the next revision billing only
    /// what no earlier revision billed (rows approved late, new PAD
    /// utilisation…). Nothing is ever billed twice: time entries point at the
    /// spec that billed them, other lines carry their source id.
    /// </summary>
    public async Task<InvoiceSpecification> GenerateInvoiceAsync(
        Guid projectId, int year, int month, Guid? userId, CancellationToken ct,
        bool supplement = false)
    {
        var (from, to) = MonthRange(year, month);
        var project = await db.Projects
            .Include(p => p.Contract).ThenInclude(c => c.Customer)
            .Include(p => p.Contract).ThenInclude(c => c.EndClient)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new BillingException("unknownProject", "Unknown project.");

        var specs = await db.InvoiceSpecifications.Include(s => s.Lines)
            .Where(s => s.ProjectId == projectId && s.Year == year && s.Month == month)
            .OrderBy(s => s.Revision).ToListAsync(ct);
        var spec = specs.LastOrDefault();
        var isNew = false;
        if (spec is { Status: not InvoiceStatus.Draft })
        {
            if (!supplement)
                throw new BillingException("invoiceFrozen",
                    "The specification has left draft — generate a supplement for rows approved since.");
            spec = new InvoiceSpecification
            {
                ProjectId = projectId, Year = year, Month = month,
                Revision = spec.Revision + 1, Currency = spec.Currency, CreatedByUserId = userId,
            };
            db.InvoiceSpecifications.Add(spec);
            isNew = true;
        }
        else if (spec is null)
        {
            spec = new InvoiceSpecification
            {
                ProjectId = projectId, Year = year, Month = month,
                Currency = "SEK", CreatedByUserId = userId,
            };
            db.InvoiceSpecifications.Add(spec);
            isNew = true;
        }
        else
        {
            db.InvoiceLines.RemoveRange(spec.Lines);
            spec.Lines.Clear();
            await db.TimeEntries.Where(e => e.InvoiceSpecificationId == spec.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.InvoiceSpecificationId, (Guid?)null), ct);
        }
        // Who the spec is addressed to and for whom the work was done, frozen
        // at generation (a draft regenerated later picks up register edits).
        spec.CustomerName = project.Contract.Customer.Name;
        spec.EndClientName = project.Contract.EndClient?.Name;
        spec.EndClientReference = project.Contract.EndClientReference;
        var specId = spec.Id;
        var others = specs.Where(s => s.Id != specId).ToList();
        // Any line of any other spec of this project — expense, cancellation
        // and mobilisation sources are billed once, ever.
        var billedSources = (await db.InvoiceLines
                .Where(l => l.Invoice.ProjectId == projectId && l.InvoiceSpecificationId != specId
                    && l.SourceId != null)
                .Select(l => l.SourceId!.Value).ToListAsync(ct))
            .ToHashSet();

        var warnings = new List<string>();
        void Warn(string w) { if (!warnings.Contains(w)) warnings.Add(w); }

        var terms = await db.ContractTerms
            .Where(t => t.ContractId == project.ContractId).ToListAsync(ct);
        ContractTerms? TermsAt(DateOnly d) => DayBilling.TermsAt(terms, project.ContractId, d);
        var endTerms = TermsAt(to);
        spec.Currency = endTerms?.Currency ?? spec.Currency;
        spec.MissingExchangeRate = false;
        var fx = await CurrencyConverter.ForMonthAsync(db, year, month, ct);
        bool CurrencyOk(string? currency, string what)
        {
            if (currency is null || currency == spec.Currency) return true;
            Warn($"{what} is in {currency}, not {spec.Currency} — left out of the total.");
            return false;
        }

        // Per-package rate overrides — resolution per (package, date): the
        // resource cell wins over the package row; latest version per scope.
        var wpRates = await db.WorkPackageRates.Include(r => r.WorkPackage)
            .Where(r => r.WorkPackage.ProjectId == projectId).ToListAsync(ct);
        var wpNames = await db.WorkPackages.Where(w => w.ProjectId == projectId)
            .ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        (WorkPackageRate? Gen, WorkPackageRate? Res) OverridesAt(Guid? wpId, Guid? resId, DateOnly d)
        {
            if (wpId is null) return (null, null);
            WorkPackageRate? Latest(Guid? rid) => wpRates
                .Where(r => r.WorkPackageId == wpId && r.ResourceId == rid && r.EffectiveFrom <= d)
                .OrderByDescending(r => r.EffectiveFrom)
                .ThenByDescending(r => r.CreatedUtc).FirstOrDefault();
            return (Latest(null), resId is null ? null : Latest(resId));
        }
        static string RateSource(WorkPackageRate? gen, WorkPackageRate? res) =>
            res is not null ? " · resource rates apply" : gen is not null ? " · package rates apply" : "";

        // ── PAD component: utilised PAD × the PAD rate. The terms that carry
        // PAD are the latest version in force during the month with PAD on —
        // the same versions that decided which rows the PAD line covers.
        var inMonth = terms
            .Where(t => t.EffectiveFrom <= to)
            .OrderBy(t => t.EffectiveFrom).ThenBy(t => t.Version)
            .Where(t => t.EffectiveFrom > from || t == TermsAt(from))
            .ToList();
        var padTerms = inMonth.LastOrDefault(t => t.PadEnabled);
        if (padTerms is not null && CurrencyOk(padTerms.Currency, "The PAD component"))
            await AddPadLinesAsync(spec, projectId, year, month, to, padTerms, others, Warn, ct);

        // ── Time billing: every day of a resource who worked on this project,
        // priced with the full day's context (shared minimum / OT per contract).
        var resourceIds = await db.TimeEntries
            .Where(e => e.ProjectId == projectId && e.Date >= from && e.Date <= to
                && e.Timesheet != null
                && (e.Timesheet.Status == TimesheetStatus.Approved
                    || e.Timesheet.Status == TimesheetStatus.Invoiced)
                && Counted.Contains(e.Category))
            .Select(e => e.ResourceId).Distinct().ToListAsync(ct);
        var monthRows = await MonthRowsAsync(resourceIds, from, to, ct);
        var dayContracts = monthRows.Select(r => r.ContractId).Distinct().ToList();
        var allTerms = await db.ContractTerms
            .Where(t => dayContracts.Contains(t.ContractId)).ToListAsync(ct);
        var resourceNames = await db.Resources.Where(r => resourceIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.DisplayName, ct);
        // This project's rows nobody billed yet — tracked so the marks persist.
        var unbilled = await db.TimeEntries
            .Where(e => e.ProjectId == projectId && e.Date >= from && e.Date <= to
                && e.InvoiceSpecificationId == null
                && e.Timesheet != null
                && (e.Timesheet.Status == TimesheetStatus.Approved
                    || e.Timesheet.Status == TimesheetStatus.Invoiced)
                && Counted.Contains(e.Category))
            .ToDictionaryAsync(e => e.Id, ct);
        var marked = 0;

        foreach (var g in monthRows.GroupBy(r => (r.ResourceId, r.Date))
                     .Where(g => g.Any(r => r.ProjectId == projectId))
                     .OrderBy(g => g.Key.Date).ThenBy(g => g.Key.ResourceId))
        {
            var (resourceId, date) = g.Key;
            var parts = DayBilling.BillDay(date, g.Select(r => r.Day).ToList(),
                c => DayBilling.TermsAt(allTerms, c, date),
                wp => OverridesAt(wp, resourceId, date));

            foreach (var part in parts.Where(p => p.ProjectId == projectId))
            {
                var open = part.Entries.Where(s => unbilled.ContainsKey(s.EntryId)).ToList();
                if (open.Count == 0) continue; // billed by an earlier revision

                if (part.Kind == BillPartKind.Priced)
                {
                    if (!CurrencyOk(part.Terms?.Currency, string.Create(Inv, $"Time on {date:yyyy-MM-dd}")))
                        continue; // left unmarked — billable once the currency question is settled
                    var pkg = part.WorkPackageId is { } wid ? wpNames.GetValueOrDefault(wid) : null;
                    if (part.ResourceRate?.NeedsReview == true)
                        Warn($"Unreviewed rate cell used: {resourceNames.GetValueOrDefault(resourceId, "?")}"
                            + (pkg is null ? "" : $" on {pkg}") + " — review it before issuing.");
                    var line = part.Line!;
                    if (line.Amount != 0m || line.EffectiveHours != 0m)
                        spec.Lines.Add(new InvoiceLine
                        {
                            InvoiceSpecificationId = specId,
                            Kind = InvoiceLineKind.Hours, Date = date,
                            Description = string.Create(Inv, $"{(part.Daily ? "Day" : "Work")} {date:yyyy-MM-dd}")
                                + (pkg is null ? "" : $" · {pkg}"),
                            Explanation = part.Explanation
                                + (part.HoursMode ? " · billed as hours" : "")
                                + RateSource(part.PackageRate, part.ResourceRate),
                            Quantity = part.Daily ? Math.Round(line.DayFraction, 4) : line.EffectiveHours,
                            UnitPrice = part.UnitPrice, Amount = line.Amount,
                        });
                }
                foreach (var s in open) unbilled[s.EntryId].InvoiceSpecificationId = specId;
                marked += open.Count;
            }
        }

        // ── One-off scheduled charges: each bills once, on its project's spec
        // for the month of its date (no project → the contract's default).
        var defaultProjectId = await DefaultChargeProjectAsync(project.ContractId, ct);
        var charges = await db.ScheduledCharges
            .Where(c => c.ContractId == project.ContractId && c.Date >= from && c.Date <= to
                && (c.ProjectId == projectId || (c.ProjectId == null && defaultProjectId == projectId)))
            .OrderBy(c => c.Date).ToListAsync(ct);
        foreach (var c in charges.Where(c => !billedSources.Contains(c.Id)))
        {
            if (!CurrencyOk(TermsAt(c.Date)?.Currency, string.Create(Inv, $"Scheduled charge {c.Date:yyyy-MM-dd}")))
                continue;
            spec.Lines.Add(new InvoiceLine
            {
                InvoiceSpecificationId = specId,
                Kind = InvoiceLineKind.ScheduledCharge, Date = c.Date, SourceId = c.Id,
                Description = c.Description,
                Explanation = string.Create(Inv, $"One-off amount scheduled for {c.Date:yyyy-MM-dd}"),
                Quantity = 1m, UnitPrice = c.Amount, Amount = c.Amount,
            });
        }

        // ── Cancellation charges on non-PAD contracts: the resolved charge
        // (hours or a fixed fee) rides the cancelled booking into billing.
        // Under PAD, hour charges already materialised as utilised PAD.
        var cancellations = await db.Bookings
            .Where(b => b.ProjectId == projectId
                && b.Status == BookingStatus.Cancelled
                && b.Date >= from && b.Date <= to
                && ((b.CancellationChargeHours ?? 0m) > 0m
                    || (b.CancellationChargeAmount ?? 0m) > 0m
                    || (b.CancellationChargeDayFraction ?? 0m) > 0m))
            .ToListAsync(ct);
        foreach (var b in cancellations.Where(b => !billedSources.Contains(b.Id)).OrderBy(x => x.Date))
        {
            var t = TermsAt(b.Date);
            if (!CurrencyOk(t?.Currency, string.Create(Inv, $"Cancellation {b.Date:yyyy-MM-dd}"))) continue;
            var (cGen, cRes) = OverridesAt(b.WorkPackageId, b.ResourceId, b.Date);
            var daily = t?.TimeBilling == TimeBilling.DailyRate;
            var rates = DayBilling.MergeRates(t, cGen, cRes, daily);
            var kind = TimeMath.KindOf(b.Date, t?.HolidayCalendar ?? "SE");
            decimal? qty = null, unit = null;
            string? explanation = null;
            if (daily && b.CancellationChargeDayFraction is > 0m)
            {
                // Day-rate contract: a share of the day; a full day whose
                // planned hours fit the half-day ceiling bills the half-day rate.
                var fraction = b.CancellationChargeDayFraction.Value;
                if (fraction == 1m && rates.HalfDayRate is > 0m && rates.HalfDayMaxHours is > 0m
                    && b.PlannedHours <= rates.HalfDayMaxHours.Value)
                {
                    qty = 1m; unit = rates.HalfDayRate.Value;
                    explanation = string.Create(Inv,
                        $"Late cancellation — half day (planned {b.PlannedHours:0.##} h ≤ {rates.HalfDayMaxHours.Value:0.##} h)");
                }
                else
                {
                    qty = fraction; unit = TimeMath.DayRateFor(kind, rates) ?? 0m;
                    explanation = fraction == 1m
                        ? "Late cancellation — full day"
                        : string.Create(Inv, $"{fraction:0.###} day cancellation charge");
                }
            }
            else if (b.CancellationChargeHours is > 0m)
            {
                if (daily)
                {
                    // Hours charge priced as a fraction of the day rate.
                    var norm = t!.OvertimeAfterHours > 0 ? t.OvertimeAfterHours : 8;
                    qty = Math.Round(b.CancellationChargeHours.Value / norm, 4);
                    unit = TimeMath.DayRateFor(kind, rates) ?? 0m;
                    explanation = string.Create(Inv,
                        $"{b.CancellationChargeHours.Value:0.##} h cancellation charge");
                }
                else
                {
                    // A booking charged in full bills at least the daily minimum.
                    var hours = b.CancellationChargeHours.Value;
                    var min = b.CancellationChargeFull ? t?.MinBillableHoursPerDay : null;
                    qty = min is { } m && m > hours ? m : hours;
                    unit = TimeMath.RateFor(kind, rates) ?? 0m;
                    explanation = string.Create(Inv, $"{hours:0.##} h cancellation charge")
                        + (qty > hours ? string.Create(Inv, $", raised to the {qty:0.##} h daily minimum") : "");
                }
            }
            if (qty is { } q && unit is { } u)
            {
                spec.Lines.Add(new InvoiceLine
                {
                    InvoiceSpecificationId = specId,
                    Kind = InvoiceLineKind.Cancellation, Date = b.Date, SourceId = b.Id,
                    Description = string.Create(Inv, $"Cancellation {b.Date:yyyy-MM-dd}"),
                    Explanation = explanation + RateSource(cGen, cRes),
                    Quantity = q, UnitPrice = u,
                    Amount = TimeMath.Round2(q * u),
                });
            }
            if (b.CancellationChargeAmount is > 0m)
            {
                spec.Lines.Add(new InvoiceLine
                {
                    InvoiceSpecificationId = specId,
                    Kind = InvoiceLineKind.Cancellation, Date = b.Date, SourceId = b.Id,
                    Description = string.Create(Inv, $"Cancellation fee {b.Date:yyyy-MM-dd}"),
                    Explanation = "Fixed cancellation fee per the contract's cancellation policy",
                    Quantity = 1m, UnitPrice = b.CancellationChargeAmount.Value,
                    Amount = b.CancellationChargeAmount.Value,
                });
            }
        }

        // ── Visits & additional travel: a mobilisation to a package outside the
        // contract's scope, or a visit beyond the included limit, bills its
        // travel time (hourly rate) and recharges its travel costs; within
        // the limit both are included in the contract.
        var visits = await WorkPackageRules.NumberVisitsAsync(db, project.ContractId, ct);
        var travelPeriods = await db.MobilisationPeriods.Include(p => p.WorkPackage)
            .Where(p => p.ProjectId == projectId && p.WorkPackage != null
                && p.StartDate <= to && p.EndDate >= from)
            .ToListAsync(ct);
        MobilisationPeriod? PeriodOn(Guid resourceId, DateOnly d) => travelPeriods
            .FirstOrDefault(p => p.ResourceId == resourceId && d >= p.StartDate && d <= p.EndDate);
        string TravelReason(MobilisationPeriod p) =>
            p.WorkPackage!.PricingMode == WorkPackagePricing.OutsideContract
                ? $"travel to {p.WorkPackage.Name}, outside the contract scope"
                : visits.TryGetValue(p.Id, out var v)
                    ? string.Create(Inv, $"visit {v.Number} — beyond the {v.Included} included visits")
                    : "additional travel";
        bool Billable(MobilisationPeriod? p) =>
            p is not null && WorkPackageRules.TravelBillable(p, visits.GetValueOrDefault(p.Id));
        bool IncludedVisit(MobilisationPeriod? p) =>
            p is not null && !Billable(p) && visits.ContainsKey(p.Id);

        var travelRows = await db.TravelRecords
            .Where(r => r.ProjectId == projectId && r.Date >= from && r.Date <= to
                && (r.DurationHours ?? 0m) > 0m
                && r.Timesheet != null
                && (r.Timesheet.Status == TimesheetStatus.Approved
                    || r.Timesheet.Status == TimesheetStatus.Invoiced))
            .OrderBy(r => r.Date).ToListAsync(ct);
        foreach (var r in travelRows.Where(r => !billedSources.Contains(r.Id)))
        {
            var p = travelPeriods.FirstOrDefault(x => x.Id == r.MobilisationPeriodId)
                ?? PeriodOn(r.ResourceId, r.Date);
            if (!Billable(p)) continue;
            var t = TermsAt(r.Date);
            if (!CurrencyOk(t?.Currency, string.Create(Inv, $"Travel {r.Date:yyyy-MM-dd}"))) continue;
            var (tGen, tRes) = OverridesAt(p!.WorkPackageId, r.ResourceId, r.Date);
            var rate = TimeMath.RateFor(TimeMath.KindOf(r.Date, t?.HolidayCalendar ?? "SE"),
                DayBilling.MergeRates(t, tGen, tRes, daily: false)) ?? 0m;
            var hours = r.DurationHours!.Value;
            spec.Lines.Add(new InvoiceLine
            {
                InvoiceSpecificationId = specId,
                Kind = InvoiceLineKind.Travel, Date = r.Date, SourceId = r.Id,
                Description = string.Create(Inv, $"Travel time {r.Date:yyyy-MM-dd}")
                    + (r.FromPlace is null && r.ToPlace is null ? "" : $" · {r.FromPlace} → {r.ToPlace}"),
                Explanation = string.Create(Inv, $"{hours:0.##} h travel × {rate:0.##} — ")
                    + TravelReason(p) + RateSource(tGen, tRes),
                Quantity = hours, UnitPrice = rate, Amount = TimeMath.Round2(hours * rate),
            });
        }

        // ── Recharged expenses with contract mark-up (§5: 1200 +10% → 1320).
        // Travel costs follow the visit rules above: recharged for billable
        // travel even unflagged, never for travel an included visit covers.
        var expenses = await db.Expenses
            .Where(x => x.ProjectId == projectId && x.Date >= from && x.Date <= to
                && !x.IncludedInRate
                && x.Timesheet != null
                && (x.Timesheet.Status == TimesheetStatus.Approved
                    || x.Timesheet.Status == TimesheetStatus.Invoiced))
            .ToListAsync(ct);
        foreach (var x in expenses.Where(x => !billedSources.Contains(x.Id)).OrderBy(x => x.Date))
        {
            var travelCost = x.Category is ExpenseCategory.Travel or ExpenseCategory.Accommodation;
            var period = travelCost ? PeriodOn(x.ResourceId, x.Date) : null;
            if (IncludedVisit(period))
            {
                if (x.RechargeableToCustomer)
                    Warn(string.Create(Inv,
                        $"Expense {x.Date:yyyy-MM-dd} ({x.Amount:0.##} {x.Currency}) is travel within an included visit — not recharged."));
                continue;
            }
            var viaTravel = Billable(period);
            if (!x.RechargeableToCustomer && !viaTravel) continue;
            // Foreign currency converts at the spec month's rate; the mark-up
            // (per category, else the default) applies to the converted amount.
            var foreign = x.Currency != spec.Currency;
            var rate = fx.RateFor(x.Currency, spec.Currency);
            if (rate is null)
            {
                Warn(fx.MissingRateWarning(x.Currency, spec.Currency));
                spec.MissingExchangeRate = true;
                continue;
            }
            var converted = foreign ? TimeMath.Round2(x.Amount * rate.Value) : x.Amount;
            var markup = TermsAt(x.Date)?.MarkupFor(x.Category) ?? 0m;
            var amount = TimeMath.Recharge(converted, markup);
            spec.Lines.Add(new InvoiceLine
            {
                InvoiceSpecificationId = specId,
                Kind = InvoiceLineKind.Expense, Date = x.Date, SourceId = x.Id,
                Description = x.Description ?? $"Expense {x.Category}",
                Explanation = (foreign
                        ? string.Create(Inv,
                            $"{x.Amount:0.##} {x.Currency} × {rate.Value:0.######} = {converted:0.##} {spec.Currency}")
                        : string.Create(Inv, $"{x.Amount:0.##} {x.Currency}"))
                    + string.Create(Inv, $" + {markup:0.##}% mark-up ({x.Category})")
                    + (viaTravel ? " · " + TravelReason(period!) : ""),
                Quantity = 1m, UnitPrice = amount, Amount = amount,
                OriginalAmount = foreign ? x.Amount : null,
                OriginalCurrency = foreign ? x.Currency : null,
                ExchangeRate = foreign ? rate : null,
            });
        }

        // ── One mobilisation fee per confirmed period, billed in the month the
        // period starts (a period crossing a month end is still one fee) —
        // package fee from the resource cell, then the package row, then contract.
        var periods = await db.MobilisationPeriods
            .Where(p => p.ProjectId == projectId && p.StartDate >= from && p.StartDate <= to)
            .OrderBy(p => p.StartDate).ToListAsync(ct);
        foreach (var p in periods.Where(p => !billedSources.Contains(p.Id)))
        {
            var (mGen, mRes) = OverridesAt(p.WorkPackageId, p.ResourceId, p.StartDate);
            var pkgFee = mRes?.MobilisationFee ?? mGen?.MobilisationFee;
            var t = TermsAt(p.StartDate);
            var fee = pkgFee ?? t?.MobilisationFee;
            if (fee is null or <= 0) continue;
            if (!CurrencyOk(t?.Currency, string.Create(Inv, $"Mobilisation {p.StartDate:yyyy-MM-dd}"))) continue;
            spec.Lines.Add(new InvoiceLine
            {
                InvoiceSpecificationId = specId,
                Kind = InvoiceLineKind.Mobilisation, Date = p.StartDate, SourceId = p.Id,
                Description = string.Create(Inv, $"Mobilisation {p.StartDate:yyyy-MM-dd} → {p.EndDate:yyyy-MM-dd}")
                    + (p.WorkPackageId is { } lid && wpNames.TryGetValue(lid, out var ln) ? $" · {ln}" : ""),
                Explanation = "Fixed fee per confirmed mobilisation period"
                    + (pkgFee is null ? "" : mRes?.MobilisationFee is not null
                        ? " · resource rate applies" : " · package rate applies"),
                Quantity = 1m, UnitPrice = fee.Value, Amount = fee.Value,
            });
        }

        if (isNew && supplement && spec.Lines.Count == 0 && marked == 0)
        {
            db.ChangeTracker.Clear();
            throw new BillingException("nothingToBill",
                "Every approved row of the month is already on an earlier revision.");
        }

        spec.TotalAmount = TimeMath.Round2(spec.Lines.Sum(l => l.Amount));
        spec.WarningsJson = warnings.Count > 0 ? JsonSerializer.Serialize(warnings) : null;
        await SaveOrConflictAsync(ct);
        return spec;
    }

    private async Task SaveOrConflictAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // A concurrent generate won the unique (scope, period) index.
            db.ChangeTracker.Clear();
            throw new BillingException("concurrentGenerate",
                "Another generation for the same period ran at the same time — reload and retry.");
        }
    }

    /// <summary>Timesheet → Invoiced leg: a sheet whose every counted entry and
    /// rechargeable expense sits on an issued/paid spec becomes Invoiced.</summary>
    public async Task RefreshInvoicedAsync(Guid specId, CancellationToken ct)
    {
        var expenseIds = await db.InvoiceLines
            .Where(l => l.InvoiceSpecificationId == specId && l.Kind == InvoiceLineKind.Expense
                && l.SourceId != null)
            .Select(l => l.SourceId!.Value).ToListAsync(ct);
        var sheetIds = await db.TimeEntries
            .Where(e => e.InvoiceSpecificationId == specId && e.TimesheetId != null)
            .Select(e => e.TimesheetId!.Value)
            .Concat(db.Expenses.Where(x => expenseIds.Contains(x.Id) && x.TimesheetId != null)
                .Select(x => x.TimesheetId!.Value))
            .Distinct().ToListAsync(ct);

        foreach (var sid in sheetIds)
        {
            var sheet = await db.Timesheets.FindAsync([sid], ct);
            if (sheet is null || sheet.Status != TimesheetStatus.Approved) continue;
            var openEntry = await db.TimeEntries.AnyAsync(e =>
                e.TimesheetId == sid && Counted.Contains(e.Category)
                && (e.InvoiceSpecificationId == null || !db.InvoiceSpecifications.Any(s =>
                    s.Id == e.InvoiceSpecificationId
                    && (s.Status == InvoiceStatus.Issued || s.Status == InvoiceStatus.Paid))), ct);
            var openExpense = await db.Expenses.AnyAsync(x =>
                x.TimesheetId == sid && x.RechargeableToCustomer && x.ProjectId != null
                && !db.InvoiceLines.Any(l => l.SourceId == x.Id
                    && (l.Invoice.Status == InvoiceStatus.Issued || l.Invoice.Status == InvoiceStatus.Paid)), ct);
            if (!openEntry && !openExpense) sheet.Status = TimesheetStatus.Invoiced;
        }
    }

    public record UnbilledRow(
        Guid ProjectId, string ProjectName, int Year, int Month,
        int EntryCount, decimal Hours, int ExpenseCount, bool HasFrozenSpec);

    /// <summary>Approved rows of a month that no specification billed yet —
    /// typically weeks approved after the month's spec left draft. Feeds the
    /// supplement flow and the month-end checklist.</summary>
    public async Task<List<UnbilledRow>> UnbilledAsync(
        int year, int month, Guid? projectId, CancellationToken ct)
    {
        var (from, to) = MonthRange(year, month);
        var entries = await db.TimeEntries
            .Where(e => e.Date >= from && e.Date <= to && e.InvoiceSpecificationId == null
                && (projectId == null || e.ProjectId == projectId)
                && e.Timesheet != null
                && (e.Timesheet.Status == TimesheetStatus.Approved
                    || e.Timesheet.Status == TimesheetStatus.Invoiced)
                && Counted.Contains(e.Category))
            .Select(e => new { e.ProjectId, e.ActualHours }).ToListAsync(ct);
        var expenses = await db.Expenses
            .Where(x => x.ProjectId != null && x.Date >= from && x.Date <= to
                && x.RechargeableToCustomer
                && (projectId == null || x.ProjectId == projectId)
                && x.Timesheet != null
                && (x.Timesheet.Status == TimesheetStatus.Approved
                    || x.Timesheet.Status == TimesheetStatus.Invoiced)
                && !db.InvoiceLines.Any(l => l.SourceId == x.Id))
            .Select(x => x.ProjectId!.Value).ToListAsync(ct);
        var ids = entries.Select(e => e.ProjectId).Concat(expenses).Distinct().ToList();
        var names = await db.Projects.Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var frozen = await db.InvoiceSpecifications
            .Where(s => ids.Contains(s.ProjectId) && s.Year == year && s.Month == month
                && s.Status != InvoiceStatus.Draft)
            .Select(s => s.ProjectId).Distinct().ToListAsync(ct);
        return ids.Select(id => new UnbilledRow(
                id, names.GetValueOrDefault(id, "?"), year, month,
                entries.Count(e => e.ProjectId == id),
                entries.Where(e => e.ProjectId == id).Sum(e => e.ActualHours),
                expenses.Count(e => e == id), frozen.Contains(id)))
            .OrderBy(r => r.ProjectName).ToList();
    }

    // ── Versions that billed history depends on (§3 "never rewritten") ──────

    /// <summary>Month-ends of the non-draft specs on the contract's projects.</summary>
    private async Task<List<(DateOnly From, DateOnly To)>> FrozenSpecMonthsAsync(
        Guid? contractId, Guid? projectId, CancellationToken ct)
    {
        var months = await db.InvoiceSpecifications
            .Where(s => s.Status != InvoiceStatus.Draft
                && (contractId == null || s.Project.ContractId == contractId)
                && (projectId == null || s.ProjectId == projectId))
            .Select(s => new { s.Year, s.Month }).Distinct().ToListAsync(ct);
        return months.Select(m => MonthRange(m.Year, m.Month)).ToList();
    }

    private async Task<List<(DateOnly From, DateOnly To)>> FrozenStatementMonthsAsync(
        Guid resourceId, CancellationToken ct)
    {
        var months = await db.SubcontractorStatements
            .Where(s => s.ResourceId == resourceId && s.Status != StatementStatus.Draft)
            .Select(s => new { s.Year, s.Month }).ToListAsync(ct);
        return months.Select(m => MonthRange(m.Year, m.Month)).ToList();
    }

    /// <summary>True when a version effective [from, nextFrom) may have priced
    /// a frozen month.</summary>
    private static bool Overlaps(
        List<(DateOnly From, DateOnly To)> frozen, DateOnly from, DateOnly? nextFrom) =>
        frozen.Any(m => m.To >= from && (nextFrom is null || m.From < nextFrom));

    /// <summary>The project a scheduled charge without one lands on: the
    /// contract's first active project by name (any project when none is active).</summary>
    public async Task<Guid?> DefaultChargeProjectAsync(Guid contractId, CancellationToken ct) =>
        await db.Projects.Where(p => p.ContractId == contractId)
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);

    /// <summary>True when the scheduled charge sits on a specification that
    /// has left draft — it can then no longer change or be deleted.</summary>
    public Task<bool> ChargeInvoicedAsync(Guid chargeId, CancellationToken ct) =>
        db.InvoiceLines.AnyAsync(l => l.SourceId == chargeId
            && l.Kind == InvoiceLineKind.ScheduledCharge
            && l.Invoice.Status != InvoiceStatus.Draft, ct);

    /// <summary>Null when the change is allowed; otherwise why not.</summary>
    public async Task<string?> TermsChangeBlockedAsync(
        Guid contractId, DateOnly effectiveFrom, Guid? deletingId, CancellationToken ct)
    {
        var frozen = await FrozenSpecMonthsAsync(contractId, null, ct);
        if (frozen.Count == 0) return null;
        if (deletingId is null)
            return frozen.Any(m => m.To >= effectiveFrom)
                ? "A non-draft invoice specification already covers that date — new terms must take effect after it."
                : null;
        var versions = await db.ContractTerms.Where(t => t.ContractId == contractId)
            .OrderBy(t => t.EffectiveFrom).ThenBy(t => t.Version).ToListAsync(ct);
        var idx = versions.FindIndex(v => v.Id == deletingId);
        var next = versions.Skip(idx + 1).FirstOrDefault(v => v.EffectiveFrom > versions[idx].EffectiveFrom);
        return Overlaps(frozen, versions[idx].EffectiveFrom, next?.EffectiveFrom)
            ? "These terms priced a non-draft invoice specification — they are part of billed history."
            : null;
    }

    public async Task<string?> AgreementChangeBlockedAsync(
        Guid resourceId, DateOnly effectiveFrom, Guid? deletingId, CancellationToken ct)
    {
        var frozen = await FrozenStatementMonthsAsync(resourceId, ct);
        if (frozen.Count == 0) return null;
        if (deletingId is null)
            return frozen.Any(m => m.To >= effectiveFrom)
                ? "A final statement already covers that date — a new agreement version must take effect after it."
                : null;
        var a = await db.CompensationAgreements.FirstAsync(x => x.Id == deletingId, ct);
        var next = await db.CompensationAgreements
            .Where(x => x.ResourceId == resourceId && x.ContractId == a.ContractId
                && x.WorkPackageId == a.WorkPackageId && x.EffectiveFrom > a.EffectiveFrom)
            .OrderBy(x => x.EffectiveFrom).Select(x => (DateOnly?)x.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
        return Overlaps(frozen, a.EffectiveFrom, next)
            ? "This agreement priced a final statement — it is part of paid history."
            : null;
    }

    public async Task<string?> PackageRateChangeBlockedAsync(
        Guid workPackageId, Guid? resourceId, DateOnly effectiveFrom, Guid? deletingId, CancellationToken ct)
    {
        var projectId = await db.WorkPackages.Where(w => w.Id == workPackageId)
            .Select(w => w.ProjectId).FirstAsync(ct);
        var frozen = await FrozenSpecMonthsAsync(null, projectId, ct);
        if (frozen.Count == 0) return null;
        if (deletingId is null)
            return frozen.Any(m => m.To >= effectiveFrom)
                ? "A non-draft invoice specification already covers that date — new rates must take effect after it."
                : null;
        var next = await db.WorkPackageRates
            .Where(r => r.WorkPackageId == workPackageId && r.ResourceId == resourceId
                && r.EffectiveFrom > effectiveFrom)
            .OrderBy(r => r.EffectiveFrom).Select(r => (DateOnly?)r.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
        return Overlaps(frozen, effectiveFrom, next)
            ? "These rates priced a non-draft invoice specification — they are part of billed history."
            : null;
    }

    // ── Subcontractor statement ─────────────────────────────────────────────

    /// <summary>(Re)builds the draft statement for resource × month: payable
    /// hours at agreement rates, reimbursable expenses, allowances, mileage
    /// and paid travel time — nothing customer-facing. The statement currency
    /// is the agreement's in force at month end; rows in another currency are
    /// left out with a warning.</summary>
    public async Task<SubcontractorStatement> GenerateStatementAsync(
        Guid resourceId, int year, int month, Guid? userId, CancellationToken ct)
    {
        var (from, to) = MonthRange(year, month);
        if (!await db.Resources.AnyAsync(r => r.Id == resourceId, ct))
            throw new BillingException("unknownResource", "Unknown resource.");

        var st = await db.SubcontractorStatements.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.ResourceId == resourceId && s.Year == year && s.Month == month, ct);
        if (st is { Status: not StatementStatus.Draft })
            throw new BillingException("statementFinal", "The statement is final — corrections need an adjustment.");
        if (st is null)
        {
            st = new SubcontractorStatement
            {
                ResourceId = resourceId, Year = year, Month = month,
                Currency = "SEK", CreatedByUserId = userId,
            };
            db.SubcontractorStatements.Add(st);
        }
        db.StatementLines.RemoveRange(st.Lines);
        st.Lines.Clear();

        var warnings = new List<string>();
        void Warn(string w) { if (!warnings.Contains(w)) warnings.Add(w); }

        var agreements = await db.CompensationAgreements
            .Where(a => a.ResourceId == resourceId).ToListAsync(ct);
        var currencyAgreement = agreements
            .Where(a => a.EffectiveFrom <= to)
            .OrderBy(a => a.ContractId != null).ThenBy(a => a.WorkPackageId != null)
            .ThenByDescending(a => a.EffectiveFrom).ThenByDescending(a => a.Version)
            .FirstOrDefault();
        st.Currency = currencyAgreement?.Currency ?? st.Currency;
        st.MissingExchangeRate = false;
        var fx = await CurrencyConverter.ForMonthAsync(db, year, month, ct);
        bool CurrencyOk(string? currency, string what)
        {
            if (currency is null || currency == st.Currency) return true;
            Warn($"{what} is in {currency}, not {st.Currency} — left out of the total.");
            return false;
        }
        // A row reported in its own currency (expense paid abroad, per diem in
        // EUR) converts at the month rate; the original stays on the line.
        StatementLine? Converted(StatementLineKind kind, DateOnly date, Guid sourceId,
            string description, string explanation, decimal amount, string currency)
        {
            var rate = fx.RateFor(currency, st.Currency);
            if (rate is null)
            {
                Warn(fx.MissingRateWarning(currency, st.Currency));
                st.MissingExchangeRate = true;
                return null;
            }
            var foreign = currency != st.Currency;
            var value = foreign ? TimeMath.Round2(amount * rate.Value) : amount;
            return new StatementLine
            {
                StatementId = st.Id, Kind = kind, Date = date, SourceId = sourceId,
                Description = description,
                Explanation = foreign
                    ? string.Create(Inv, $"{explanation} · {amount:0.##} {currency} × {rate.Value:0.######}")
                    : explanation,
                Quantity = 1m, UnitPrice = value, Amount = value,
                OriginalAmount = foreign ? amount : null,
                OriginalCurrency = foreign ? currency : null,
                ExchangeRate = foreign ? rate : null,
            };
        }

        var rows = await MonthRowsAsync([resourceId], from, to, ct);
        var contractIds = rows.Select(r => r.ContractId).Distinct().ToList();
        var allTerms = await db.ContractTerms
            .Where(t => contractIds.Contains(t.ContractId)).ToListAsync(ct);
        var projectIds = rows.Select(r => r.ProjectId).Distinct().ToList();
        var projectNames = await db.Projects.Where(p => projectIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var wpIds = rows.Where(r => r.WorkPackageId != null).Select(r => r.WorkPackageId!.Value)
            .Distinct().ToList();
        var wpNames = await db.WorkPackages.Where(w => wpIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Name, ct);

        // ── Hours per day: one shared minimum/OT pool, each (project, package)
        // part at its own agreement.
        foreach (var g in rows.GroupBy(e => e.Date).OrderBy(g => g.Key))
        {
            var date = g.Key;
            var parts = DayBilling.PayDay(date, g.Select(r => r.Day).ToList(),
                (c, wp) => DayBilling.AgreementAt(agreements, c, wp, date),
                c => DayBilling.TermsAt(allTerms, c, date)?.HolidayCalendar);
            foreach (var part in parts)
            {
                if (part.Line.EffectiveHours == 0m && part.Line.Amount == 0m) continue;
                if (!CurrencyOk(part.Agreement?.Currency, string.Create(Inv, $"Hours on {date:yyyy-MM-dd}")))
                    continue;
                var pkg = part.WorkPackageId is { } wid ? wpNames.GetValueOrDefault(wid) : null;
                st.Lines.Add(new StatementLine
                {
                    StatementId = st.Id,
                    Kind = StatementLineKind.Hours, Date = date,
                    Description = string.Create(Inv,
                        $"{projectNames.GetValueOrDefault(part.ProjectId, "?")}{(pkg is null ? "" : $" · {pkg}")} — {date:yyyy-MM-dd}"),
                    Explanation = part.Explanation,
                    Quantity = part.Line.EffectiveHours, UnitPrice = part.UnitPrice, Amount = part.Line.Amount,
                });
            }
        }

        // ── Reimbursable expenses at cost (mark-up is the customer's side).
        var expenses = await db.Expenses
            .Where(x => x.ResourceId == resourceId && x.Date >= from && x.Date <= to
                && x.ReimbursableToResource
                && x.Timesheet != null
                && (x.Timesheet.Status == TimesheetStatus.Approved
                    || x.Timesheet.Status == TimesheetStatus.Invoiced))
            .ToListAsync(ct);
        foreach (var x in expenses.OrderBy(x => x.Date))
        {
            if (Converted(StatementLineKind.Expense, x.Date, x.Id,
                    x.Description ?? $"Expense {x.Category}", "Reimbursed at cost",
                    x.Amount, x.Currency) is { } line)
                st.Lines.Add(line);
        }

        // ── Per diem claims at their claimed amounts.
        var allowances = await db.AllowanceClaims
            .Where(x => x.ResourceId == resourceId && x.Date >= from && x.Date <= to
                && x.Timesheet != null
                && (x.Timesheet.Status == TimesheetStatus.Approved
                    || x.Timesheet.Status == TimesheetStatus.Invoiced))
            .ToListAsync(ct);
        foreach (var x in allowances.OrderBy(x => x.Date))
        {
            if (Converted(StatementLineKind.Allowance, x.Date, x.Id,
                    x.Notes ?? "Per diem", "Per diem claim", x.Amount, x.Currency) is { } line)
                st.Lines.Add(line);
        }

        // ── Mileage + paid travel time per the resource's agreement.
        var travel = await db.TravelRecords
            .Where(x => x.ResourceId == resourceId && x.Date >= from && x.Date <= to
                && x.Timesheet != null
                && (x.Timesheet.Status == TimesheetStatus.Approved
                    || x.Timesheet.Status == TimesheetStatus.Invoiced))
            .Select(x => new { x.Id, x.Date, x.DistanceKm, x.DurationHours,
                ContractId = x.Project != null ? (Guid?)x.Project.ContractId : null })
            .ToListAsync(ct);
        foreach (var x in travel.OrderBy(x => x.Date))
        {
            var a = DayBilling.AgreementAt(agreements, x.ContractId ?? Guid.Empty, null, x.Date);
            if (a is null) continue;
            if (!CurrencyOk(a.Currency, string.Create(Inv, $"Travel {x.Date:yyyy-MM-dd}"))) continue;

            if (x.DistanceKm is > 0 && a.MileageRate is > 0)
            {
                var amount = TimeMath.Round2(x.DistanceKm.Value * a.MileageRate.Value);
                st.Lines.Add(new StatementLine
                {
                    StatementId = st.Id,
                    Kind = StatementLineKind.Mileage, Date = x.Date, SourceId = x.Id,
                    Description = string.Create(Inv, $"Mileage {x.DistanceKm:0.##} km"),
                    Explanation = string.Create(Inv, $"{x.DistanceKm:0.##} km × {a.MileageRate.Value:0.##}"),
                    Quantity = x.DistanceKm.Value, UnitPrice = a.MileageRate.Value, Amount = amount,
                });
            }
            if (x.DurationHours is > 0 && a.TravelTimePercent is > 0 && a.HourlyRate is > 0)
            {
                var rate = TimeMath.Round2(a.HourlyRate.Value * a.TravelTimePercent.Value / 100m);
                var amount = TimeMath.Round2(x.DurationHours.Value * rate);
                st.Lines.Add(new StatementLine
                {
                    StatementId = st.Id,
                    Kind = StatementLineKind.TravelTime, Date = x.Date, SourceId = x.Id,
                    Description = string.Create(Inv, $"Travel time {x.DurationHours:0.##} h"),
                    Explanation = string.Create(Inv,
                        $"{x.DurationHours:0.##} h × {a.TravelTimePercent:0.##}% of hourly"),
                    Quantity = x.DurationHours.Value, UnitPrice = rate, Amount = amount,
                });
            }
        }

        st.TotalAmount = TimeMath.Round2(st.Lines.Sum(l => l.Amount));
        st.WarningsJson = warnings.Count > 0 ? JsonSerializer.Serialize(warnings) : null;
        await SaveOrConflictAsync(ct);
        return st;
    }

    // ── Project direct cost (margin reporting, §8) ──────────────────────────

    public record CostBreakdown(
        string Currency, decimal Hours, decimal Expenses, decimal Allowances,
        decimal Mileage, decimal TravelTime, decimal Total);

    /// <summary>Direct cost of one project in a month, per currency — the
    /// payable side (agreement rates) of the approved rows hanging on the
    /// project, priced exactly as the statement prices them. Read-only.</summary>
    public async Task<List<CostBreakdown>> ProjectCostAsync(
        Guid projectId, int year, int month, CancellationToken ct)
    {
        var (from, to) = MonthRange(year, month);
        var contractId = await db.Projects.Where(p => p.Id == projectId)
            .Select(p => p.ContractId).FirstOrDefaultAsync(ct);
        var resourceIds = await db.TimeEntries
            .Where(e => e.ProjectId == projectId && e.Date >= from && e.Date <= to
                && e.Timesheet != null
                && (e.Timesheet.Status == TimesheetStatus.Approved
                    || e.Timesheet.Status == TimesheetStatus.Invoiced))
            .Select(e => e.ResourceId)
            .Concat(db.TravelRecords.Where(x => x.ProjectId == projectId && x.Date >= from && x.Date <= to)
                .Select(x => x.ResourceId))
            .Distinct().ToListAsync(ct);
        var agreements = await db.CompensationAgreements
            .Where(a => resourceIds.Contains(a.ResourceId)).ToListAsync(ct);

        var acc = new Dictionary<string, decimal[]>(); // hours, expenses, allowances, mileage, travel
        void Add(string? currency, int slot, decimal amount)
        {
            var key = currency ?? "SEK";
            if (!acc.TryGetValue(key, out var v)) acc[key] = v = new decimal[5];
            v[slot] += amount;
        }

        var rows = await MonthRowsAsync(resourceIds, from, to, ct);
        var contractIds = rows.Select(r => r.ContractId).Distinct().ToList();
        var allTerms = await db.ContractTerms
            .Where(t => contractIds.Contains(t.ContractId)).ToListAsync(ct);
        foreach (var g in rows.GroupBy(e => (e.ResourceId, e.Date))
                     .Where(g => g.Any(r => r.ProjectId == projectId)))
        {
            var (resourceId, date) = g.Key;
            var mine = agreements.Where(a => a.ResourceId == resourceId).ToList();
            foreach (var part in DayBilling.PayDay(date, g.Select(r => r.Day).ToList(),
                         (c, wp) => DayBilling.AgreementAt(mine, c, wp, date),
                         c => DayBilling.TermsAt(allTerms, c, date)?.HolidayCalendar)
                     .Where(p => p.ProjectId == projectId))
                Add(part.Agreement?.Currency, 0, part.Line.Amount);
        }

        var expenses = await db.Expenses
            .Where(x => x.ProjectId == projectId && x.Date >= from && x.Date <= to
                && x.ReimbursableToResource
                && x.Timesheet != null
                && (x.Timesheet.Status == TimesheetStatus.Approved
                    || x.Timesheet.Status == TimesheetStatus.Invoiced))
            .Select(x => new { x.Currency, x.Amount }).ToListAsync(ct);
        foreach (var x in expenses) Add(x.Currency, 1, x.Amount);
        var allowances = await db.AllowanceClaims
            .Where(x => x.ProjectId == projectId && x.Date >= from && x.Date <= to
                && x.Timesheet != null
                && (x.Timesheet.Status == TimesheetStatus.Approved
                    || x.Timesheet.Status == TimesheetStatus.Invoiced))
            .Select(x => new { x.Currency, x.Amount }).ToListAsync(ct);
        foreach (var x in allowances) Add(x.Currency, 2, x.Amount);

        var travel = await db.TravelRecords
            .Where(x => x.ProjectId == projectId && x.Date >= from && x.Date <= to
                && x.Timesheet != null
                && (x.Timesheet.Status == TimesheetStatus.Approved
                    || x.Timesheet.Status == TimesheetStatus.Invoiced))
            .ToListAsync(ct);
        foreach (var x in travel)
        {
            var a = DayBilling.AgreementAt(
                agreements.Where(y => y.ResourceId == x.ResourceId), contractId, null, x.Date);
            if (a is null) continue;
            if (x.DistanceKm is > 0 && a.MileageRate is > 0)
                Add(a.Currency, 3, TimeMath.Round2(x.DistanceKm.Value * a.MileageRate.Value));
            if (x.DurationHours is > 0 && a.TravelTimePercent is > 0 && a.HourlyRate is > 0)
                Add(a.Currency, 4, TimeMath.Round2(
                    x.DurationHours.Value * TimeMath.Round2(a.HourlyRate.Value * a.TravelTimePercent.Value / 100m)));
        }

        return acc.OrderBy(k => k.Key).Select(k => new CostBreakdown(
                k.Key, TimeMath.Round2(k.Value[0]), k.Value[1], k.Value[2], k.Value[3], k.Value[4],
                TimeMath.Round2(k.Value.Sum())))
            .ToList();
    }
}
