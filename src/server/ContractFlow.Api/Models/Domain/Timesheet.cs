namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// Draft → Submitted → Approved | Returned | Rejected → Invoiced (design §3).
/// Approved is locked — corrections go through the audited admin reopen;
/// Invoiced is set once every billable row sits on an issued specification.
/// </summary>
public enum TimesheetStatus
{
    Draft,
    Submitted,
    Approved,
    Returned,
    Rejected,
    /// <summary>Every counted entry and rechargeable expense of the week is on
    /// an issued (or paid) invoice specification.</summary>
    Invoiced,
}

public enum TimesheetReviewAction
{
    Submitted,
    Approved,
    Returned,
    Rejected,
    Reopened,
}

/// <summary>One step in a timesheet's review history — append-only, so a
/// returned-and-resubmitted week keeps every reviewer comment (design §6.2).</summary>
public class TimesheetReview
{
    public Guid Id { get; set; }
    public Guid TimesheetId { get; set; }
    public Timesheet Timesheet { get; set; } = null!;
    public TimesheetReviewAction Action { get; set; }
    public string? Note { get; set; }
    public Guid? ByUserId { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
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

    public List<TimesheetReview> Reviews { get; set; } = [];

    public List<TimeEntry> Entries { get; set; } = [];
    public List<TravelRecord> TravelRecords { get; set; } = [];
    public List<Expense> Expenses { get; set; } = [];
    public List<AllowanceClaim> AllowanceClaims { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
