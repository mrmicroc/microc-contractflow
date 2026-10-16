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
    public void EmptyLadder_FallsBackToBinaryDeadline()
    {
        // Legacy behaviour: inside deadline → timely/free, else full charge.
        Assert.True(CancellationMath.Resolve(null, 6, 8m, 4).IsFree);
        Assert.True(CancellationMath.Resolve([], 6, 8m, 4).IsFree);
        Assert.Equal(8m, CancellationMath.Resolve(null, 2, 8m, 4).Hours);
    }
}
