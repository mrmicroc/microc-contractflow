namespace ContractFlow.Api.Models.Domain;

/// <summary>draft → customerReview → final → issued → paid; corrections go
/// through revisions/credit notes, never by editing an issued spec (§3).</summary>
public enum InvoiceStatus
{
    Draft,
    CustomerReview,
    Final,
    Issued,
    Paid,
}

/// <summary>
/// What MICROC may invoice the customer for one project in one month — the
/// billing basis built from approved timesheets at the contract terms in
/// force on each day (ledger 4 of §1.1, strictly separate from compensation).
/// </summary>
public class InvoiceSpecification
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }
    /// <summary>0 = the month's original spec; 1.. = supplements billing rows
    /// approved after an earlier revision left draft.</summary>
    public int Revision { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;
    public required string Currency { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public DateTime? IssuedUtc { get; set; }
    public Guid? IssuedByUserId { get; set; }
    public DateTime? PaidUtc { get; set; }
    public Guid? PaidByUserId { get; set; }
    /// <summary>JSON string array of generation warnings (currency mismatch,
    /// unreviewed rate cells, PAD without rate…) — rows behind a warning are
    /// left out of the total, never silently summed.</summary>
    public string? WarningsJson { get; set; }
}

public enum InvoiceLineKind
{
    /// <summary>Billable hours for one attendance day (incl. min-adjustment).</summary>
    Hours,
    /// <summary>Recharged expense incl. contract mark-up.</summary>
    Expense,
    /// <summary>Fixed customer fee per confirmed mobilisation period.</summary>
    Mobilisation,
    /// <summary>Utilised PAD × PAD rate (PAD-component contracts).</summary>
    Pad,
    /// <summary>One-off fixed price (lump-sum component) on its dated month.</summary>
    FixedPrice,
    /// <summary>Cancellation charge — hours or a fixed fee resolved by the
    /// terms' cancellation ladder (non-PAD contracts; PAD contracts charge via
    /// utilised PAD instead).</summary>
    Cancellation,
    /// <summary>Free-text/manual line (adjustments stay explicit).</summary>
    Manual,
}

public class InvoiceLine
{
    public Guid Id { get; set; }
    public Guid InvoiceSpecificationId { get; set; }
    public InvoiceSpecification Invoice { get; set; } = null!;

    public InvoiceLineKind Kind { get; set; }
    /// <summary>The service date the line stems from (null for period-level lines).</summary>
    public DateOnly? Date { get; set; }
    public required string Description { get; set; }
    /// <summary>How the amount was computed — shown to the reviewer (§5: every
    /// calculated line carries its explanation).</summary>
    public string? Explanation { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Source row, for drill-down and double-billing guards: Expense,
    /// MobilisationPeriod or cancelled Booking id (hours lines → null; their
    /// entries point back via TimeEntry.InvoiceSpecificationId).</summary>
    public Guid? SourceId { get; set; }
}
