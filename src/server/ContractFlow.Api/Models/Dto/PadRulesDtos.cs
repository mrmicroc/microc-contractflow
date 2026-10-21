using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Models.Dto;

/// <summary>The PAD rules of a terms version (see <c>ContractTermsPadRules.cs</c>).</summary>
public record PadRulesDto(
    CarryOverBasis CarryOverBasis, decimal CarryOverRoundUpTo,
    PadInvoicing PadInvoicing, decimal? AdditionalPadRate,
    SharedDayMinimum SharedDayMinimum,
    bool UnbookedTimeConsumesPad, int UnbookedTimeIncrementMinutes,
    TimeOnly DefaultStartTime, int StartTimeNoticeBusinessDays)
{
    public static PadRulesDto From(ContractTerms t) => new(
        t.CarryOverBasis, t.CarryOverRoundUpTo, t.PadInvoicing, t.AdditionalPadRate,
        t.SharedDayMinimum, t.UnbookedTimeConsumesPad, t.UnbookedTimeIncrementMinutes,
        t.DefaultStartTime, t.StartTimeNoticeBusinessDays);
}

/// <summary>PAD-rule fields of a new terms version — all optional with the
/// defaults of <see cref="ContractTerms"/>.</summary>
public partial class ContractTermsRequest
{
    public CarryOverBasis CarryOverBasis { get; set; } = CarryOverBasis.UnusedBaseline;
    [Range(0, 100)]
    public decimal CarryOverRoundUpTo { get; set; } = 1m;
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
        t.CarryOverRoundUpTo = CarryOverRoundUpTo;
        t.PadInvoicing = PadInvoicing;
        t.AdditionalPadRate = AdditionalPadRate;
        t.SharedDayMinimum = SharedDayMinimum;
        t.UnbookedTimeConsumesPad = UnbookedTimeConsumesPad;
        t.UnbookedTimeIncrementMinutes = UnbookedTimeIncrementMinutes;
        t.DefaultStartTime = DefaultStartTime;
        t.StartTimeNoticeBusinessDays = StartTimeNoticeBusinessDays;
    }
}
