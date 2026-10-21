using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Models.Dto;

/// <summary>The PAD rules of a terms version (see <c>ContractTermsPadRules.cs</c>).</summary>
public record PadRulesDto(
    CarryOverBasis CarryOverBasis, CarryOverRounding CarryOverRounding,
    decimal CarryOverRoundingStep, decimal? CarryOverMaxPad,
    CarryOverConsumption CarryOverConsumption,
    PadInvoicing PadInvoicing, decimal? AdditionalPadRate,
    SharedDayMinimum SharedDayMinimum,
    bool UnbookedTimeConsumesPad, int UnbookedTimeIncrementMinutes,
    TimeOnly DefaultStartTime, int StartTimeNoticeBusinessDays)
{
    public static PadRulesDto From(ContractTerms t) => new(
        t.CarryOverBasis, t.CarryOverRounding, t.CarryOverRoundingStep, t.CarryOverMaxPad,
        t.CarryOverConsumption, t.PadInvoicing, t.AdditionalPadRate,
        t.SharedDayMinimum, t.UnbookedTimeConsumesPad, t.UnbookedTimeIncrementMinutes,
        t.DefaultStartTime, t.StartTimeNoticeBusinessDays);
}

/// <summary>PAD-rule fields of a new terms version — all optional with the
/// defaults of <see cref="ContractTerms"/>.</summary>
public partial class ContractTermsRequest
{
    public CarryOverBasis CarryOverBasis { get; set; } = CarryOverBasis.UnusedBaseline;
    public CarryOverRounding CarryOverRounding { get; set; } = CarryOverRounding.Up;
    /// <summary>Rounding step for the carry-over cap; 0 = no rounding.</summary>
    [Range(0, 100)]
    public decimal CarryOverRoundingStep { get; set; } = 1m;
    /// <summary>Absolute ceiling on PAD carried out of one month; null = none.</summary>
    [Range(0, 10000)]
    public decimal? CarryOverMaxPad { get; set; }
    public CarryOverConsumption CarryOverConsumption { get; set; } = CarryOverConsumption.CarryOverFirst;
    public PadInvoicing PadInvoicing { get; set; } = PadInvoicing.Utilised;
    public decimal? AdditionalPadRate { get; set; }
    public SharedDayMinimum SharedDayMinimum { get; set; } = SharedDayMinimum.Proportional;
    public bool UnbookedTimeConsumesPad { get; set; }
    [Range(0, 480)]
    public int UnbookedTimeIncrementMinutes { get; set; } = 15;
    public TimeOnly DefaultStartTime { get; set; } = new(8, 0);
    [Range(0, 60)]
    public int StartTimeNoticeBusinessDays { get; set; } = 4;

    /// <summary>Copies the PAD-rule fields onto a new terms version.</summary>
    public void ApplyPadRules(ContractTerms t)
    {
        t.CarryOverBasis = CarryOverBasis;
        t.CarryOverRounding = CarryOverRounding;
        t.CarryOverRoundingStep = CarryOverRoundingStep;
        t.CarryOverMaxPad = CarryOverMaxPad;
        t.CarryOverConsumption = CarryOverConsumption;
        t.PadInvoicing = PadInvoicing;
        t.AdditionalPadRate = AdditionalPadRate;
        t.SharedDayMinimum = SharedDayMinimum;
        t.UnbookedTimeConsumesPad = UnbookedTimeConsumesPad;
        t.UnbookedTimeIncrementMinutes = UnbookedTimeIncrementMinutes;
        t.DefaultStartTime = DefaultStartTime;
        t.StartTimeNoticeBusinessDays = StartTimeNoticeBusinessDays;
    }
}
