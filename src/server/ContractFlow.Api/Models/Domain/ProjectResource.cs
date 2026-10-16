namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// A resource staffed on a project — the commercial roster. Distinct from
/// <see cref="ProjectAccess"/> (self-reporting permission): membership decides
/// who may be booked, which resources appear in rate-matrix selectors, and
/// triggers inheritance of contract-default rates per work package (created
/// with <see cref="WorkPackageRate.NeedsReview"/> set).
/// </summary>
public class ProjectResource
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
