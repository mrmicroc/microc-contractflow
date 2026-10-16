namespace ContractFlow.Api.Models.Domain;

/// <summary>A named sub-assignment under a project — a site, region, depot or
/// any other part bookings and reporting can target. Carries versioned rate
/// overrides over the contract terms.</summary>
public class WorkPackage
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }

    /// <summary>Versioned package-specific price overrides over the contract terms.</summary>
    public List<WorkPackageRate> Rates { get; set; } = [];
}
