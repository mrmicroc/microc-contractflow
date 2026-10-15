namespace ContractFlow.Api.Models.Domain;

public enum PerDiemPolicy
{
    /// <summary>No per diem compensation.</summary>
    None,
    /// <summary>Statutory per diem (Swedish tax authority norms).</summary>
    Statutory,
    /// <summary>Fixed amount per the resource's agreement — see PerDiemAmount.</summary>
    Custom,
}

/// <summary>
/// Versioned compensation terms for one resource — what MICROC pays them,
/// completely separate from what the customer is billed (design §1.1). Scoped
/// per resource and optionally per contract (<see cref="ContractId"/> null =
/// the resource's default agreement). Effective-dated, never rewritten.
/// </summary>
public class CompensationAgreement
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    /// <summary>Null = default agreement covering every contract.</summary>
    public Guid? ContractId { get; set; }
    public Contract? Contract { get; set; }

    /// <summary>1-based, assigned by the server (max + 1 within the same scope).</summary>
    public int Version { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>ISO 4217 currency code, e.g. "SEK".</summary>
    public required string Currency { get; set; }
    public decimal? HourlyRate { get; set; }
    public decimal? SaturdayRate { get; set; }
    public decimal? SundayRate { get; set; }
    public decimal? HolidayRate { get; set; }
    /// <summary>Overtime starts after this many hours in a day.</summary>
    public int OvertimeAfterHours { get; set; } = 8;
    /// <summary>Overtime multiplier in percent (150 = ×1.5).</summary>
    public decimal OvertimePercent { get; set; } = 150m;
    /// <summary>Percent of hourly rate paid for travel time.</summary>
    public decimal? TravelTimePercent { get; set; }
    /// <summary>Mileage compensation per km in the agreement currency.</summary>
    public decimal? MileageRate { get; set; }
    public PerDiemPolicy PerDiem { get; set; } = PerDiemPolicy.None;
    /// <summary>Daily amount when <see cref="PerDiem"/> is Custom.</summary>
    public decimal? PerDiemAmount { get; set; }
    /// <summary>Which expenses are reimbursable, caps, approval requirements…</summary>
    public string? ExpensePolicy { get; set; }
    public string? Notes { get; set; }
}
