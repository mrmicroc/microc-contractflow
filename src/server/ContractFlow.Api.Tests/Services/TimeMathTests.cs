using ContractFlow.Api.Services;

namespace ContractFlow.Api.Tests.Services;

/// <summary>
/// Calculation-engine tests built on the worked examples in
/// docs/FUNCTIONAL_DESIGN.md §5 — the commercial rules, verbatim.
/// </summary>
public class TimeMathTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static DayRateSet Rates(
        decimal weekday = 10m, decimal? sat = 12m, decimal? sun = 14m,
        int otAfter = 8, decimal otPct = 150m, decimal? min = null) =>
        new(weekday, sat, sun, null, otAfter, otPct, min);

    [Fact]
    public void MinHours_SplitAcrossTwoProjects_AllocatesProportionally()
    {
        // Spec: 6 h across projects A+B with 8 h minimum → A 2.67 / B 5.33.
        var result = TimeMath.CalculateDay(
            new DateOnly(2026, 10, 12), // Monday
            [(A, 2m), (B, 4m)],
            Rates(min: 8m));

        Assert.Equal(6m, result.ActualTotal);
        Assert.Equal(8m, result.EffectiveTotal);
        Assert.Equal(2m, result.MinimumAdjustment);
        Assert.Equal(2.67m, result.Lines[0].EffectiveHours);
        Assert.Equal(5.33m, result.Lines[1].EffectiveHours);
        Assert.Equal(8m, result.Lines.Sum(l => l.EffectiveHours));
        Assert.Contains("minimum-hours adjustment +2", result.Explanation);
    }

    [Fact]
    public void Saturday_Overtime_At175Percent()
    {
        // Spec: Saturday 10 h, ordinary 8 → 8 h × Sat rate + 2 h × Sat × 1.75.
        // Sat rate 12, OT 175 % → 96 + 42 = 138.
        var result = TimeMath.CalculateDay(
            new DateOnly(2026, 10, 10), // Saturday
            [(A, 10m)],
            Rates(sat: 12m, otAfter: 8, otPct: 175m));

        Assert.Equal(DayKind.Saturday, result.Kind);
        var line = Assert.Single(result.Lines);
        Assert.Equal(8m, line.OrdinaryHours);
        Assert.Equal(2m, line.OvertimeHours);
        Assert.Equal(138m, line.Amount);
        Assert.Equal(138m, result.Amount);
        Assert.Contains("Saturday", result.Explanation);
        Assert.Contains("175%", result.Explanation);
    }

    [Fact]
    public void MinHours_ThreeActual_BecomesEightPayable()
    {
        // Spec: 3 actual → +5.0 adjustment → 8 payable, separate lines.
        var result = TimeMath.CalculateDay(
            new DateOnly(2026, 10, 13),
            [(A, 3m)],
            Rates(weekday: 10m, min: 8m));

        var line = Assert.Single(result.Lines);
        Assert.Equal(3m, line.ActualHours);
        Assert.Equal(5m, line.MinimumAdjustment);
        Assert.Equal(8m, line.EffectiveHours);
        Assert.Equal(80m, line.Amount);
    }

    [Fact]
    public void MinHours_AdjustmentNeverTriggersOvertime()
    {
        // 6 h actual, min 8, OT after 8 — effective reaches 8 exactly, no OT.
        var result = TimeMath.CalculateDay(
            new DateOnly(2026, 10, 13), [(A, 6m)], Rates(otAfter: 8, min: 8m));

        Assert.Equal(0m, result.OvertimeTotal);
        Assert.Equal(8m, result.EffectiveTotal);
    }

    [Fact]
    public void Holiday_BeatsWeekday_AndUsesHolidayRate()
    {
        // 2026-01-01 is a Thursday and a Swedish public holiday.
        var result = TimeMath.CalculateDay(
            new DateOnly(2026, 1, 1), [(A, 8m)],
            new DayRateSet(10m, 12m, 14m, 20m, 8, 150m, null));

        Assert.Equal(DayKind.Holiday, result.Kind);
        Assert.Equal(160m, result.Amount); // 8 × 20
    }

    [Fact]
    public void MissingDayRate_FallsBackTowardWeekday()
    {
        // Saturday without a configured Saturday rate bills at the weekday rate.
        var result = TimeMath.CalculateDay(
            new DateOnly(2026, 10, 10), [(A, 8m)], Rates(sat: null));
        Assert.Equal(80m, result.Amount);
    }

    [Fact]
    public void BreakFreeSplit_RoundsSoLinesSumToTotal()
    {
        // 6 h split 2/4 with min 8 → 2.67 + 5.33 = exactly 8.00.
        var result = TimeMath.CalculateDay(
            new DateOnly(2026, 10, 12), [(A, 2m), (B, 4m)], Rates(min: 8m));
        Assert.Equal(result.EffectiveTotal, result.Lines.Sum(l => l.EffectiveHours));
    }

    [Fact]
    public void DailyRate_FullDay_SplitsProportionallyAcrossProjects()
    {
        // Daily component: one flat day price regardless of hours, split by
        // each project's share of the day (DayFraction is the bill quantity).
        var result = TimeMath.CalculateDay(
            new DateOnly(2026, 10, 12), // Monday
            [(A, 2m), (B, 6m)],
            new DayRateSet(10m, null, null, null, 8, 150m, null,
                DayRate: 8000m));

        Assert.Equal(8000m, result.Amount);
        Assert.Equal(0.25m, result.Lines[0].DayFraction);
        Assert.Equal(2000m, result.Lines[0].Amount);
        Assert.Equal(0.75m, result.Lines[1].DayFraction);
        Assert.Equal(6000m, result.Lines[1].Amount);
        Assert.Contains("full day", result.Explanation);
    }

    [Fact]
    public void DailyRate_ShortDay_BillsHalfDayRate()
    {
        var rates = new DayRateSet(null, null, null, null, 8, 150m, null,
            DayRate: 8000m, HalfDayRate: 5000m, HalfDayMaxHours: 4m);

        var half = TimeMath.CalculateDay(new DateOnly(2026, 10, 12), [(A, 4m)], rates);
        Assert.Equal(5000m, half.Amount);
        Assert.Contains("half day", half.Explanation);

        var full = TimeMath.CalculateDay(new DateOnly(2026, 10, 12), [(A, 5m)], rates);
        Assert.Equal(8000m, full.Amount);
    }

    [Fact]
    public void DailyRate_NeitherOvertimeNorMinimumStacks()
    {
        // A fixed day price never grows: 10 h with OT-after-8 and min-hours
        // configured still bills exactly the day rate.
        var result = TimeMath.CalculateDay(
            new DateOnly(2026, 10, 12), [(A, 10m)],
            new DayRateSet(10m, null, null, null, 8, 150m, 8m, DayRate: 8000m));

        Assert.Equal(0m, result.MinimumAdjustment);
        Assert.Equal(8000m, result.Amount);
        // (OT hours are still tracked for information — they just don't price.)
        Assert.Equal(2m, result.OvertimeTotal);
    }

    [Fact]
    public void Recharge_AddsContractMarkup()
    {
        // Spec: expense 1 200 + 10 % mark-up → 1 320.
        Assert.Equal(1320m, TimeMath.Recharge(1200m, 10m));
    }

    [Fact]
    public void SuggestPeriods_ConsecutiveDays_OnePeriod()
    {
        var days = Enumerable.Range(0, 5).Select(d => new DateOnly(2026, 10, 12).AddDays(d));
        var periods = TimeMath.SuggestPeriods(days); // Mon–Fri
        var p = Assert.Single(periods);
        Assert.Equal(new DateOnly(2026, 10, 12), p.Start);
        Assert.Equal(new DateOnly(2026, 10, 16), p.End);
    }

    [Fact]
    public void SuggestPeriods_WeekendGap_TwoPeriods()
    {
        // Two Mon–Fri weeks → two mobilisations (weekly commute home).
        var days = Enumerable.Range(0, 12)
            .Select(d => new DateOnly(2026, 10, 12).AddDays(d))
            .Where(d => d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday);
        var periods = TimeMath.SuggestPeriods(days);
        Assert.Equal(2, periods.Count);
        Assert.Equal(new DateOnly(2026, 10, 16), periods[0].End);
        Assert.Equal(new DateOnly(2026, 10, 19), periods[1].Start);
    }
}
