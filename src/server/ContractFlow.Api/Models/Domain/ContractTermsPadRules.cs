namespace ContractFlow.Api.Models.Domain;

/// <summary>What the carry-over percentage is applied to (§4.3 / 7.1).</summary>
public enum CarryOverBasis
{
    /// <summary>The month's actual unused baseline — e.g. 12 baseline, 3 used
    /// → 20 % of 9 = 1.8 → 2 carried.</summary>
    UnusedBaseline,
    /// <summary>The month's whole baseline — 20 % of 12 = 2.4 → 3 (capped at
    /// the unused amount).</summary>
    Baseline,
}

/// <summary>Which PAD quantity the month's PAD line bills.</summary>
public enum PadInvoicing
{
    /// <summary>Utilised PAD only — unused allocation that expires is not billed.</summary>
    Utilised,
    /// <summary>Lump-sum allocation: the month bills the allocation it
    /// consumed — utilised within carry-in + baseline, PLUS what expired.
    /// Carried-out PAD bills in the receiving month when utilised or expired,
    /// so each allocated PAD is billed exactly once (opening carry + baseline
    /// − carry-out).</summary>
    AllocationConsumed,
}

/// <summary>How the booked-day minimum applies when one booked day is shared
/// between projects.</summary>
public enum SharedDayMinimum
{
    /// <summary>One minimum for the day, split between the projects in
    /// proportion to their (actual, else planned) hours.</summary>
    Proportional,
    /// <summary>Each project carries its own full minimum.</summary>
    PerProject,
}

/// <summary>
/// PAD rules of a terms version that a contract can tune — all effective-dated
/// with the rest of the version. Defaults follow a typical lump-sum PAD
/// contract; on terms without the PAD component none of them has any effect.
/// </summary>
public partial class ContractTerms
{
    /// <summary>What <see cref="CarryOverMaxPercent"/> is a share of — default
    /// the actual unused baseline.</summary>
    public CarryOverBasis CarryOverBasis { get; set; } = CarryOverBasis.UnusedBaseline;
    /// <summary>The carry-over cap rounds UP to a multiple of this — 1.00 =
    /// whole PAD (default); 0 = no rounding.</summary>
    public decimal CarryOverRoundUpTo { get; set; } = 1m;

    /// <summary>Which PAD quantity is billed each month — default utilised
    /// only; lump-sum contracts bill consumed allocation incl. expiry.</summary>
    public PadInvoicing PadInvoicing { get; set; } = PadInvoicing.Utilised;
    /// <summary>Price per Additional PAD when the approving request names no
    /// rate of its own; null = the ordinary <see cref="PadRate"/>.</summary>
    public decimal? AdditionalPadRate { get; set; }

    /// <summary>Minimum handling for booked days shared between projects.</summary>
    public SharedDayMinimum SharedDayMinimum { get; set; } = SharedDayMinimum.Proportional;

    /// <summary>Approved time with no booking (meetings, desktop work, document
    /// review, project management) consumes PAD at hours ÷ PadHoursPerDay
    /// instead of billing as hours. Off by default.</summary>
    public bool UnbookedTimeConsumesPad { get; set; }
    /// <summary>Unbooked time rounds up to this many minutes before
    /// conversion (default 15).</summary>
    public int UnbookedTimeIncrementMinutes { get; set; } = 15;

    /// <summary>Working-time start of a booked day when no start time was
    /// agreed by the notice deadline (default 08:00 local).</summary>
    public TimeOnly DefaultStartTime { get; set; } = new(8, 0);
    /// <summary>Business days before the booked day by which a start time
    /// must be agreed; later changes never postpone working time (default 4).</summary>
    public int StartTimeNoticeBusinessDays { get; set; } = 4;
}
