using System.Globalization;
using ContractFlow.Api.Data;
using ContractFlow.Api.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ContractFlow.Api.Services;

/// <summary>
/// Builds the money documents (design §6.1 "month-end close → customer invoice
/// spec + subcontractor statement"). Both generators walk the approved
/// timesheets through <see cref="TimeMath"/> — billing with contract-terms
/// rates, compensation with agreement rates — so ledger 3 and ledger 4 never
/// share a rate source. Every line carries its explanation (§5).
/// </summary>
public class BillingService(AppDbContext db)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly HashSet<TimeEntryCategory> Counted =
        [TimeEntryCategory.Work, TimeEntryCategory.Waiting, TimeEntryCategory.Standby];

    private static (DateOnly From, DateOnly To) MonthRange(int year, int month) =>
        (new DateOnly(year, month, 1), new DateOnly(year, month, 1).AddMonths(1).AddDays(-1));

    // ── Supplier-invoice matching (§6.2) ────────────────────────────────────

    public record MatchDiff(string Kind, decimal Invoiced, decimal Statement, decimal Diff);

    /// <summary>Compares the supplier invoice's lines per kind against the
    /// statement's — the statement is the authority. Tolerance 0.01 for
    /// rounding-only noise.</summary>
    public static List<MatchDiff> MatchInvoice(
        IEnumerable<SupplierInvoiceLine> invoiceLines,
        IEnumerable<StatementLine> statementLines)
    {
        var invoiced = invoiceLines
            .GroupBy(l => l.Kind).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        var statement = statementLines
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

    // ── Customer invoice specification ──────────────────────────────────────

    /// <summary>(Re)builds the draft spec for project × month from approved
    /// timesheets. Existing draft lines are replaced; a reviewed/issued spec
    /// is frozen — corrections need a revision (not yet implemented).</summary>
    public async Task<InvoiceSpecification> GenerateInvoiceAsync(
        Guid projectId, int year, int month, Guid? userId, CancellationToken ct)
    {
        var (from, to) = MonthRange(year, month);
        var project = await db.Projects.Include(p => p.Contract)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new InvalidOperationException("Unknown project.");

        var spec = await db.InvoiceSpecifications.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Year == year && s.Month == month, ct);
        if (spec is { Status: not InvoiceStatus.Draft })
            throw new InvalidOperationException("The specification has left draft — create a revision instead.");
        if (spec is null)
        {
            spec = new InvoiceSpecification
            {
                ProjectId = projectId, Year = year, Month = month,
                Currency = "SEK", CreatedByUserId = userId,
            };
            db.InvoiceSpecifications.Add(spec);
        }
        db.InvoiceLines.RemoveRange(spec.Lines);
        spec.Lines.Clear();

        var terms = await db.ContractTerms
            .Where(t => t.ContractId == project.ContractId).ToListAsync(ct);
        var termsAt = (DateOnly d) => terms
            .Where(t => t.EffectiveFrom <= d)
            .OrderByDescending(t => t.EffectiveFrom).ThenByDescending(t => t.Version)
            .FirstOrDefault();
        spec.Currency = termsAt(to)?.Currency ?? spec.Currency;

        // ── PAD component: the month bills utilised PAD × the PAD rate —
        // booked days are already inside the ledger, so their hours never
        // price twice (design §4/§5 — billing component on the terms version
        // in force at month end).
        var endTerms = termsAt(to);
        if (endTerms?.PadEnabled == true && endTerms.PadRate is > 0m)
        {
            var utilised = await db.PadTransactions
                .Where(t => t.ProjectId == projectId && t.Year == year && t.Month == month
                    && t.Type == PadTransactionType.Utilise)
                .SumAsync(t => t.Amount, ct);
            if (utilised > 0m)
                spec.Lines.Add(new InvoiceLine
                {
                    Id = Guid.NewGuid(), InvoiceSpecificationId = spec.Id,
                    Kind = InvoiceLineKind.Pad, Date = to,
                    Description = string.Create(Inv, $"PAD utilised {year}-{month:00}"),
                    Explanation = string.Create(Inv,
                        $"{utilised:0.##} PAD utilised × {endTerms.PadRate.Value:0.##} {spec.Currency}"),
                    Quantity = utilised, UnitPrice = endTerms.PadRate.Value,
                    Amount = TimeMath.Round2(utilised * endTerms.PadRate.Value),
                });
        }

        // ── Time billing: re-run the billable pipeline per resource×day, keep
        // this project's share. Under a PAD component, booking-bound entries
        // are covered by the PAD line — only standalone rows bill per time.
        var dayGroups = await db.TimeEntries
            .Where(e => e.ProjectId == projectId && e.Date >= from && e.Date <= to
                && e.Timesheet != null && e.Timesheet.Status == TimesheetStatus.Approved
                && Counted.Contains(e.Category))
            .GroupBy(e => new { e.ResourceId, e.Date })
            .Select(g => new { g.Key.ResourceId, g.Key.Date })
            .ToListAsync(ct);

        foreach (var g in dayGroups.OrderBy(x => x.Date).ThenBy(x => x.ResourceId))
        {
            var t = termsAt(g.Date);
            var padCovers = t?.PadEnabled == true;
            var mode = t?.TimeBilling ?? TimeBilling.Hourly;
            if (mode == TimeBilling.None) continue;

            // The day was priced across ALL its projects at approval — replay
            // with every approved row of the day so the split matches.
            var allDay = await db.TimeEntries
                .Where(e => e.ResourceId == g.ResourceId && e.Date == g.Date
                    && e.Timesheet != null && e.Timesheet.Status == TimesheetStatus.Approved
                    && Counted.Contains(e.Category))
                .Select(e => new { e.ProjectId, e.ActualHours, e.BookingId })
                .ToListAsync(ct);
            var splits = allDay
                .Where(x => !(padCovers && x.BookingId != null))
                .GroupBy(x => x.ProjectId)
                .Select(x => (ProjectId: x.Key, Hours: x.Sum(e => e.ActualHours))).ToList();
            if (splits.All(s => s.ProjectId != projectId)) continue;

            var daily = mode == TimeBilling.DailyRate;
            var rates = new DayRateSet(
                t?.HourlyRate, t?.SaturdayRate, t?.SundayRate, t?.HolidayRate,
                t?.OvertimeAfterHours ?? 8, t?.OvertimePercent ?? 150m,
                t?.MinBillableHoursPerDay,
                DayRate: daily ? t?.DayRate : null,
                SaturdayDayRate: daily ? t?.SaturdayDayRate : null,
                SundayDayRate: daily ? t?.SundayDayRate : null,
                HolidayDayRate: daily ? t?.HolidayDayRate : null,
                HalfDayRate: daily ? t?.HalfDayRate : null,
                HalfDayMaxHours: daily ? t?.HalfDayMaxHours : null);
            var day = TimeMath.CalculateDay(g.Date, splits, rates, t?.HolidayCalendar ?? "SE");
            var line = day.Lines.FirstOrDefault(l => l.ProjectId == projectId);
            if (line is null || line.Amount == 0m && line.EffectiveHours == 0m) continue;

            var rate = daily
                ? (line.DayFraction > 0m ? line.Amount / line.DayFraction : 0m)
                : TimeMath.RateFor(day.Kind, rates) ?? 0m;
            spec.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(), InvoiceSpecificationId = spec.Id,
                Kind = InvoiceLineKind.Hours, Date = g.Date,
                Description = daily
                    ? string.Create(Inv, $"Day {g.Date:yyyy-MM-dd}")
                    : string.Create(Inv, $"Work {g.Date:yyyy-MM-dd}"),
                Explanation = day.Explanation,
                Quantity = daily ? line.DayFraction : line.EffectiveHours,
                UnitPrice = rate, Amount = line.Amount,
            });
        }

        // ── Fixed-price component: one-off amount on its configured month.
        if (endTerms?.FixedPriceEnabled == true && endTerms.FixedPriceAmount is > 0m
            && endTerms.FixedPriceDate is { } fpDate && fpDate >= from && fpDate <= to)
        {
            spec.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(), InvoiceSpecificationId = spec.Id,
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
        foreach (var b in cancellations.OrderBy(x => x.Date))
        {
            var t = termsAt(b.Date);
            if (b.CancellationChargeHours is > 0m)
            {
                var rates = new DayRateSet(
                    t?.HourlyRate, t?.SaturdayRate, t?.SundayRate, t?.HolidayRate,
                    t?.OvertimeAfterHours ?? 8, t?.OvertimePercent ?? 150m, null,
                    DayRate: t?.DayRate,
                    SaturdayDayRate: t?.SaturdayDayRate,
                    SundayDayRate: t?.SundayDayRate,
                    HolidayDayRate: t?.HolidayDayRate);
                var kind = TimeMath.KindOf(b.Date, t?.HolidayCalendar ?? "SE");
                decimal qty, unit;
                if (t?.TimeBilling == TimeBilling.DailyRate)
                {
                    // Hours charge priced as a fraction of the day rate.
                    var norm = t.OvertimeAfterHours > 0 ? t.OvertimeAfterHours : 8;
                    qty = b.CancellationChargeHours.Value / norm;
                    unit = TimeMath.DayRateFor(kind, rates) ?? 0m;
                }
                else
                {
                    qty = b.CancellationChargeHours.Value;
                    unit = TimeMath.RateFor(kind, rates) ?? 0m;
                }
                spec.Lines.Add(new InvoiceLine
                {
                    Id = Guid.NewGuid(), InvoiceSpecificationId = spec.Id,
                    Kind = InvoiceLineKind.Cancellation, Date = b.Date,
                    Description = string.Create(Inv, $"Cancellation {b.Date:yyyy-MM-dd}"),
                    Explanation = string.Create(Inv,
                        $"{b.CancellationChargeHours.Value:0.##} h cancellation charge"),
                    Quantity = qty, UnitPrice = unit,
                    Amount = TimeMath.Round2(qty * unit),
                });
            }
            if (b.CancellationChargeAmount is > 0m)
            {
                spec.Lines.Add(new InvoiceLine
                {
                    Id = Guid.NewGuid(), InvoiceSpecificationId = spec.Id,
                    Kind = InvoiceLineKind.Cancellation, Date = b.Date,
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
                && x.Timesheet != null && x.Timesheet.Status == TimesheetStatus.Approved)
            .ToListAsync(ct);
        foreach (var x in expenses.OrderBy(x => x.Date))
        {
            var markup = termsAt(x.Date)?.ExpenseMarkupPercent ?? 0m;
            var amount = TimeMath.Recharge(x.Amount, markup);
            spec.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(), InvoiceSpecificationId = spec.Id,
                Kind = InvoiceLineKind.Expense, Date = x.Date, SourceId = x.Id,
                Description = x.Description ?? $"Expense {x.Category}",
                Explanation = string.Create(Inv, $"{x.Amount:0.##} {x.Currency} + {markup:0.##}% mark-up"),
                Quantity = 1m, UnitPrice = amount, Amount = amount,
            });
        }

        // ── One mobilisation fee per confirmed period overlapping the month.
        var periods = await db.MobilisationPeriods
            .Where(p => p.ProjectId == projectId && p.StartDate <= to && p.EndDate >= from)
            .OrderBy(p => p.StartDate).ToListAsync(ct);
        foreach (var p in periods)
        {
            var fee = termsAt(p.StartDate)?.MobilisationFee;
            if (fee is null or <= 0) continue;
            spec.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(), InvoiceSpecificationId = spec.Id,
                Kind = InvoiceLineKind.Mobilisation, Date = p.StartDate, SourceId = p.Id,
                Description = string.Create(Inv, $"Mobilisation {p.StartDate:yyyy-MM-dd} → {p.EndDate:yyyy-MM-dd}"),
                Explanation = "Fixed fee per confirmed mobilisation period",
                Quantity = 1m, UnitPrice = fee.Value, Amount = fee.Value,
            });
        }

        spec.TotalAmount = TimeMath.Round2(spec.Lines.Sum(l => l.Amount));
        await db.SaveChangesAsync(ct);
        return spec;
    }

    // ── Subcontractor statement ─────────────────────────────────────────────

    /// <summary>(Re)builds the draft statement for resource × month: payable
    /// hours at agreement rates, reimbursable expenses, allowances, mileage
    /// and paid travel time — nothing customer-facing.</summary>
    public async Task<SubcontractorStatement> GenerateStatementAsync(
        Guid resourceId, int year, int month, Guid? userId, CancellationToken ct)
    {
        var (from, to) = MonthRange(year, month);
        if (!await db.Resources.AnyAsync(r => r.Id == resourceId, ct))
            throw new InvalidOperationException("Unknown resource.");

        var st = await db.SubcontractorStatements.Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.ResourceId == resourceId && s.Year == year && s.Month == month, ct);
        if (st is { Status: not StatementStatus.Draft })
            throw new InvalidOperationException("The statement is final — corrections need an adjustment.");
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

        var agreements = await db.CompensationAgreements
            .Where(a => a.ResourceId == resourceId).ToListAsync(ct);
        var entryProjects = await db.TimeEntries
            .Where(e => e.ResourceId == resourceId && e.Date >= from && e.Date <= to
                && e.Timesheet != null && e.Timesheet.Status == TimesheetStatus.Approved)
            .Select(e => e.ProjectId).Distinct().ToListAsync(ct);
        var projectContracts = await db.Projects
            .Where(p => entryProjects.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.ContractId, ct);
        var agreementAt = (Guid contractId, DateOnly d) => agreements
            .Where(a => (a.ContractId == contractId || a.ContractId == null) && a.EffectiveFrom <= d)
            .OrderByDescending(a => a.ContractId == contractId)
            .ThenByDescending(a => a.EffectiveFrom).ThenByDescending(a => a.Version)
            .FirstOrDefault();

        var projectNames = await db.Projects.Where(p => entryProjects.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        // ── Hours per resource×day×project at the agreement in force.
        var entries = await db.TimeEntries
            .Where(e => e.ResourceId == resourceId && e.Date >= from && e.Date <= to
                && e.Timesheet != null && e.Timesheet.Status == TimesheetStatus.Approved
                && Counted.Contains(e.Category))
            .Select(e => new { e.Date, e.ProjectId, e.ActualHours })
            .ToListAsync(ct);

        foreach (var g in entries.GroupBy(e => e.Date).OrderBy(g => g.Key))
        {
            var splits = g.GroupBy(e => e.ProjectId)
                .Select(x => (ProjectId: x.Key, Hours: x.Sum(e => e.ActualHours))).ToList();
            var dominant = splits.OrderByDescending(s => s.Hours).First().ProjectId;
            var a = agreementAt(projectContracts[dominant], g.Key);
            var rates = new DayRateSet(
                a?.HourlyRate, a?.SaturdayRate, a?.SundayRate, a?.HolidayRate,
                a?.OvertimeAfterHours ?? 8, a?.OvertimePercent ?? 150m,
                a?.MinPayableHoursPerDay);
            var day = TimeMath.CalculateDay(g.Key, splits, rates);
            var rate = TimeMath.RateFor(day.Kind, rates) ?? 0m;

            foreach (var line in day.Lines)
            {
                if (line.EffectiveHours == 0m && line.Amount == 0m) continue;
                st.Lines.Add(new StatementLine
                {
                    Id = Guid.NewGuid(), StatementId = st.Id,
                    Kind = StatementLineKind.Hours, Date = g.Key,
                    Description = string.Create(Inv,
                        $"{projectNames.GetValueOrDefault(line.ProjectId, "?")} — {g.Key:yyyy-MM-dd}"),
                    Explanation = day.Explanation,
                    Quantity = line.EffectiveHours, UnitPrice = rate, Amount = line.Amount,
                });
            }
        }

        // ── Reimbursable expenses at cost (mark-up is the customer's side).
        var expenses = await db.Expenses
            .Where(x => x.ResourceId == resourceId && x.Date >= from && x.Date <= to
                && x.ReimbursableToResource
                && x.Timesheet != null && x.Timesheet.Status == TimesheetStatus.Approved)
            .ToListAsync(ct);
        foreach (var x in expenses.OrderBy(x => x.Date))
            st.Lines.Add(new StatementLine
            {
                Id = Guid.NewGuid(), StatementId = st.Id,
                Kind = StatementLineKind.Expense, Date = x.Date, SourceId = x.Id,
                Description = x.Description ?? $"Expense {x.Category}",
                Explanation = "Reimbursed at cost",
                Quantity = 1m, UnitPrice = x.Amount, Amount = x.Amount,
            });

        // ── Per diem claims at their claimed amounts.
        var allowances = await db.AllowanceClaims
            .Where(x => x.ResourceId == resourceId && x.Date >= from && x.Date <= to
                && x.Timesheet != null && x.Timesheet.Status == TimesheetStatus.Approved)
            .ToListAsync(ct);
        foreach (var x in allowances.OrderBy(x => x.Date))
            st.Lines.Add(new StatementLine
            {
                Id = Guid.NewGuid(), StatementId = st.Id,
                Kind = StatementLineKind.Allowance, Date = x.Date, SourceId = x.Id,
                Description = x.Notes ?? "Per diem",
                Explanation = "Per diem claim",
                Quantity = 1m, UnitPrice = x.Amount, Amount = x.Amount,
            });

        // ── Mileage + paid travel time per the resource's agreement.
        var travel = await db.TravelRecords
            .Where(x => x.ResourceId == resourceId && x.Date >= from && x.Date <= to
                && x.Timesheet != null && x.Timesheet.Status == TimesheetStatus.Approved)
            .ToListAsync(ct);
        foreach (var x in travel.OrderBy(x => x.Date))
        {
            var a = agreementAt(
                x.ProjectId.HasValue ? projectContracts.GetValueOrDefault(x.ProjectId.Value) : Guid.Empty,
                x.Date)
                ?? agreements.Where(y => y.ContractId == null && y.EffectiveFrom <= x.Date)
                    .OrderByDescending(y => y.EffectiveFrom).FirstOrDefault();
            if (a is null) continue;

            if (x.DistanceKm is > 0 && a.MileageRate is > 0)
            {
                var amount = TimeMath.Round2(x.DistanceKm.Value * a.MileageRate.Value);
                st.Lines.Add(new StatementLine
                {
                    Id = Guid.NewGuid(), StatementId = st.Id,
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
                    Id = Guid.NewGuid(), StatementId = st.Id,
                    Kind = StatementLineKind.TravelTime, Date = x.Date, SourceId = x.Id,
                    Description = string.Create(Inv, $"Travel time {x.DurationHours:0.##} h"),
                    Explanation = string.Create(Inv,
                        $"{x.DurationHours:0.##} h × {a.TravelTimePercent:0.##}% of hourly"),
                    Quantity = x.DurationHours.Value, UnitPrice = rate, Amount = amount,
                });
            }
        }

        st.Currency = agreements.FirstOrDefault()?.Currency ?? st.Currency;
        st.TotalAmount = TimeMath.Round2(st.Lines.Sum(l => l.Amount));
        await db.SaveChangesAsync(ct);
        return st;
    }

    // ── Project direct cost (margin reporting, §8) ──────────────────────────

    public record CostBreakdown(
        decimal Hours, decimal Expenses, decimal Allowances,
        decimal Mileage, decimal TravelTime, decimal Total);

    /// <summary>Direct cost of one project in a month — the payable side
    /// (agreement rates) of the approved rows hanging on the project. Read-only
    /// computation; nothing is persisted.</summary>
    public async Task<CostBreakdown> ProjectCostAsync(
        Guid projectId, int year, int month, CancellationToken ct)
    {
        var (from, to) = MonthRange(year, month);
        var contractId = await db.Projects.Where(p => p.Id == projectId)
            .Select(p => p.ContractId).FirstOrDefaultAsync(ct);
        var resourceIds = await db.TimeEntries
            .Where(e => e.ProjectId == projectId && e.Date >= from && e.Date <= to
                && e.Timesheet != null && e.Timesheet.Status == TimesheetStatus.Approved)
            .Select(e => e.ResourceId).Distinct().ToListAsync(ct);
        var agreements = await db.CompensationAgreements
            .Where(a => resourceIds.Contains(a.ResourceId)).ToListAsync(ct);
        var agreementAt = (Guid resourceId, Guid? cId, DateOnly d) => agreements
            .Where(a => a.ResourceId == resourceId
                && (a.ContractId == cId || a.ContractId == null) && a.EffectiveFrom <= d)
            .OrderByDescending(a => a.ContractId == cId)
            .ThenByDescending(a => a.EffectiveFrom).ThenByDescending(a => a.Version)
            .FirstOrDefault();

        decimal hours = 0m;
        var entries = await db.TimeEntries
            .Where(e => e.ProjectId == projectId && e.Date >= from && e.Date <= to
                && e.Timesheet != null && e.Timesheet.Status == TimesheetStatus.Approved
                && Counted.Contains(e.Category))
            .Select(e => new { e.ResourceId, e.Date, e.ActualHours }).ToListAsync(ct);
        var dayContracts = await db.Projects
            .ToDictionaryAsync(p => p.Id, p => p.ContractId, ct);
        foreach (var g in entries.GroupBy(e => (e.ResourceId, e.Date)))
        {
            // Other projects share the day's OT/minimum pool — replay the whole
            // approved day and take this project's line (same as the invoice).
            var allDay = await db.TimeEntries
                .Where(e => e.ResourceId == g.Key.ResourceId && e.Date == g.Key.Date
                    && e.Timesheet != null && e.Timesheet.Status == TimesheetStatus.Approved
                    && Counted.Contains(e.Category))
                .Select(e => new { e.ProjectId, e.ActualHours }).ToListAsync(ct);
            var splits = allDay.GroupBy(x => x.ProjectId)
                .Select(x => (ProjectId: x.Key, Hours: x.Sum(e => e.ActualHours))).ToList();
            var dominant = splits.OrderByDescending(s => s.Hours).First().ProjectId;
            var a = agreementAt(
                g.Key.ResourceId, dayContracts.GetValueOrDefault(dominant), g.Key.Date);
            var rates = new DayRateSet(
                a?.HourlyRate, a?.SaturdayRate, a?.SundayRate, a?.HolidayRate,
                a?.OvertimeAfterHours ?? 8, a?.OvertimePercent ?? 150m,
                a?.MinPayableHoursPerDay);
            var day = TimeMath.CalculateDay(g.Key.Date, splits, rates);
            hours += day.Lines.FirstOrDefault(l => l.ProjectId == projectId)?.Amount ?? 0m;
        }

        var expenses = await db.Expenses
            .Where(x => x.ProjectId == projectId && x.Date >= from && x.Date <= to
                && x.ReimbursableToResource
                && x.Timesheet != null && x.Timesheet.Status == TimesheetStatus.Approved)
            .SumAsync(x => x.Amount, ct);
        var allowances = await db.AllowanceClaims
            .Where(x => x.ProjectId == projectId && x.Date >= from && x.Date <= to
                && x.Timesheet != null && x.Timesheet.Status == TimesheetStatus.Approved)
            .SumAsync(x => x.Amount, ct);

        decimal mileage = 0m, travelTime = 0m;
        var travel = await db.TravelRecords
            .Where(x => x.ProjectId == projectId && x.Date >= from && x.Date <= to
                && x.Timesheet != null && x.Timesheet.Status == TimesheetStatus.Approved)
            .ToListAsync(ct);
        foreach (var x in travel)
        {
            var a = agreementAt(x.ResourceId, contractId, x.Date);
            if (a is null) continue;
            if (x.DistanceKm is > 0 && a.MileageRate is > 0)
                mileage += TimeMath.Round2(x.DistanceKm.Value * a.MileageRate.Value);
            if (x.DurationHours is > 0 && a.TravelTimePercent is > 0 && a.HourlyRate is > 0)
                travelTime += TimeMath.Round2(
                    x.DurationHours.Value * a.HourlyRate.Value * a.TravelTimePercent.Value / 100m);
        }

        return new CostBreakdown(
            TimeMath.Round2(hours), expenses, allowances,
            mileage, travelTime,
            TimeMath.Round2(hours + expenses + allowances + mileage + travelTime));
    }
}
