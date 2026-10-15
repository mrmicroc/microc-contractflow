using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;

namespace ContractFlow.Api.Tests.Services;

/// <summary>Pure PAD rules — tests built on the spec's worked examples (§4/§5).</summary>
public class PadMathTests
{
    [Theory]
    [InlineData(8, 1.00)]   // standard day commits exactly 1 PAD
    [InlineData(4, 1.00)]   // shorter day still commits the 1-PAD minimum
    [InlineData(10, 1.25)]  // longer days proportionally more
    [InlineData(16, 2.00)]
    public void CommitAmount_FollowsMinimumAndProportion(decimal hours, decimal expected)
        => Assert.Equal(expected, PadMath.CommitAmount(hours, padHoursPerDay: 8m, minPadPerBookedDay: 1m));

    [Fact]
    public void CarryOver_SpecExample_Baseline10Used7_Carries2()
    {
        // Spec §5: 10 baseline days, 7 used → carry-over 2 (20 % of baseline,
        // rounded up, capped at unused), 1 expires.
        Assert.Equal(2m, PadMath.CarryOver(baseline: 10m, utilised: 7m, maxPercent: 20m));
        Assert.Equal(1m, PadMath.Expiry(baseline: 10m, utilised: 7m, maxPercent: 20m));
    }

    [Theory]
    [InlineData(10, 10, 0, 0)]   // fully used — nothing to carry or expire
    [InlineData(10, 0, 2, 8)]    // nothing used — carry capped at 20 % of baseline
    [InlineData(11, 0, 3, 8)]    // 20 % of 11 = 2.2 → rounds up to 3
    [InlineData(10, 8.5, 1.5, 0)] // unused 1.5 < cap 2 → carries the actual unused
    public void CarryOver_Rules(decimal baseline, decimal used, decimal carry, decimal expire)
    {
        Assert.Equal(carry, PadMath.CarryOver(baseline, used, 20m));
        Assert.Equal(expire, PadMath.Expiry(baseline, used, 20m));
    }

    private static PadTransaction Tx(PadTransactionType type, decimal amount) =>
        new() { Type = type, Amount = amount };

    [Fact]
    public void Sum_Free_MatchesSpecDashboardExample()
    {
        // Spec §4.2: baseline 8 + carry-over 1 = 9 available; utilised 2.5,
        // committed 4 → free 2.5. In ledger terms the dashboard's "committed 4"
        // is the OPEN commitment — a 6.5 commit where 2.5 was utilised.
        var txs = new[]
        {
            Tx(PadTransactionType.BaselineSet, 8m),
            Tx(PadTransactionType.CarryOverIn, 1m),
            Tx(PadTransactionType.Utilise, 2.5m),
            Tx(PadTransactionType.Commit, 6.5m),
        };
        var sums = PadMath.Sum(txs);
        Assert.Equal(9m, sums.TotalAvailable);
        Assert.Equal(2.5m, sums.Free);
        Assert.Equal(4m, sums.CommittedOpen);
    }

    [Fact]
    public void Sum_BaselineSetDeltas_AndReleaseNetCorrectly()
    {
        var txs = new[]
        {
            Tx(PadTransactionType.BaselineSet, 10m),
            Tx(PadTransactionType.BaselineSet, -2m), // "set to 8" amendment
            Tx(PadTransactionType.Commit, 5m),
            Tx(PadTransactionType.Release, 2m),
            Tx(PadTransactionType.Utilise, 1m),
        };
        var sums = PadMath.Sum(txs);
        Assert.Equal(8m, sums.Baseline);
        Assert.Equal(2m, sums.CommittedOpen); // 5 − 2 − 1
        Assert.Equal(5m, sums.Free);          // 8 − 1 utilised − 2 open
    }

    [Fact]
    public void Sum_MonthCanExistOnCarryOverAlone()
    {
        // Carry-over may enter a month with zero baseline (§4.3).
        var sums = PadMath.Sum([Tx(PadTransactionType.CarryOverIn, 2m)]);
        Assert.Equal(0m, sums.Baseline);
        Assert.Equal(2m, sums.TotalAvailable);
        Assert.Equal(2m, sums.Free);
    }
}
