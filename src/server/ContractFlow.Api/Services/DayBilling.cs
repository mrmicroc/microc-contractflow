using System.Globalization;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Services;

/// <summary>One approved, counted time row of a resource's day as the two
/// calculators see it. <paramref name="WorkPackageId"/> is the effective
/// package — the booking's for booked rows, the row's own otherwise.
/// <paramref name="Outside"/>: that package lies outside the contract's scope
/// (<see cref="WorkPackagePricing.OutsideContract"/>) — billed by time even
/// under PAD, in its own pool.</summary>
public record DayRow(
    Guid EntryId, Guid ProjectId, Guid ContractId, Guid? WorkPackageId,
    EntryBilling Billing, bool Booked, decimal Hours, bool Outside = false);

/// <summary>An entry's frozen share of its part: hours incl. its slice of the
/// minimum adjustment, and that slice alone.</summary>
public record EntryShare(Guid EntryId, decimal Hours, decimal MinimumAdjustment);

public enum BillPartKind
{
    /// <summary>Priced per time on the invoice.</summary>
    Priced,
    /// <summary>Booking-bound row under a PAD component — billed via the PAD line.</summary>
    PadCovered,
    /// <summary>The terms version bills no time (TimeBilling.None).</summary>
    NotBilled,
}

/// <summary>The customer-billing result for one (project, package, mode)
/// partition of a day.</summary>
public record BillPart(
    Guid ProjectId, Guid ContractId, Guid? WorkPackageId, bool HoursMode, bool Daily,
    BillPartKind Kind, ContractTerms? Terms, DayLine? Line, DayKind DayKind,
    decimal UnitPrice, string Explanation,
    WorkPackageRate? PackageRate, WorkPackageRate? ResourceRate,
    IReadOnlyList<EntryShare> Entries, bool Outside = false);

/// <summary>The compensation result for one (project, package) part of a day.</summary>
public record PayPart(
    Guid ProjectId, Guid? WorkPackageId, CompensationAgreement? Agreement,
    DayLine Line, decimal UnitPrice, string Explanation,
    IReadOnlyList<EntryShare> Entries);

