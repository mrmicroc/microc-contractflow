namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// The party that pays MICROC. May differ from the end client in multi-layer
/// chains — see <see cref="EndClient"/>.
/// </summary>
public class Customer
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Customer number or reference in the customer's own systems.</summary>
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<Contract> Contracts { get; set; } = [];
}
