using ContractFlow.Api.Models.Domain;
using ContractFlow.Api.Services;

namespace ContractFlow.Api.Tests.Services;

/// <summary>Configurable PAD rules: shared booked days, unbooked services,
/// working-time start — numeric examples from a lump-sum PAD contract.</summary>
public class PadRulesMathTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-4000-8000-00000000a001");
    private static readonly Guid B = Guid.Parse("00000000-0000-4000-8000-00000000b001");

    [Fact]
    public void SharedDay_UnderEightHours_SplitsTheMinimumByTime()
    {
        // 2 h on one project + 4 h on the other → the 1.00 minimum splits 0.33 / 0.67.
        var split = PadMath.SplitDay([(A, 2m), (B, 4m)], 8m, 1m);
        Assert.Equal(0.33m, split[0].Pad);
        Assert.Equal(0.67m, split[1].Pad);
        Assert.Equal(1.00m, split.Sum(s => s.Pad));
    }

    [Fact]
    public void SharedDay_OverEightHours_EachProjectItsActualTime()
    {
        // 6 h + 6 h = 12 h → 1.50 PAD, 0.75 each.
        var split = PadMath.SplitDay([(A, 6m), (B, 6m)], 8m, 1m);
        Assert.Equal(0.75m, split[0].Pad);
        Assert.Equal(0.75m, split[1].Pad);
    }

    [Fact]
    public void SharedDay_PerProjectMode_EachCarriesItsOwnMinimum()
    {
        var split = PadMath.SplitDay([(A, 2m), (B, 4m)], 8m, 1m, SharedDayMinimum.PerProject);
        Assert.Equal(1m, split[0].Pad);
        Assert.Equal(1m, split[1].Pad);
    }

    [Fact]
    public void SharedDay_ThreeWaySplit_SumsExactly()
    {
        var split = PadMath.SplitDay([(A, 1m), (B, 1m), (Guid.NewGuid(), 1m)], 8m, 1m);
        Assert.Equal(1.00m, split.Sum(s => s.Pad));
        Assert.Equal(0.33m, split[0].Pad);
    }

    [Theory]
    [InlineData(2.00, 15, 0.25)]    // 2 h ÷ 8
    [InlineData(1.10, 15, 0.1563)]  // rounds up to 1.25 h → 0.15625
    [InlineData(0.10, 15, 0.0313)]  // 6 min → 15 min → 0.03125
    [InlineData(1.10, 0, 0.1375)]   // no increment → exact hours
    public void UnbookedServices_ConsumeHoursOverEight_InIncrements(decimal hours, int minutes, decimal pad)
        => Assert.Equal(pad, PadMath.UnbookedPad(hours, minutes, 8m));

    [Fact]
    public void StartTime_NoneAgreed_DefaultApplies()
        => Assert.Equal(new TimeOnly(8, 0), PadMath.EffectiveStartTime([], new TimeOnly(8, 0)));

    [Fact]
    public void StartTime_AgreedInTime_Applies()
        => Assert.Equal(new TimeOnly(9, 0),
            PadMath.EffectiveStartTime([(new TimeOnly(9, 0), true)], new TimeOnly(8, 0)));

    [Fact]
    public void StartTime_LateChangeNeverPostpones()
        => Assert.Equal(new TimeOnly(9, 0), PadMath.EffectiveStartTime(
            [(new TimeOnly(9, 0), true), (new TimeOnly(11, 0), false)], new TimeOnly(8, 0)));

    [Fact]
    public void StartTime_LateChangeWithoutEarlierAgreement_KeepsDefault()
        => Assert.Equal(new TimeOnly(8, 0), PadMath.EffectiveStartTime(
            [(new TimeOnly(10, 0), false)], new TimeOnly(8, 0)));

    [Fact]
    public void StartTime_LateChangeEarlier_BringsItForward()
        => Assert.Equal(new TimeOnly(7, 0), PadMath.EffectiveStartTime(
            [(new TimeOnly(9, 0), true), (new TimeOnly(7, 0), false)], new TimeOnly(8, 0)));
}
