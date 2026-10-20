namespace ContractFlow.Api.Models.Domain;

/// <summary>What the reported hours represent (design §3 Execution).</summary>
public enum TimeEntryCategory
{
    /// <summary>Active work.</summary>
    Work,
    /// <summary>On site but prevented from working (customer-caused standby).</summary>
    Waiting,
    /// <summary>Available on standby per instruction.</summary>
    Standby,
    /// <summary>Unpaid break — kept for attendance truth, never payable/billable.</summary>
    Break,
}

/// <summary>How the customer is billed for a reported row (design §5).</summary>
public enum EntryBilling
{
    /// <summary>Follow the contract terms' components — hourly or day-rate
    /// pricing; on PAD contracts booking-bound rows are covered by the PAD line.</summary>
    Contract,
    /// <summary>Force plain hourly billing (contract hourly/day-type rates) —
    /// for self-reported ad-hoc work like a short customer call that must not
    /// consume a PAD day or trigger a day rate.</summary>
    Hours,
}

/// <summary>
/// One reported day-part on one project (a day may split across several
/// projects → one entry per project). Execution ledger data, separate from the
/// planning booking it optionally links to (design §1.1). The three hour
/// quantities are kept apart: ActualHours is what the resource reported;
/// PayableHours (what MICROC owes the resource) and BillableHours (what the
/// customer may be invoiced) are computed at approval and frozen here so a
/// month reconciles years later (§5).
/// </summary>
public class TimeEntry
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    /// <summary>Optional link to the booking this work was performed under.</summary>
    public Guid? BookingId { get; set; }
    public Booking? Booking { get; set; }
    /// <summary>Work package this row ran under — only on unbooked rows;
    /// booked rows inherit the package from their booking. Drives which
    /// package-rate / matrix cell prices the row.</summary>
    public Guid? WorkPackageId { get; set; }
    public WorkPackage? WorkPackage { get; set; }

    public DateOnly Date { get; set; }
    public TimeEntryCategory Category { get; set; } = TimeEntryCategory.Work;
    /// <summary>Hours as reported by the resource — never overwritten.</summary>
    public decimal ActualHours { get; set; }
    /// <summary>Customer-billing mode for this row (contract model vs. plain
    /// hours). Only affects the invoice specification — resource compensation
    /// always follows the compensation agreement.</summary>
    public EntryBilling Billing { get; set; } = EntryBilling.Contract;
    public string? Description { get; set; }

    // Frozen at timesheet approval (null while draft):
    public decimal? PayableHours { get; set; }
    public decimal? BillableHours { get; set; }
    /// <summary>Share of the day's PAYABLE minimum-hours adjustment on this
    /// entry (§5 step 4) — the agreement's minimum.</summary>
    public decimal? MinimumAdjustmentHours { get; set; }
    /// <summary>Share of the day's BILLABLE minimum-hours adjustment — the
    /// contract's minimum, kept apart from the payable one (§1.1).</summary>
    public decimal? BillableMinimumAdjustmentHours { get; set; }

    /// <summary>The invoice specification that billed (or covered, for
    /// PAD-covered / non-billed rows) this entry — set at generation, cleared
    /// when a draft is rebuilt or deleted. Prevents billing a row twice and
    /// drives the Timesheet → Invoiced leg.</summary>
    public Guid? InvoiceSpecificationId { get; set; }

    /// <summary>Set while the entry sits inside a submitted/approved timesheet;
    /// null = draft. Returned timesheets release their rows back to null.</summary>
    public Guid? TimesheetId { get; set; }
    public Timesheet? Timesheet { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}
