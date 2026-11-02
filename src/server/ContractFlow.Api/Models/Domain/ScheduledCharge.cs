namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// A one-off amount billed on a given date — a milestone payment, set-up fee,
/// instalment of a payment plan. Contract-level and not versioned: it is a
/// dated event, not a rule. Bills once, on the invoice specification of
/// <see cref="ProjectId"/> for the month containing <see cref="Date"/>
/// (null project = the contract's first active project by name).
/// </summary>
public class ScheduledCharge
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public required string Description { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
