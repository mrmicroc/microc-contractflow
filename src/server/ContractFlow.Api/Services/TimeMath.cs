using System.Globalization;

namespace ContractFlow.Api.Services;

public enum DayKind
{
    Weekday,
    Saturday,
    Sunday,
    Holiday,
}

/// <summary>
/// The rate set a calculation runs against — resolved from the contract terms
/// version (customer billing) or the compensation agreement version
/// (subcontractor pay) in force on the entry date (design §5: the same pipeline
/// runs twice with different rule sets).
/// </summary>
public record DayRateSet(
    decimal? WeekdayRate,
    decimal? SaturdayRate,
    decimal? SundayRate,
    decimal? HolidayRate,
    int OvertimeAfterHours,
    decimal OvertimePercent,
    /// <summary>Minimum hours per attendance day; null = no minimum. The
    /// minimum is added as an adjustment line — it never overwrites actual
    /// hours and never itself triggers overtime.</summary>
    decimal? MinHoursPerDay,
    /// <summary>Flat price per attendance day — the daily-rate component.
    /// Set ⇒ the day bills a fixed price split across projects, with no
    /// overtime or minimum-hours adjustment on top.</summary>
    decimal? DayRate = null,
    /// <summary>Price when the day totals ≤ <paramref name="HalfDayMaxHours"/>
    /// — null = every attended day bills the full day rate.</summary>
    decimal? HalfDayRate = null,
    decimal? HalfDayMaxHours = null);

public record DayLine(
    Guid ProjectId,
    decimal ActualHours,
    /// <summary>Actual + this line's share of the minimum adjustment.</summary>
    decimal EffectiveHours,
    decimal OrdinaryHours,
    decimal OvertimeHours,
    decimal MinimumAdjustment,
    decimal Amount,
    /// <summary>This line's share of the day (0–1) — the billing quantity for
    /// daily-rate contracts.</summary>
    decimal DayFraction = 0m);

public record DayResult(
    DayKind Kind,
    decimal ActualTotal,
    decimal EffectiveTotal,
    decimal MinimumAdjustment,
    decimal OrdinaryTotal,
    decimal OvertimeTotal,
    decimal Amount,
    IReadOnlyList<DayLine> Lines,
    string Explanation);

