namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// Records that a project-month's PAD account has been closed. Closing writes
/// CarryOverOut/Expire here and CarryOverIn next month (design §6.4).
/// Corrections after close use Adjustment entries or a controlled reopen —
/// never a re-close.
/// </summary>
public class ProjectMonthClose
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime ClosedUtc { get; set; } = DateTime.UtcNow;
    public Guid? ClosedByUserId { get; set; }
}
