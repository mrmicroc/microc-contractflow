namespace ContractFlow.Api.Models.Domain;

/// <summary>Attention state of a competency record — computed per day, never
/// stored: certification refreshes are date-driven facts.</summary>
public enum CompetencyStatus
{
    /// <summary>No expiry date recorded.</summary>
    Unbounded,
    /// <summary>Expires, but not yet inside the reminder window.</summary>
    Valid,
    /// <summary>Inside the reminder window — renewal should start.</summary>
    Due,
    /// <summary>Past the expiry date.</summary>
    Expired,
}

/// <summary>
/// One competency/certification record on a resource — e.g. a course or
/// authorisation that may expire. Optional reminder window turns the record
/// into a flag for renewal before the expiry date hits.
/// </summary>
public class ResourceCompetency
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;

    /// <summary>Free-text label ("Course X", "Authorisation A"…).</summary>
    public required string Label { get; set; }
    /// <summary>Null = never expires.</summary>
    public DateOnly? ExpiryDate { get; set; }
    /// <summary>Days before expiry the record starts flagging for renewal.
    /// Null = flag only once expired.</summary>
    public int? ReminderDays { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Computes the attention state as of <paramref name="today"/>.</summary>
    public CompetencyStatus StatusOn(DateOnly today)
    {
        if (ExpiryDate is not { } expiry) return CompetencyStatus.Unbounded;
        if (expiry <= today) return CompetencyStatus.Expired;
        if (ReminderDays is { } days && expiry.AddDays(-days) <= today)
            return CompetencyStatus.Due;
        return CompetencyStatus.Valid;
    }
}
