using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;

namespace ContractFlow.Api.Tests.Services;

/// <summary>
/// Cancellation-ladder resolution — the stepped replacement for the binary
/// "4 business days" deadline (design §4.3 refined).
/// </summary>
public class CancellationMathTests
{
    // The user's worked example: ≥4 days → 4 h, ≥2 → 6 h, ≥1 → 8 h.
    private static readonly List<CancellationStep> Ladder =
    [
        new() { MinBusinessDaysBefore = 4, ChargeHours = 4m },
        new() { MinBusinessDaysBefore = 2, ChargeHours = 6m },
        new() { MinBusinessDaysBefore = 1, ChargeHours = 8m },
    ];

    [Theory]
    [InlineData(5, 4)]   // plenty of notice → loosest rung
    [InlineData(4, 4)]
    [InlineData(3, 6)]   // inside the 2-day rung
    [InlineData(2, 6)]
    [InlineData(1, 8)]   // tightest rung
    public void Steps_PickTheTightestMatchingRung(int daysBefore, decimal expectedHours)
    {
        var charge = CancellationMath.Resolve(Ladder, daysBefore, plannedHours: 8m, fallbackDeadlineDays: 4);
        Assert.Equal(expectedHours, charge.Hours);
        Assert.Null(charge.Amount);
    }

    [Fact]
    public void BelowLowestRung_ChargesTheFullBooking()
    {
        var charge = CancellationMath.Resolve(Ladder, 0, plannedHours: 8m, fallbackDeadlineDays: 4);
        Assert.Equal(8m, charge.Hours); // day-of cancel = whole booked day
    }

    [Fact]
    public void Step_WithNoCharge_IsFree()
    {
        var steps = new List<CancellationStep>
        {
            new() { MinBusinessDaysBefore = 4 },                    // free window
            new() { MinBusinessDaysBefore = 1, ChargeHours = 8m },
        };
        Assert.True(CancellationMath.Resolve(steps, 5, 8m, 4).IsFree);
        Assert.Equal(8m, CancellationMath.Resolve(steps, 2, 8m, 4).Hours);
    }

    [Fact]
    public void FixedAmount_Rung_CarriesTheFee()
    {
        var steps = new List<CancellationStep>
        {
            new() { MinBusinessDaysBefore = 2, ChargeAmount = 3000m },
        };
        var charge = CancellationMath.Resolve(steps, 3, 8m, 4);
        Assert.Null(charge.Hours);
        Assert.Equal(3000m, charge.Amount);
    }

    [Fact]
    public void FullCharge_IsFlagged_RungChargeIsNot()
    {
        // Full = the whole booking is charged (late) → PAD contracts consume
        // the whole commitment, never less than the 1.00 PAD minimum.
        Assert.True(CancellationMath.Resolve(Ladder, 0, 4m, 4).Full);
        Assert.True(CancellationMath.Resolve(null, 1, 4m, 4).Full);
        Assert.False(CancellationMath.Resolve(Ladder, 1, 4m, 4).Full); // 8 h rung, explicit
        Assert.False(CancellationMath.Resolve(null, 6, 4m, 4).Full);
    }

    [Fact]
    public void EmptyLadder_FallsBackToBinaryDeadline()
    {
        // Legacy behaviour: inside deadline → timely/free, else full charge.
        Assert.True(CancellationMath.Resolve(null, 6, 8m, 4).IsFree);
        Assert.True(CancellationMath.Resolve([], 6, 8m, 4).IsFree);
        Assert.Equal(8m, CancellationMath.Resolve(null, 2, 8m, 4).Hours);
    }

    /// <summary>A stepped policy: ≥ 10 business days free, 5–9 days 50 %,
    /// 2–4 days 100 % (explicit), under 2 days below the ladder → full.</summary>
    private static readonly List<CancellationStep> PercentLadder =
    [
        new() { MinBusinessDaysBefore = 10 },
        new() { MinBusinessDaysBefore = 5, ChargePercent = 50m },
        new() { MinBusinessDaysBefore = 2, ChargePercent = 100m },
    ];

    [Fact]
    public void PercentRung_ChargesShareOfPlannedHours_AndOfTheDay()
    {
        var half = CancellationMath.Resolve(PercentLadder, 6, 8m, 4);
        Assert.Equal(4m, half.Hours);           // hourly / PAD: 50 % of 8 h
        Assert.Equal(0.5m, half.DayFraction);   // day rate: half the day
        Assert.False(half.Full);
        Assert.True(CancellationMath.Resolve(PercentLadder, 12, 8m, 4).IsFree);
    }

    [Fact]
    public void PercentRung_100_IsAFullCharge_LikeBelowTheLadder()
    {
        var full = CancellationMath.Resolve(PercentLadder, 3, 6m, 4);
        Assert.True(full.Full);
        Assert.Equal(6m, full.Hours);
        Assert.True(CancellationMath.Resolve(PercentLadder, 1, 6m, 4).Full);
    }

    [Fact]
    public void PercentRung_ExplicitHoursOrFractionWin()
    {
        List<CancellationStep> ladder =
            [new() { MinBusinessDaysBefore = 3, ChargePercent = 50m, ChargeHours = 2m, ChargeDayFraction = 0.25m }];
        var c = CancellationMath.Resolve(ladder, 4, 8m, 4);
        Assert.Equal(2m, c.Hours);
        Assert.Equal(0.25m, c.DayFraction);
    }
}
