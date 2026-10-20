namespace ContractFlow.Api.Models.Domain;

public enum StatementStatus
{
    Draft,
    Final,
    Paid,
}

/// <summary>
/// What MICROC owes one resource for one month — the payable basis built from
/// their approved timesheets at the compensation agreement in force on each
/// day (ledger 3 of §1.1). The resource sees this; nothing customer-side
/// (billable hours, margins) leaks into it.
/// </summary>
public class SubcontractorStatement
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public int Year { get; set; }
    public int Month { get; set; }

    public StatementStatus Status { get; set; } = StatementStatus.Draft;
    public required string Currency { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }

    public List<StatementLine> Lines { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
    public DateTime? PaidUtc { get; set; }
    public Guid? PaidByUserId { get; set; }
    /// <summary>JSON string array of generation warnings (rows in another
    /// currency than the statement are left out, never silently summed).</summary>
    public string? WarningsJson { get; set; }
}

public enum StatementLineKind
{
    /// <summary>Payable hours for one attendance day (incl. min-adjustment).</summary>
    Hours,
    /// <summary>Reimbursable expense at cost.</summary>
    Expense,
    /// <summary>Per diem / allowance claim.</summary>
    Allowance,
    /// <summary>Own-car mileage at the agreement's per-km rate.</summary>
    Mileage,
    /// <summary>Paid travel time at the agreement's percent of hourly rate.</summary>
    TravelTime,
    Manual,
}

public class StatementLine
{
    public Guid Id { get; set; }
    public Guid StatementId { get; set; }
    public SubcontractorStatement Statement { get; set; } = null!;

    public StatementLineKind Kind { get; set; }
    public DateOnly? Date { get; set; }
    public required string Description { get; set; }
    public string? Explanation { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public Guid? SourceId { get; set; }
}
