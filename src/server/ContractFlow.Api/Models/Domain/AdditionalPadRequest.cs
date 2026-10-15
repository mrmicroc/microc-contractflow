namespace ContractFlow.Api.Models.Domain;

public enum AdditionalPadStatus
{
    Draft,
    Submitted,
    Approved,
    Rejected,
    Withdrawn,
}

/// <summary>
/// A request for PAD beyond the month's baseline + carry-over (design §3).
/// Approving writes an <see cref="PadTransactionType.AdditionalApproved"/>
/// ledger entry, enlarging that month's available PAD. A booking needing
/// additional PAD is prepared but only commercially confirmed once such a
/// request is approved (design §4.3).
/// </summary>
public class AdditionalPadRequest
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Rate per PAD if it differs from contract terms (optional).</summary>
    public decimal? Rate { get; set; }
    public string? Reason { get; set; }
    /// <summary>Customer-side reference for the approval (order no, mail).</summary>
    public string? Reference { get; set; }

    public AdditionalPadStatus Status { get; set; } = AdditionalPadStatus.Submitted;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public DateTime? DecidedUtc { get; set; }
}
