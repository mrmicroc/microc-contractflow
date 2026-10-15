namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// A per-diem claim for one day (design §6.2). The policy — none / statutory /
/// custom amount — lives on the compensation agreement; the claim itself just
/// records day + amount + currency so an approved statement is reproducible.
/// </summary>
public class AllowanceClaim
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    /// <summary>Optional project the subsistence was incurred under.</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    /// <summary>ISO 4217 code.</summary>
    public required string Currency { get; set; }
    public string? Notes { get; set; }

    public Guid? TimesheetId { get; set; }
    public Timesheet? Timesheet { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}