/// <summary>
/// The single source of truth for pricing one resource-day (design §5) —
/// used by timesheet approval (to freeze payable/billable hours), the invoice
/// specification, the subcontractor statement and the cost report, so the
/// frozen quantities always equal what is invoiced and paid.
///
/// Customer side: per contract, a day's rows share ONE minimum and ONE
/// overtime threshold (multiple activities same day share one minimum, §5)
/// within each pricing pool — day-rate rows and plain-hour rows are separate
/// pools. Hours are split proportionally across (project, package, mode)
/// partitions and each partition prices at its own resolved rates.
/// Another contract's hours never touch this contract's minimum or OT (§1.1).
///
/// Pay side: the whole day shares one minimum/OT threshold from the dominant
/// agreement; each (project, package) part prices at its own agreement.
/// </summary>
public static class DayBilling
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Rate set merged per field: resource matrix cell → package row →
    /// contract terms. <paramref name="daily"/> keeps day-rate fields; off →
    /// pure hourly pricing.</summary>
    public static DayRateSet MergeRates(ContractTerms? t, WorkPackageRate? gen,
        WorkPackageRate? res, bool daily) => new(
        res?.HourlyRate ?? gen?.HourlyRate ?? t?.HourlyRate,
        res?.SaturdayRate ?? gen?.SaturdayRate ?? t?.SaturdayRate,
        res?.SundayRate ?? gen?.SundayRate ?? t?.SundayRate,
        res?.HolidayRate ?? gen?.HolidayRate ?? t?.HolidayRate,
        t?.OvertimeAfterHours ?? 8, t?.OvertimePercent ?? 150m,
        t?.MinBillableHoursPerDay,
        DayRate: daily ? res?.DayRate ?? gen?.DayRate ?? t?.DayRate : null,
        SaturdayDayRate: daily ? res?.SaturdayDayRate ?? gen?.SaturdayDayRate ?? t?.SaturdayDayRate : null,
        SundayDayRate: daily ? res?.SundayDayRate ?? gen?.SundayDayRate ?? t?.SundayDayRate : null,
        HolidayDayRate: daily ? res?.HolidayDayRate ?? gen?.HolidayDayRate ?? t?.HolidayDayRate : null,
        HalfDayRate: daily ? res?.HalfDayRate ?? gen?.HalfDayRate ?? t?.HalfDayRate : null,
        HalfDayMaxHours: daily ? res?.HalfDayMaxHours ?? gen?.HalfDayMaxHours ?? t?.HalfDayMaxHours : null);

    public static DayRateSet AgreementRates(CompensationAgreement? a, CompensationAgreement? rules) => new(
        a?.HourlyRate, a?.SaturdayRate, a?.SundayRate, a?.HolidayRate,
        rules?.OvertimeAfterHours ?? 8, rules?.OvertimePercent ?? 150m,
        rules?.MinPayableHoursPerDay);

    /// <summary>Agreement resolution: exact work-package scope, then contract
    /// scope, then the default; latest effective version within a scope.</summary>
    public static CompensationAgreement? AgreementAt(
        IEnumerable<CompensationAgreement> agreements, Guid contractId, Guid? workPackageId, DateOnly date) =>
        agreements
            .Where(x => x.EffectiveFrom <= date && (
                (workPackageId != null && x.WorkPackageId == workPackageId) ||
                (x.WorkPackageId == null && (x.ContractId == contractId || x.ContractId == null))))
            .OrderByDescending(x => x.WorkPackageId != null)
            .ThenByDescending(x => x.ContractId != null)
            .ThenByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Version)
            .FirstOrDefault();

    public static ContractTerms? TermsAt(IEnumerable<ContractTerms> terms, Guid contractId, DateOnly date) =>
        terms.Where(t => t.ContractId == contractId && t.EffectiveFrom <= date)
            .OrderByDescending(t => t.EffectiveFrom).ThenByDescending(t => t.Version)
            .FirstOrDefault();

    // ── Customer billing ────────────────────────────────────────────────────

    /// <param name="termsOf">Contract id → terms version in force on the day.</param>
    /// <param name="overridesOf">Work package → (package row, resource cell) in force.</param>
    public static List<BillPart> BillDay(
        DateOnly date, IReadOnlyList<DayRow> rows,
        Func<Guid, ContractTerms?> termsOf,
        Func<Guid?, (WorkPackageRate? Gen, WorkPackageRate? Res)> overridesOf)
    {
        var parts = new List<BillPart>();
        foreach (var byContract in rows.GroupBy(r => r.ContractId).OrderBy(g => g.Key))
        {
            var t = termsOf(byContract.Key);
            var mode = t?.TimeBilling ?? TimeBilling.Hourly;
            var padOn = t?.PadEnabled == true;
            var calendar = t?.HolidayCalendar ?? "SE";
            var kind = TimeMath.KindOf(date, calendar);

            // Work at a package outside the contract's scope is never PAD-covered.
            foreach (var g in byContract.Where(r => padOn && r.Booked && !r.Outside)
                         .GroupBy(r => (r.ProjectId, r.WorkPackageId)).OrderBy(x => x.Key.ProjectId))
                parts.Add(Unpriced(g.Key.ProjectId, byContract.Key, g.Key.WorkPackageId,
                    BillPartKind.PadCovered, t, kind, "Covered by the PAD line", g));

            var reportable = byContract.Where(r => !(padOn && r.Booked && !r.Outside)).ToList();
            if (reportable.Count == 0) continue;
            if (mode == TimeBilling.None)
            {
                foreach (var g in reportable.Where(r => !r.Outside)
                             .GroupBy(r => (r.ProjectId, r.WorkPackageId))
                             .OrderBy(x => x.Key.ProjectId))
                    parts.Add(Unpriced(g.Key.ProjectId, byContract.Key, g.Key.WorkPackageId,
                        BillPartKind.NotBilled, t, kind, "Terms bill no time", g, zero: true));
                reportable = reportable.Where(r => r.Outside).ToList();
                if (reportable.Count == 0) continue;
            }

            // Pools: contract-mode rows on a day-rate contract share the day
            // price; everything else prices as hours. Within a pool the minimum
            // and OT threshold are shared by all partitions. Out-of-scope work
            // is additional — its own pool, by time (hours when the terms bill
            // no time at all).
            var pools = reportable.GroupBy(r => (
                Outside: r.Outside,
                Daily: mode == TimeBilling.DailyRate && r.Billing == EntryBilling.Contract));
            foreach (var pool in pools.OrderBy(p => p.Key.Outside).ThenBy(p => p.Key.Daily))
            {
                var daily = pool.Key.Daily;
                var outside = pool.Key.Outside;
                var partitions = pool
                    .GroupBy(r => (r.ProjectId, r.WorkPackageId, HoursMode: r.Billing == EntryBilling.Hours))
                    .OrderBy(x => x.Key.ProjectId).ThenBy(x => x.Key.WorkPackageId)
                    .ThenBy(x => x.Key.HoursMode)
                    .ToList();
                var synth = partitions.Select(_ => Guid.NewGuid()).ToList();
                var splits = partitions
                    .Select((p, i) => (ProjectId: synth[i], Hours: p.Sum(r => r.Hours))).ToList();
                var poolHours = splits.Sum(s => s.Hours);

                for (var i = 0; i < partitions.Count; i++)
                {
                    var p = partitions[i];
                    var (gen, res) = overridesOf(p.Key.WorkPackageId);
                    var rates = MergeRates(t, gen, res, daily);
                    var day = TimeMath.CalculateDay(date, splits, rates, calendar);
                    var line = day.Lines[i] with { ProjectId = p.Key.ProjectId };
                    var unit = daily
                        ? (line.DayFraction > 0m ? TimeMath.Round2(line.Amount / line.DayFraction) : 0m)
                        : TimeMath.RateFor(day.Kind, rates) ?? 0m;
                    var explanation = partitions.Count == 1
                        ? day.Explanation
                        : PartExplanation(day, line, rates, daily, poolHours);
                    parts.Add(new BillPart(
                        p.Key.ProjectId, byContract.Key, p.Key.WorkPackageId, p.Key.HoursMode, daily,
                        BillPartKind.Priced, t, line, day.Kind, unit,
                        explanation + (outside ? " · additional work outside the contract scope" : ""),
                        gen, res, Distribute(p.ToList(), line.EffectiveHours), outside));
                }
            }
        }
        return parts;
    }

    private static BillPart Unpriced(
        Guid projectId, Guid contractId, Guid? wp, BillPartKind kind, ContractTerms? t,
        DayKind dayKind, string explanation, IEnumerable<DayRow> rows, bool zero = false)
    {
        var list = rows.OrderBy(r => r.EntryId).ToList();
        return new BillPart(projectId, contractId, wp, false, false, kind, t, null, dayKind, 0m,
            explanation, null, null,
            list.Select(r => new EntryShare(r.EntryId, zero ? 0m : r.Hours, 0m)).ToList());
    }

    private static string PartExplanation(
        DayResult day, DayLine line, DayRateSet rates, bool daily, decimal poolHours)
    {
        var share = string.Create(Inv, $" (share {line.ActualHours:0.##} of {poolHours:0.##} h)");
        var ot = line.OvertimeHours > 0
            ? string.Create(Inv, $" + {line.OvertimeHours:0.##} h OT × {rates.OvertimePercent:0.##}%")
            : "";
        if (daily)
        {
            var price = TimeMath.DayRateFor(day.Kind, rates) ?? 0m;
            return string.Create(Inv,
                $"{day.Kind}: {line.DayFraction:0.###} day @ {price:0.##}{ot}{share} → {line.Amount:0.##}");
        }
        var rate = TimeMath.RateFor(day.Kind, rates);
        var min = line.MinimumAdjustment > 0
            ? string.Create(Inv, $"; minimum-hours adjustment +{line.MinimumAdjustment:0.##} h")
            : "";
        return rate.HasValue
            ? string.Create(Inv,
                $"{day.Kind}: {line.OrdinaryHours:0.##} h @ {rate.Value:0.##}{ot}{min}{share} → {line.Amount:0.##}")
            : string.Create(Inv, $"{day.Kind}: {line.OrdinaryHours:0.##} h (no rate configured){min}{share}");
    }

    // ── Compensation ────────────────────────────────────────────────────────

    /// <param name="agreementOf">(contract, package) → agreement in force.</param>
    /// <param name="calendarOf">Contract id → holiday calendar code.</param>
    public static List<PayPart> PayDay(
        DateOnly date, IReadOnlyList<DayRow> rows,
        Func<Guid, Guid?, CompensationAgreement?> agreementOf,
        Func<Guid, string?> calendarOf)
    {
        if (rows.Count == 0) return [];
        var parts = rows
            .GroupBy(r => (r.ProjectId, r.WorkPackageId))
            .OrderBy(x => x.Key.ProjectId).ThenBy(x => x.Key.WorkPackageId)
            .ToList();
        // Dominant part: most hours; ties break on the ordering above.
        var dominantIdx = 0;
        for (var i = 1; i < parts.Count; i++)
            if (parts[i].Sum(r => r.Hours) > parts[dominantIdx].Sum(r => r.Hours)) dominantIdx = i;
        var dominant = parts[dominantIdx];
        var dominantContract = dominant.First().ContractId;
        var rules = agreementOf(dominantContract, dominant.Key.WorkPackageId);
        var calendar = calendarOf(dominantContract) ?? "SE";

        var synth = parts.Select(_ => Guid.NewGuid()).ToList();
        var splits = parts.Select((p, i) => (ProjectId: synth[i], Hours: p.Sum(r => r.Hours))).ToList();
        var total = splits.Sum(s => s.Hours);

        var result = new List<PayPart>();
        for (var i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            var a = agreementOf(p.First().ContractId, p.Key.WorkPackageId);
            var rates = AgreementRates(a, rules);
            var day = TimeMath.CalculateDay(date, splits, rates, calendar);
            var line = day.Lines[i] with { ProjectId = p.Key.ProjectId };
            var explanation = parts.Count == 1
                ? day.Explanation
                : PartExplanation(day, line, rates, false, total);
            result.Add(new PayPart(p.Key.ProjectId, p.Key.WorkPackageId, a, line,
                TimeMath.RateFor(day.Kind, rates) ?? 0m, explanation,
                Distribute(p.ToList(), line.EffectiveHours)));
        }
        return result;
    }

    /// <summary>Splits a part's effective hours across its entries in
    /// proportion to their actual hours — the last entry absorbs the rounding
    /// remainder so the shares always sum exactly to the part.</summary>
    public static List<EntryShare> Distribute(IReadOnlyList<DayRow> rows, decimal effective)
    {
        var ordered = rows.OrderBy(r => r.EntryId).ToList();
        var actual = ordered.Sum(r => r.Hours);
        var shares = new List<EntryShare>();
        decimal acc = 0m;
        for (var i = 0; i < ordered.Count; i++)
        {
            var r = ordered[i];
            var hours = i == ordered.Count - 1
                ? effective - acc
                : actual > 0 ? TimeMath.Round2(effective * r.Hours / actual) : r.Hours;
            acc += hours;
            shares.Add(new EntryShare(r.EntryId, hours, Math.Max(0m, hours - r.Hours)));
        }
        return shares;
    }
}
