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
public class BillingService(AppDbContext db)
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
        Guid? InvoiceSpecificationId)
    {
        public DayRow Day => new(Id, ProjectId, ContractId, WorkPackageId, Billing, Booked, Hours);
    }

    /// <summary>All approved/invoiced counted rows of the given resources in
    /// [from, to] — every project, so a day prices with its full context.</summary>
    private Task<List<MonthRow>> MonthRowsAsync(
        IReadOnlyCollection<Guid> resourceIds, DateOnly from, DateOnly to, CancellationToken ct) =>
        db.TimeEntries
            .Where(e => resourceIds.Contains(e.ResourceId) && e.Date >= from && e.Date <= to
                && e.Timesheet != null
                && (e.Timesheet.Status == TimesheetStatus.Approved
                    || e.Timesheet.Status == TimesheetStatus.Invoiced)
                && Counted.Contains(e.Category))
            .Select(e => new MonthRow(
                e.Id, e.ResourceId, e.Date, e.ProjectId, e.Project.ContractId,
                e.BookingId != null ? e.Booking!.WorkPackageId : e.WorkPackageId,
                e.Billing, e.BookingId != null, e.ActualHours, e.InvoiceSpecificationId))
            .ToListAsync(ct);

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
        var project = await db.Projects.Include(p => p.Contract)
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
        {
            var tx = await db.PadTransactions
                .Where(t => t.ProjectId == projectId && t.Year == year && t.Month == month)
                .ToListAsync(ct);
            var sums = PadMath.Sum(tx);
            var billedPad = others.SelectMany(s => s.Lines)
                .Where(l => l.Kind == InvoiceLineKind.Pad).Sum(l => l.Quantity);
            var remaining = sums.Utilised - billedPad;
            if (remaining > 0m && billedPad == 0m)
            {
                // Approved Additional PAD carrying its own rate bills the
                // utilisation above the month's own allocation at that rate.
                var over = Math.Max(0m, sums.Utilised - (sums.Baseline + sums.CarryOverIn + sums.Adjustments));
                var rated = await db.AdditionalPadRequests
                    .Where(r => r.ProjectId == projectId && r.Year == year && r.Month == month
                        && r.Status == AdditionalPadStatus.Approved && r.Rate != null)
                    .OrderBy(r => r.CreatedUtc).ToListAsync(ct);
                foreach (var r in rated)
                {
                    var take = Math.Min(Math.Min(over, r.Quantity), remaining);
                    if (take <= 0m) break;
                    spec.Lines.Add(new InvoiceLine
                    {
                        InvoiceSpecificationId = specId,
                        Kind = InvoiceLineKind.Pad, Date = to,
                        Description = string.Create(Inv, $"Additional PAD {year}-{month:00}")
                            + (r.Reference is null ? "" : $" · {r.Reference}"),
                        Explanation = string.Create(Inv,
                            $"{take:0.##} additional PAD × {r.Rate!.Value:0.##} {spec.Currency} (approved request rate)"),
                        Quantity = take, UnitPrice = r.Rate.Value,
                        Amount = TimeMath.Round2(take * r.Rate.Value),
                    });
                    over -= take; remaining -= take;
                }
            }
            if (remaining > 0m)
            {
                if (padTerms.PadRate is > 0m)
                    spec.Lines.Add(new InvoiceLine
                    {
                        InvoiceSpecificationId = specId,
                        Kind = InvoiceLineKind.Pad, Date = to,
                        Description = string.Create(Inv, $"PAD utilised {year}-{month:00}"),
                        Explanation = string.Create(Inv,
                            $"{remaining:0.##} PAD utilised × {padTerms.PadRate.Value:0.##} {spec.Currency}")
                            + (billedPad > 0m ? string.Create(Inv, $" (after {billedPad:0.##} PAD on earlier revisions)") : ""),
                        Quantity = remaining, UnitPrice = padTerms.PadRate.Value,
                        Amount = TimeMath.Round2(remaining * padTerms.PadRate.Value),
                    });
                else
                    Warn(string.Create(Inv,
                        $"The PAD component is enabled but has no PAD rate — {remaining:0.##} utilised PAD not billed."));
            }
        }

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

        // ── Fixed-price component: one-off amount on its configured month.
        if (endTerms?.FixedPriceEnabled == true && endTerms.FixedPriceAmount is > 0m
            && endTerms.FixedPriceDate is { } fpDate && fpDate >= from && fpDate <= to
            && !others.SelectMany(s => s.Lines).Any(l => l.Kind == InvoiceLineKind.FixedPrice)
            && CurrencyOk(endTerms.Currency, "The fixed price"))
        {
            spec.Lines.Add(new InvoiceLine
            {
                InvoiceSpecificationId = specId,
                Kind = InvoiceLineKind.FixedPrice, Date = fpDate,
                Description = endTerms.FixedPriceNote
                    ?? string.Create(Inv, $"Fixed price {year}-{month:00}"),
                Explanation = "Fixed-price contract component",
                Quantity = 1m, UnitPrice = endTerms.FixedPriceAmount.Value,
                Amount = endTerms.FixedPriceAmount.Value,
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
                    || (b.CancellationChargeAmount ?? 0m) > 0m))
            .ToListAsync(ct);
        foreach (var b in cancellations.Where(b => !billedSources.Contains(b.Id)).OrderBy(x => x.Date))
        {
            var t = TermsAt(b.Date);
            if (!CurrencyOk(t?.Currency, string.Create(Inv, $"Cancellation {b.Date:yyyy-MM-dd}"))) continue;
            var (cGen, cRes) = OverridesAt(b.WorkPackageId, b.ResourceId, b.Date);
            if (b.CancellationChargeHours is > 0m)
            {
                var rates = DayBilling.MergeRates(t, cGen, cRes, t?.TimeBilling == TimeBilling.DailyRate);
                var kind = TimeMath.KindOf(b.Date, t?.HolidayCalendar ?? "SE");
                decimal qty, unit;
                if (t?.TimeBilling == TimeBilling.DailyRate)
                {
                    // Hours charge priced as a fraction of the day rate.
                    var norm = t.OvertimeAfterHours > 0 ? t.OvertimeAfterHours : 8;
                    qty = Math.Round(b.CancellationChargeHours.Value / norm, 4);
                    unit = TimeMath.DayRateFor(kind, rates) ?? 0m;
                }
                else
                {
                    qty = b.CancellationChargeHours.Value;
                    unit = TimeMath.RateFor(kind, rates) ?? 0m;
                }
                spec.Lines.Add(new InvoiceLine
                {
                    InvoiceSpecificationId = specId,
                    Kind = InvoiceLineKind.Cancellation, Date = b.Date, SourceId = b.Id,
                    Description = string.Create(Inv, $"Cancellation {b.Date:yyyy-MM-dd}"),
                    Explanation = string.Create(Inv,
                            $"{b.CancellationChargeHours.Value:0.##} h cancellation charge")
                        + RateSource(cGen, cRes),
                    Quantity = qty, UnitPrice = unit,
                    Amount = TimeMath.Round2(qty * unit),
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

        // ── Recharged expenses with contract mark-up (§5: 1200 +10% → 1320).
        var expenses = await db.Expenses
            .Where(x => x.ProjectId == projectId && x.Date >= from && x.Date <= to
                && x.RechargeableToCustomer
                && x.Timesheet != null
                && (x.Timesheet.Status == TimesheetStatus.Approved
                    || x.Timesheet.Status == TimesheetStatus.Invoiced))
            .ToListAsync(ct);
        foreach (var x in expenses.Where(x => !billedSources.Contains(x.Id)).OrderBy(x => x.Date))
        {
            if (!CurrencyOk(x.Currency, string.Create(Inv, $"Expense {x.Date:yyyy-MM-dd} ({x.Amount:0.##})")))
                continue;
            var markup = TermsAt(x.Date)?.ExpenseMarkupPercent ?? 0m;
            var amount = TimeMath.Recharge(x.Amount, markup);
            spec.Lines.Add(new InvoiceLine
            {
                InvoiceSpecificationId = specId,
                Kind = InvoiceLineKind.Expense, Date = x.Date, SourceId = x.Id,
                Description = x.Description ?? $"Expense {x.Category}",
                Explanation = string.Create(Inv, $"{x.Amount:0.##} {x.Currency} + {markup:0.##}% mark-up"),
                Quantity = 1m, UnitPrice = amount, Amount = amount,
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
        bool CurrencyOk(string? currency, string what)
        {
            if (currency is null || currency == st.Currency) return true;
            Warn($"{what} is in {currency}, not {st.Currency} — left out of the total.");
            return false;
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
            if (!CurrencyOk(x.Currency, string.Create(Inv, $"Expense {x.Date:yyyy-MM-dd} ({x.Amount:0.##})")))
                continue;
            st.Lines.Add(new StatementLine
            {
                StatementId = st.Id,
                Kind = StatementLineKind.Expense, Date = x.Date, SourceId = x.Id,
                Description = x.Description ?? $"Expense {x.Category}",
                Explanation = "Reimbursed at cost",
                Quantity = 1m, UnitPrice = x.Amount, Amount = x.Amount,
            });
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
            if (!CurrencyOk(x.Currency, string.Create(Inv, $"Per diem {x.Date:yyyy-MM-dd}"))) continue;
            st.Lines.Add(new StatementLine
            {
                StatementId = st.Id,
                Kind = StatementLineKind.Allowance, Date = x.Date, SourceId = x.Id,
                Description = x.Notes ?? "Per diem",
                Explanation = "Per diem claim",
                Quantity = 1m, UnitPrice = x.Amount, Amount = x.Amount,
            });
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
