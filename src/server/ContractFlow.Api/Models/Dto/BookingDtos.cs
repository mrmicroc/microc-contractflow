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
    /// <summary>Project split of a shared booked day (empty = single project).</summary>
    List<BookingShareDto>? Shares = null,
    /// <summary>Agreed start time (null = none agreed).</summary>
    TimeOnly? StartTime = null,
    /// <summary>When working time commences per the notice rule — the start
    /// in force at the notice deadline (or the terms default); later changes
    /// only ever bring it forward. Null on terms without a booked-day model.</summary>
    TimeOnly? EffectiveStartTime = null);

/// <summary>One project's part of a shared booked day; <c>ExpectedPad</c> =
/// its planned share of the commitment (null for resource callers).</summary>
public record BookingShareDto(Guid ProjectId, string ProjectName, decimal PlannedHours, decimal? ExpectedPad);

/// <summary>A planned project share in a booking request.</summary>
public class BookingShareRequest
{
    [Required]
    public Guid ProjectId { get; set; }
    [Range(0.25, 24)]
    public decimal PlannedHours { get; set; }
}

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
    /// <summary>Initial status — only pre-commitment statuses are allowed here.</summary>
    public BookingStatus Status { get; set; } = BookingStatus.Forecast;
    /// <summary>Shared booked day: the planned hours per project (all on the
    /// same contract, primary project included). Overrides PlannedHours with
    /// their sum. Empty / one entry = a single-project day.</summary>
    public List<BookingShareRequest> Shares { get; set; } = [];
    /// <summary>Agreed start time, local at the work location.</summary>
    public TimeOnly? StartTime { get; set; }
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
    public BookingStatus? Status { get; set; }
    public DateOnly? Date { get; set; }
    public Guid? ResourceId { get; set; }
    /// <summary>Null keeps the current split; empty clears it (pre-commitment only).</summary>
    public List<BookingShareRequest>? Shares { get; set; }
    /// <summary>Sets the agreed start time (recorded with its notice date).</summary>
    public TimeOnly? StartTime { get; set; }
    /// <summary>True withdraws the agreed start time.</summary>
    public bool ClearStartTime { get; set; }
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
    /// <summary>Shared booked day: actual hours per project id — splits the
    /// day's PAD by actual time. Omitted → the reported time that date.</summary>
    public Dictionary<Guid, decimal>? ActualHoursByProject { get; set; }
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
    DateTime CreatedUtc, DateTime? DecidedUtc, Guid? BookingId);

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
