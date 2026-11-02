namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// registered → matched/discrepancy → approved → paid. "Matched" means the
/// registered amounts agree with the statement within a rounding epsilon;
/// discrepancy keeps the computed diffs visible for review.
/// </summary>
public enum SupplierInvoiceStatus
{
    Registered,
    Matched,
    Discrepancy,
    Approved,
    Paid,
}

/// <summary>
/// An incoming invoice from a subcontractor (design §6.2: "supplier invoice
/// registered → matched against statement → diffs shown"). Matching compares
/// its lines per kind against the statement for the same period — never the
/// other way around: the statement is the authority, the invoice is checked
/// against it.
/// </summary>
public class SupplierInvoice
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;

    /// <summary>The subcontractor's own invoice number.</summary>
    public required string Number { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public required string Currency { get; set; }
    public decimal TotalAmount { get; set; }

    public SupplierInvoiceStatus Status { get; set; } = SupplierInvoiceStatus.Registered;
    /// <summary>The statement this invoice was matched against.</summary>
    public Guid? StatementId { get; set; }
    public SubcontractorStatement? Statement { get; set; }

    /// <summary>JSON snapshot of the per-kind diff at match time
    /// ([{kind, invoiced, statement, diff}]) — kept so a regenerated statement
    /// doesn't silently rewrite history.</summary>
    public string? MatchDiffJson { get; set; }
    /// <summary>Why a Discrepancy was approved anyway — required then.</summary>
    public string? ApprovalNote { get; set; }

    public List<SupplierInvoiceLine> Lines { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public DateTime? PaidUtc { get; set; }
}

/// <summary>One amount row on the supplier invoice — the same kind vocabulary
/// as <see cref="StatementLineKind"/> so matching can compare per kind.</summary>
public class SupplierInvoiceLine
{
    public Guid Id { get; set; }
    public Guid SupplierInvoiceId { get; set; }
    public SupplierInvoice Invoice { get; set; } = null!;

    public StatementLineKind Kind { get; set; }
    public required string Description { get; set; }
    public decimal Amount { get; set; }
}
