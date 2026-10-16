namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// Resources allowed to self-report time on a project without a booking — the
/// one-hour customer call nobody plans. An empty list keeps the project open
/// to every resource (backward compatible); once populated it restricts
/// self-reporting to members plus anyone holding a booking on the project.
/// </summary>
public class ProjectAccess
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
