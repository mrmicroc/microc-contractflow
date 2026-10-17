namespace ContractFlow.Api.Models.Domain;

public enum ResourceType
{
    /// <summary>MICROC employee.</summary>
    Employee,
    /// <summary>External subcontractor compensated per agreement.</summary>
    Subcontractor,
    /// <summary>Contracted directly by the customer — MICROC plans but does not pay.</summary>
    DirectlyContracted,
}

/// <summary>
/// A person who can be booked onto assignments. Not necessarily a system user —
/// <see cref="UserId"/> links to a login when the resource reports time in the
/// app; subcontractors without accounts can be managed by admin.
/// </summary>
public class Resource
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    public ResourceType Type { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    /// <summary>Competency/certification records — expiry-aware list.</summary>
    public List<ResourceCompetency> CompetencyItems { get; set; } = [];
    /// <summary>Optional link to the AppUser this person signs in as.</summary>
    public Guid? UserId { get; set; }
    public AppUser? User { get; set; }
    /// <summary>
    /// Unguessable token authorising the personal .ics feed URL
    /// (/api/calendar/{id}/{token}.ics). Rotatable — null disables the feed.
    /// </summary>
    public string? FeedToken { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<CompensationAgreement> Agreements { get; set; } = [];
    public List<ResourceLeave> Leave { get; set; } = [];
}
