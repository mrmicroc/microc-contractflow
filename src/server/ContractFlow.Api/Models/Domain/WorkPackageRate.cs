namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// Versioned package-level price overrides: when a booking targets this work package,
/// each set field wins over the contract terms in force — a null field falls
/// back to the contract. A new row with a later EffectiveFrom is a new version;
/// historical dates keep their historical package prices. Prices only — policy
/// (OT rules, PAD, cancellation) stays on the contract.
/// </summary>
public class WorkPackageRate
{
    public Guid Id { get; set; }
    public Guid WorkPackageId { get; set; }
    public WorkPackage WorkPackage { get; set; } = null!;

    /// <summary>
    /// Set = the (resource × work package) matrix cell — this version prices
    /// only that resource's work on the package. Null = the package-wide
    /// default every resource falls back to.
    /// </summary>
    public Guid? ResourceId { get; set; }
    public Resource? Resource { get; set; }

    /// <summary>
    /// Set when the row was auto-inherited from the contract on adding the
    /// resource to the project — an admin must review it against the terms
    /// before it is trusted. Cleared explicitly via the review endpoint.
    /// </summary>
    public bool NeedsReview { get; set; }

    /// <summary>First day this override set applies to.</summary>
    public DateOnly EffectiveFrom { get; set; }

    public decimal? HourlyRate { get; set; }
    public decimal? SaturdayRate { get; set; }
    public decimal? SundayRate { get; set; }
    public decimal? HolidayRate { get; set; }

    public decimal? DayRate { get; set; }
    public decimal? SaturdayDayRate { get; set; }
    public decimal? SundayDayRate { get; set; }
    public decimal? HolidayDayRate { get; set; }
    public decimal? HalfDayRate { get; set; }
    public decimal? HalfDayMaxHours { get; set; }

    /// <summary>Mobilisation fee when a confirmed period targets this package.</summary>
    public decimal? MobilisationFee { get; set; }

    public string? Notes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
