namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// Draft → Submitted → Approved | Returned | Rejected (design §3). Approved is
/// locked — corrections go via auditable adjustments; Invoiced arrives with
/// the economy phase.
/// </summary>
public enum TimesheetStatus
{
    Draft,
    Submitted,
    Approved,
    Returned,
    Rejected,
}

/// <summary>
/// Submission container per resource per ISO week (design §3): collecting that
/// week's time entries, travel records, expenses and allowance claims. Rows
/// carry TimesheetId — submit assigns it, return/reject releases them back to
/// draft.
/// </summary>
public class Timesheet
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;

    public int Year { get; set; }
    /// <summary>ISO-8601 week number (Monday-first), matching the UI.</summary>
    public int IsoWeek { get; set; }

    public TimesheetStatus Status { get; set; } = TimesheetStatus.Draft;
    public DateTime? SubmittedUtc { get; set; }
    public DateTime? ReviewedUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    /// <summary>Reviewer comment on return/reject.</summary>
    public string? ReviewerNote { get; set; }

    public List<TimeEntry> Entries { get; set; } = [];
    public List<TravelRecord> TravelRecords { get; set; } = [];
    public List<Expense> Expenses { get; set; } = [];
    public List<AllowanceClaim> AllowanceClaims { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
