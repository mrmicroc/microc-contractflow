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

    private static readonly CarryOverPolicy P = CarryOverPolicy.Default;

    [Fact]
    public void CarryOver_Baseline10Used7_Carries1_UnusedBasis()
    {
        // 20 % of the 3 unused = 0.6 → rounds up to 1 carried; 2 expire.
        Assert.Equal(1m, PadMath.CarryOver(baseline: 10m, utilised: 7m, P));
        Assert.Equal(2m, PadMath.Expiry(baseline: 10m, utilised: 7m, P));
    }

    [Fact]
    public void CarryOver_BaselineBasis_KeepsTheOlderRule()
    {
        // Configurable: 20 % of the whole baseline (10 → 2), capped at unused.
        var whole = P with { Basis = CarryOverBasis.Baseline };
        Assert.Equal(2m, PadMath.CarryOver(10m, 7m, whole));
        Assert.Equal(1m, PadMath.Expiry(10m, 7m, whole));
        Assert.Equal(1.5m, PadMath.CarryOver(10m, 8.5m, whole));
    }

    /// <summary>The contract document's carry-over table: monthly baseline 12,
    /// max 20 % of the actual unused baseline rounded up to whole PAD, never
    /// more than what is unused.</summary>
    [Theory]
    [InlineData(3.00, 2.00)]
    [InlineData(7.00, 1.00)]
    [InlineData(8.00, 1.00)]
    [InlineData(10.50, 1.00)]
    [InlineData(11.00, 1.00)]
    [InlineData(11.50, 0.50)]
    [InlineData(11.90, 0.10)]
    [InlineData(12.00, 0.00)]
    public void CarryOver_DocumentTable(decimal utilised, decimal maxCarry)
        => Assert.Equal(maxCarry, PadMath.CarryOver(12m, utilised, P));

    [Fact]
    public void CarryOver_RoundingStepConfigurable()
    {
        Assert.Equal(1.8m, PadMath.CarryOver(12m, 3m, P with { RoundingStep = 0m }));     // no rounding
        Assert.Equal(2m, PadMath.CarryOver(12m, 3m, P with { RoundingStep = 0.5m }));     // 1.8 → 2.0
        Assert.Equal(0.25m, PadMath.CarryOver(12m, 11m, P with { RoundingStep = 0.25m })); // 0.2 → 0.25
    }

    /// <summary>Baseline 12, 3 used → 9 unused → 20 % = 1.8; 5 used → 7
    /// unused → 1.4 — by direction and step.</summary>
    [Theory]
    [InlineData(CarryOverRounding.Up, 1.0, 3, 2.0)]
    [InlineData(CarryOverRounding.Down, 1.0, 3, 1.0)]
    [InlineData(CarryOverRounding.Nearest, 1.0, 3, 2.0)]
    [InlineData(CarryOverRounding.Nearest, 1.0, 5, 1.0)]
    [InlineData(CarryOverRounding.Down, 0.5, 3, 1.5)]
    [InlineData(CarryOverRounding.Nearest, 0.5, 5, 1.5)]
    [InlineData(CarryOverRounding.Down, 0, 3, 1.8)]
    public void CarryOver_RoundingDirectionConfigurable(
        CarryOverRounding direction, double step, decimal utilised, double expected)
        => Assert.Equal((decimal)expected, PadMath.CarryOver(12m, utilised,
            P with { Rounding = direction, RoundingStep = (decimal)step }));

    [Fact]
    public void CarryOver_RoundingDown_CanReachZero_AllExpires()
    {
        // 11 used → 1 unused → 0.2 rounded down to 0 → nothing carried, 1 expires.
        var down = P with { Rounding = CarryOverRounding.Down };
        Assert.Equal(0m, PadMath.CarryOver(12m, 11m, down));
        Assert.Equal(1m, PadMath.Expiry(12m, 11m, down));
    }

    [Fact]
    public void CarryOver_AbsoluteCeiling_AppliesAfterRounding()
    {
        // Nothing used: 20 % of 12 unused = 2.4 → 3, held under a 2 PAD ceiling.
        Assert.Equal(3m, PadMath.CarryOver(12m, 0m, P));
        Assert.Equal(2m, PadMath.CarryOver(12m, 0m, P with { MaxPad = 2m }));
        Assert.Equal(10m, PadMath.Expiry(12m, 0m, P with { MaxPad = 2m }));
    }

    [Fact]
    public void CarryOver_PercentConfigurable()
        => Assert.Equal(5m, PadMath.CarryOver(12m, 2m, P with { MaxPercent = 50m })); // 50 % of 10

    [Theory]
    [InlineData(10, 10, 0, 0)]   // fully used — nothing to carry or expire
    [InlineData(10, 0, 2, 8)]    // nothing used — 20 % of 10 unused = 2
    [InlineData(11, 0, 3, 8)]    // 20 % of 11 = 2.2 → rounds up to 3
    [InlineData(10, 8.5, 1, 0.5)] // 20 % of 1.5 = 0.3 → 1 (capped at the 1.5 unused)
    public void CarryOver_Rules(decimal baseline, decimal used, decimal carry, decimal expire)
    {
        Assert.Equal(carry, PadMath.CarryOver(baseline, used, P));
        Assert.Equal(expire, PadMath.Expiry(baseline, used, P));
    }

    [Fact]
    public void CarryOver_CarryInConsumedBeforeBaseline()
    {
        // §4.3: carry-over is consumed before baseline. Baseline 10 + carry-in 2,
        // utilised 11 → carry covers 2, baseline 9 → 1 unused → carries 1
        // (under the cap of 2), nothing expires.
        Assert.Equal(9m, PadMath.BaselineUsed(10m, 11m, carryIn: 2m));
        Assert.Equal(1m, PadMath.CarryOver(10m, 11m, P, carryIn: 2m));
        Assert.Equal(0m, PadMath.Expiry(10m, 11m, P, carryIn: 2m));
    }

    [Fact]
    public void CarryOver_BaselineFirst_Configurable()
    {
        // Baseline first: 10 baseline + 2 carry-in, 7 used → baseline 7 used,
        // 3 unused → 20 % = 0.6 → 1 carried, 2 expire (the 2 carry-in expire
        // separately as residual). Carry first would have used only 5 baseline.
        var bf = P with { Consumption = CarryOverConsumption.BaselineFirst };
        Assert.Equal(7m, PadMath.BaselineUsed(10m, 7m, carryIn: 2m, consumption: CarryOverConsumption.BaselineFirst));
        Assert.Equal(5m, PadMath.BaselineUsed(10m, 7m, carryIn: 2m));
        Assert.Equal(1m, PadMath.CarryOver(10m, 7m, bf, carryIn: 2m));
        Assert.Equal(2m, PadMath.Expiry(10m, 7m, bf, carryIn: 2m));
    }

    [Fact]
    public void CarryResiduals_BaselineFirst_LeavesCarryResidual()
    {
        // Baseline 10 + carry-in 2, 7 used: baseline covers it all, so the 2
        // carried PAD survive as a residual (to re-carry or expire at close).
        var txs = new[]
        {
            new PadTransaction { Year = 2027, Month = 2, Type = PadTransactionType.BaselineSet, Amount = 10m },
            new PadTransaction { Year = 2027, Month = 2, Type = PadTransactionType.CarryOverIn, Amount = 2m,
                OriginYear = 2027, OriginMonth = 1 },
            new PadTransaction { Year = 2027, Month = 2, Type = PadTransactionType.Utilise, Amount = 7m },
        };
        var residual = Assert.Single(PadMath.CarryResiduals(txs, 2027, 2, CarryOverConsumption.BaselineFirst));
        Assert.Equal(2m, residual.Residual);
        Assert.Empty(PadMath.CarryResiduals(txs, 2027, 2));
    }

    [Fact]
    public void CarryOver_BorrowedByPreviousMonth_ConsumesBaseline()
    {
        // 2 PAD lent to last month's overrun eat this month's baseline directly.
        Assert.Equal(7m, PadMath.BaselineUsed(10m, 5m, borrowed: 2m));
        Assert.Equal(1m, PadMath.CarryOver(10m, 5m, P, borrowed: 2m)); // 20 % of 3 unused → 1
        Assert.Equal(2m, PadMath.Expiry(10m, 5m, P, borrowed: 2m));
    }

    [Fact]
    public void CommitAmount_RoundsToLedgerPrecision()
        => Assert.Equal(1.1313m, PadMath.CommitAmount(9.05m, 8m, 1m)); // 1.13125 → 4 dp

    [Fact]
    public void CarryResiduals_UtilisationEatsCarryFirst()
    {
        // Month with baseline 10 + carry-in 2 (from the month before), 3 used:
        // the carry is fully consumed, no residual survives to re-carry.
        var txs = new[]
        {
            new PadTransaction { Year = 2027, Month = 2, Type = PadTransactionType.BaselineSet, Amount = 10m },
            new PadTransaction { Year = 2027, Month = 2, Type = PadTransactionType.CarryOverIn, Amount = 2m,
                OriginYear = 2027, OriginMonth = 1 },
            new PadTransaction { Year = 2027, Month = 2, Type = PadTransactionType.Utilise, Amount = 3m },
        };
        Assert.Empty(PadMath.CarryResiduals(txs, 2027, 2));
    }

    // ── Continuation beyond commitment (§4.3 ①–④) ───────────────────────────

    private static PadMonthSums Sums(decimal baseline = 0m, decimal additional = 0m,
        decimal utilised = 0m, decimal borrowed = 0m) =>
        new(baseline, 0m, additional, 0m, utilised, 0m, 0m, 0m, borrowed, 0m);

    [Fact]
    public void Overrun_Step1_UsesCurrentFreeFirst()
    {
        var split = PadMath.AllocateOverrun(0.5m, Sums(baseline: 5m, utilised: 2m), Sums(baseline: 5m), 0m);
        Assert.Equal(0.5m, split.FromCurrentFree);
        Assert.Equal(0m, split.FromAdditional + split.FromNextMonthBaseline + split.AutoAdditional);
    }

    [Fact]
    public void Overrun_Step2_ThenApprovedAdditional()
    {
        // Own pool exhausted (baseline 2 fully used); 1 approved additional free.
        var current = Sums(baseline: 2m, additional: 1m, utilised: 2m);
        var split = PadMath.AllocateOverrun(0.75m, current, Sums(baseline: 5m), additionalAvailable: 1m);
        Assert.Equal(0m, split.FromCurrentFree);
        Assert.Equal(0.75m, split.FromAdditional);
    }

    [Fact]
    public void Overrun_Step3_ThenNextMonthBaseline_FlagsConflict()
    {
        var current = Sums(baseline: 2m, utilised: 2m);
        // Next month: baseline 1, already fully committed elsewhere → free 0.
        var next = new PadMonthSums(1m, 0m, 0m, 1m, 0m, 0m, 0m, 0m, 0m, 0m);
        var split = PadMath.AllocateOverrun(0.5m, current, next, 0m);
        Assert.Equal(0.5m, split.FromNextMonthBaseline);
        Assert.True(split.NextMonthConflict);
    }

    [Fact]
    public void Overrun_Step4_RestBecomesAutoAdditional()
    {
        var current = Sums(baseline: 1m, utilised: 0.75m);
        var split = PadMath.AllocateOverrun(1.5m, current, next: null, 0m); // next month closed
        Assert.Equal(0.25m, split.FromCurrentFree);
        Assert.Equal(0m, split.FromNextMonthBaseline);
        Assert.Equal(1.25m, split.AutoAdditional);
        Assert.Equal(1.5m, split.Total);
    }

    [Fact]
    public void Free_BorrowNextMonth_ReducesLendingMonth()
    {
        var sums = PadMath.Sum([
            Tx(PadTransactionType.BaselineSet, 5m),
            Tx(PadTransactionType.BorrowNextMonth, 0.5m),
        ]);
        Assert.Equal(4.5m, sums.Free);
        Assert.Equal(5m, sums.TotalAvailable);
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
