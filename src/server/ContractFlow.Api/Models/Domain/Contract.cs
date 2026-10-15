namespace ContractFlow.Api.Models.Domain;

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
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<ContractTerms> TermsVersions { get; set; } = [];
    public List<Project> Projects { get; set; } = [];
}
