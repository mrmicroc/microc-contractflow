namespace ContractFlow.Api.Models.Domain;

/// <summary>A work site belonging to a project (substation, region, depot…).</summary>
public class Location
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}
