namespace ContractFlow.Api.Models.Domain;

/// <summary>Append-only PAD ledger entry types (design §4.1).</summary>
public enum PadTransactionType
{
    /// <summary>Baseline delta — the month's baseline is the sum of its
    /// BaselineSet rows. Amending the baseline appends a (possibly negative)
    /// delta row noting the new total, so the ledger stays append-only.</summary>
    BaselineSet,
    CarryOverIn,
    Commit,
    Release,
    Utilise,
    AdditionalApproved,
    AutoAdditional,
    BorrowNextMonth,
    CarryOverOut,
    Expire,
    Adjustment,
}

/// <summary>
/// One entry in the append-only PAD ledger for a project-month. The monthly
/// account is always derived as sums over these rows — never updated in place
/// (design §4). Amount is positive for real flows; <see cref="PadTransactionType.BaselineSet"/>
/// and <see cref="PadTransactionType.Adjustment"/> may carry signed corrections.
/// </summary>
public class PadTransaction
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public int Year { get; set; }
    public int Month { get; set; }
    public PadTransactionType Type { get; set; }
    public decimal Amount { get; set; }

    /// <summary>The booking behind Commit/Release/Utilise rows, when any.</summary>
    public Guid? BookingId { get; set; }
    public Booking? Booking { get; set; }

    /// <summary>For CarryOverIn/CarryOverOut rows: the month the PAD
    /// originated as baseline — its age decides when it expires
    /// (ContractTerms.CarryOverMonths). Null on older rows; treated as
    /// "the month before the row's own month".</summary>
    public int? OriginYear { get; set; }
    public int? OriginMonth { get; set; }
    /// <summary>The request behind AdditionalApproved rows, when any.</summary>
    public Guid? AdditionalPadRequestId { get; set; }
    public string? Note { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}
