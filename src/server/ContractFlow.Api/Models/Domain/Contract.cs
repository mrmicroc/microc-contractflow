namespace ContractFlow.Api.Models.Domain;

/// <summary>Which commercial model the contract bills under (design §4/§5).</summary>
public enum ContractModel
{
    /// <summary>Pre-allocated days (PAD) per month with carry-over rules.</summary>
    PadBased,
    /// <summary>Pure hourly billing against the contract's rate set.</summary>
    Hourly,
}

public class Contract
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public Guid? EndClientId { get; set; }
    public EndClient? EndClient { get; set; }

    public required string Name { get; set; }
    /// <summary>The customer's own contract/framework reference.</summary>
    public string? ExternalReference { get; set; }
    public ContractModel Model { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<ContractTerms> TermsVersions { get; set; } = [];
    public List<Project> Projects { get; set; } = [];
}
