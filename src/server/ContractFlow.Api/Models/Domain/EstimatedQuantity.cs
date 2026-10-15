namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// Tender quantity for a project — used for monitoring actual-vs-estimated
/// utilisation (design §8). Never a contractual guarantee.
/// </summary>
public class EstimatedQuantity
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>What is estimated, e.g. "Inspection days 2026".</summary>
    public required string Label { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Unit of measure, e.g. "days", "PAD", "hours".</summary>
    public required string Unit { get; set; }
    public string? Notes { get; set; }
}
