using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Models.Dto;

public record BookingDto(
    Guid Id, Guid ProjectId, string ProjectName, string CustomerName,
    Guid ResourceId, string ResourceName, Guid? WorkPackageId, string? WorkPackageName,
    DateOnly Date, decimal PlannedHours, string? ActivityRef, string? WorkDescription,
    string? CustomerContact, string? EvidenceRef, BookingStatus Status,
    decimal? ExpectedPad, DateTime? AcknowledgedUtc,
    /// <summary>Resolved cancellation charge (hours / fixed amount) — set when
    /// a non-PAD booking is cancelled under a charging step.</summary>
    decimal? CancellationChargeHours, decimal? CancellationChargeAmount,
    Guid? ReplacesBookingId, Guid? RescheduledToBookingId, string[] Warnings,
    string? InstructedBy = null, DateTime? InstructedAtUtc = null,
    decimal? CancellationChargeDayFraction = null);

public class BookingRequest
{
    [Required]
    public Guid ProjectId { get; set; }
    [Required]
    public Guid ResourceId { get; set; }
    public Guid? WorkPackageId { get; set; }
    [Required]
    public DateOnly Date { get; set; }
    [Range(0.25, 24)]
    public decimal PlannedHours { get; set; } = 8m;
    [StringLength(100)]
    public string? ActivityRef { get; set; }
    public string? WorkDescription { get; set; }
    [StringLength(200)]
    public string? CustomerContact { get; set; }
    [StringLength(200)]
    public string? EvidenceRef { get; set; }
    /// <summary>Who at the customer requested/instructed the day, and when.</summary>
    [StringLength(200)]
    public string? InstructedBy { get; set; }
    public DateTime? InstructedAtUtc { get; set; }
    /// <summary>Initial status — only pre-commitment statuses are allowed here.</summary>
    public BookingStatus Status { get; set; } = BookingStatus.Forecast;
}

public class BookingUpdateRequest
{
    public Guid? WorkPackageId { get; set; }
    /// <summary>Null keeps the current planned hours.</summary>
    [Range(0.25, 24)]
    public decimal? PlannedHours { get; set; }
    [StringLength(100)]
    public string? ActivityRef { get; set; }
    public string? WorkDescription { get; set; }
    [StringLength(200)]
    public string? CustomerContact { get; set; }
    [StringLength(200)]
    public string? EvidenceRef { get; set; }
    /// <summary>Who at the customer requested/instructed the day, and when.</summary>
    [StringLength(200)]
    public string? InstructedBy { get; set; }
    public DateTime? InstructedAtUtc { get; set; }
    public BookingStatus? Status { get; set; }
    public DateOnly? Date { get; set; }
    public Guid? ResourceId { get; set; }
}

public class RescheduleRequest
{
    [Required]
    public DateOnly Date { get; set; }
    [Range(0.25, 24)]
    public decimal? PlannedHours { get; set; }
    public string? Note { get; set; }
}

/// <summary>Completion body (optional). <see cref="ActualHours"/> beyond the
/// planned commitment triggers the continuation allocation of §4.3.</summary>
public class CompleteRequest
{
    [Range(0.25, 24)]
    public decimal? ActualHours { get; set; }
}

public record PadTransactionDto(
    Guid Id, PadTransactionType Type, decimal Amount, Guid? BookingId,
    string? Note, DateTime CreatedUtc);

public record PadDashboardDto(
    Guid ProjectId, string ProjectName, int Year, int Month,
    decimal Baseline, decimal CarryOverIn, decimal AdditionalApproved,
    decimal CommittedOpen, decimal Utilised, decimal Released, decimal Expired,
    decimal CarryOverOut, decimal BorrowedFromThisMonth, decimal Adjustments,
    decimal TotalAvailable, decimal Free,
    bool Closed, DateTime? ClosedUtc,
    List<PadTransactionDto> Transactions);

public class BaselineRequest
{
    /// <summary>New baseline total for the month (set semantics — replaces, doesn't add).</summary>
    [Range(0, 10000)]
    public decimal Amount { get; set; }
    public string? Note { get; set; }
}

public record CloseMonthResultDto(
    decimal Baseline, decimal Utilised, decimal ReleasedOpenCommitments,
    decimal CarryOverOut, decimal Expired, decimal CarryOverInNextMonth,
    int NextYear, int NextMonth);

/// <summary>Month-close request — <see cref="Force"/> overrides blocking
/// checklist items and requires a documented <see cref="Reason"/>.</summary>
public class CloseMonthRequest
{
    public bool Force { get; set; }
    [StringLength(500)]
    public string? Reason { get; set; }
}

/// <summary>One pre-close checklist line (design §6.4). Codes:
/// unresolvedBookings, planningLeftovers, unsubmittedTime,
/// unapprovedTimesheets, unapprovedExpenses, travelNeedingReview,
/// pendingAdditionalPad, negativeFree.</summary>
public record CloseCheckItemDto(string Code, int Count, bool Blocking);

/// <summary>One step of the carry-over / expiry calculation, in order.</summary>
public record CloseStepDto(string Code, decimal Amount, string Explanation);

/// <summary>Dry run of a month close — nothing is written. Ordering errors
/// (previousMonthOpen / nextMonthClosed / monthNotEnded / alreadyClosed)
/// can't be forced; blocking checklist items can (admin + reason).</summary>
public record ClosePreviewDto(
    int Year, int Month, bool CanClose, string[] OrderingErrors,
    List<CloseCheckItemDto> Checklist, List<CloseStepDto> Steps,
    CloseMonthResultDto Result);

public record AdditionalPadRequestDto(
    Guid Id, Guid ProjectId, int Year, int Month, decimal Quantity, decimal? Rate,
    string? Reason, string? Reference, AdditionalPadStatus Status,
    DateTime CreatedUtc, DateTime? DecidedUtc, Guid? BookingId,
    string? ApprovedByRepresentative = null);

/// <summary>Optional approval evidence — the customer's authorised
/// representative who approved the Additional PAD and its remuneration.</summary>
public class AdditionalPadApproveRequest
{
    [StringLength(200)]
    public string? ApprovedByRepresentative { get; set; }
}

public class AdditionalPadRequestCreate
{
    [Required]
    public Guid ProjectId { get; set; }
    [Range(1, 2100)]
    public int Year { get; set; }
    [Range(1, 12)]
    public int Month { get; set; }
    [Range(0.01, 10000)]
    public decimal Quantity { get; set; }
    public decimal? Rate { get; set; }
    public string? Reason { get; set; }
    [StringLength(200)]
    public string? Reference { get; set; }
    /// <summary>Optional prepared booking that waits on this request.</summary>
    public Guid? BookingId { get; set; }
}