/// <summary>
/// Pure execution-day arithmetic (design §5): day type via the holiday
/// calendar → ordinary/overtime split on actuals → minimum-hours adjustment →
/// proportional allocation across projects → amounts at the applicable rates.
/// Deterministic and free of I/O — every step covered by the spec's worked
/// examples.
/// </summary>
public static class TimeMath
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Day type for the SE calendar (Saturday/Sunday beat Weekday; a
    /// listed holiday beats everything).</summary>
    public static DayKind KindOf(DateOnly date, string? holidayCalendar = "SE")
    {
        if (BusinessDays.SwedishHolidays(date.Year).Contains(date))
            return DayKind.Holiday;
        return date.DayOfWeek switch
        {
            DayOfWeek.Saturday => DayKind.Saturday,
            DayOfWeek.Sunday => DayKind.Sunday,
            _ => DayKind.Weekday,
        };
    }

    /// <summary>Rate for a day kind with fallback toward the weekday rate.</summary>
    public static decimal? RateFor(DayKind kind, DayRateSet r) => kind switch
    {
        DayKind.Holiday => r.HolidayRate ?? r.SundayRate ?? r.SaturdayRate ?? r.WeekdayRate,
        DayKind.Sunday => r.SundayRate ?? r.SaturdayRate ?? r.WeekdayRate,
        DayKind.Saturday => r.SaturdayRate ?? r.WeekdayRate,
        _ => r.WeekdayRate,
    };

    /// <summary>
    /// Calculates one attendance day. <paramref name="splits"/> carries the
    /// payable-relevant hours per project for the day (break rows excluded by
    /// the caller). Rounded per day to 2 decimals, AwayFromZero (per-contract
    /// rounding is an open question, §11).
    /// </summary>
    public static DayResult CalculateDay(
        DateOnly date,
        IReadOnlyList<(Guid ProjectId, decimal Hours)> splits,
        DayRateSet rates,
        string? holidayCalendar = "SE")
    {
        var kind = KindOf(date, holidayCalendar);
        var actual = splits.Sum(s => s.Hours);
        var rate = RateFor(kind, rates);
        var otAfter = Math.Max(0, rates.OvertimeAfterHours);
        var otPct = rates.OvertimePercent;

        if (actual <= 0)
            return new DayResult(kind, 0m, 0m, 0m, 0m, 0m, 0m, [], $"{kind}: no hours");

        // Step 3 — OT split on ACTUAL hours (the minimum never triggers OT).
        var ordinaryTotal = Math.Min(actual, otAfter);
        var otTotal = actual - ordinaryTotal;

        // Daily-rate component: one flat price per attended day, split across
        // projects. A half-day rate applies when configured and the day stays
        // short; no OT or minimum adjustment stacks on a fixed day price.
        var daily = rates.DayRate is > 0m;
        var halfDay = daily && rates.HalfDayRate is > 0m
            && rates.HalfDayMaxHours is > 0m && actual <= rates.HalfDayMaxHours.Value;
        var dayPrice = daily ? (halfDay ? rates.HalfDayRate!.Value : rates.DayRate!.Value) : 0m;

        // Step 4 — minimum adjustment on top of actuals.
        var minAdj = !daily && rates.MinHoursPerDay.HasValue && actual < rates.MinHoursPerDay.Value
            ? rates.MinHoursPerDay.Value - actual
            : 0m;
        var effective = actual + minAdj;

        // Step 5 — proportional allocation across projects, rounded per line;
        // the last line absorbs the rounding remainder so Σ lines == totals.
        var lines = new List<DayLine>();
        decimal effAcc = 0m, otAcc = 0m, amtAcc = 0m;
        for (var i = 0; i < splits.Count; i++)
        {
            var (projectId, hours) = splits[i];
            var last = i == splits.Count - 1;
            var share = hours / actual;

            var eff = last ? effective - effAcc : Round2(effective * share);
            var ot = last ? otTotal - otAcc : Round2(otTotal * share);
            effAcc += eff; otAcc += ot;

            var ordinary = eff - ot;
            var amount = daily
                ? (last ? dayPrice - amtAcc : Round2(dayPrice * share))
                : rate.HasValue
                    ? Round2(ordinary * rate.Value + ot * rate.Value * otPct / 100m)
                    : 0m;
            amtAcc += amount;
            lines.Add(new DayLine(projectId, hours, eff, ordinary, ot, eff - hours, amount, share));
        }

        // Steps 6+7 — rounded amounts + human-readable explanation (§5).
        var amountTotal = lines.Sum(l => l.Amount);
        var explanation = daily
            ? string.Create(Inv, $"{kind}: {(halfDay ? "half" : "full")} day @ {dayPrice:0.##}") +
              string.Create(Inv, $" → {amountTotal:0.##}")
            : rate.HasValue
            ? string.Create(Inv, $"{kind}: {ordinaryTotal:0.##} h @ {rate.Value:0.##}") +
              (otTotal > 0 ? string.Create(Inv, $" + {otTotal:0.##} h OT × {otPct:0.##}%") : "") +
              (minAdj > 0 ? string.Create(Inv, $"; minimum-hours adjustment +{minAdj:0.##} h") : "") +
              string.Create(Inv, $" → {amountTotal:0.##}")
            : $"{kind}: {ordinaryTotal:0.##} h (no rate configured)" +
              (minAdj > 0 ? string.Create(Inv, $"; minimum-hours adjustment +{minAdj:0.##} h") : "");

        return new DayResult(kind, actual, effective, minAdj, ordinaryTotal, otTotal,
            amountTotal, lines, explanation);
    }

    /// <summary>Customer recharge of an expense with contract mark-up
    /// (spec example: 1 200 + 10 % → 1 320).</summary>
    public static decimal Recharge(decimal amount, decimal markupPercent) =>
        Round2(amount * (1m + markupPercent / 100m));

    /// <summary>
    /// Groups attendance days into mobilisation periods: runs of adjacent
    /// calendar days. A weekend gap (e.g. home between Mon–Fri weeks) starts a
    /// new period — matching the one-fee-per-period contract rule (§6.2).
    /// </summary>
    public static List<(DateOnly Start, DateOnly End)> SuggestPeriods(IEnumerable<DateOnly> days)
    {
        var sorted = days.Distinct().Order().ToList();
        var periods = new List<(DateOnly, DateOnly)>();
        foreach (var d in sorted)
        {
            if (periods.Count > 0 && d == periods[^1].Item2.AddDays(1))
                periods[^1] = (periods[^1].Item1, d);
            else
                periods.Add((d, d));
        }
        return periods;
    }

    public static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
