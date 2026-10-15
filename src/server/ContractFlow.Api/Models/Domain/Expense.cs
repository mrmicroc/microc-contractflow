namespace ContractFlow.Api.Models.Domain;

public enum ExpenseCategory
{
    Travel,
    Meal,
    Accommodation,
    Material,
    Equipment,
    Other,
}

/// <summary>
/// An outlay by the resource (design §6.2). The three flags are independent:
/// an expense can be reimbursable to the resource AND rechargeable to the
/// customer (with mark-up per the contract), or included in the rate (neither
/// flag — paid silently inside the hour rate). Receipt upload lands with blob
/// storage later — ReceiptRef holds the reference meanwhile.
/// </summary>
public class Expense
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid? BookingId { get; set; }
    public Booking? Booking { get; set; }

    public DateOnly Date { get; set; }
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;
    public decimal Amount { get; set; }
    /// <summary>ISO 4217 code of the outlay.</summary>
    public required string Currency { get; set; }
    public string? Description { get; set; }
    /// <summary>Receipt reference (file name/order no) until file upload lands.</summary>
    public string? ReceiptRef { get; set; }

    /// <summary>MICROC reimburses the resource.</summary>
    public bool ReimbursableToResource { get; set; }
    /// <summary>Recharged to the customer (with contract mark-up).</summary>
    public bool RechargeableToCustomer { get; set; }
    /// <summary>Covered inside the agreed rates — no separate payment either way.</summary>
    public bool IncludedInRate { get; set; }

    public Guid? TimesheetId { get; set; }
    public Timesheet? Timesheet { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}
