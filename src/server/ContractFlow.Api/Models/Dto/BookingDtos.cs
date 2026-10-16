using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Models.Dto;

public record BookingDto(
    Guid Id, Guid ProjectId, string ProjectName, string CustomerName,
    Guid ResourceId, string ResourceName, Guid? LocationId, string? LocationName,
    DateOnly Date, decimal PlannedHours, string? ActivityRef, string? WorkDescription,
    string? CustomerContact, string? EvidenceRef, BookingStatus Status,
    decimal? ExpectedPad, DateTime? AcknowledgedUtc,
    /// <summary>Resolved cancellation charge (hours / fixed amount) — set when
    /// a non-PAD booking is cancelled under a charging step.</summary>
    decimal? CancellationChargeHours, decimal? CancellationChargeAmount,
    Guid? ReplacesBookingId, Guid? RescheduledToBookingId, string[] Warnings);

public class BookingRequest
{
    [Required]
    public Guid ProjectId { get; set; }
    [Required]
    public Guid ResourceId { get; set; }
    public Guid? LocationId { get; set; }
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
}

public class BookingUpdateRequest
{
    public Guid? LocationId { get; set; }
    [Range(0.25, 24)]
    public decimal PlannedHours { get; set; } = 8m;
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
}

public class RescheduleRequest
{
    [Required]
    public DateOnly Date { get; set; }
    [Range(0.25, 24)]
    public decimal? PlannedHours { get; set; }
    public string? Note { get; set; }
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

public record AdditionalPadRequestDto(
    Guid Id, Guid ProjectId, int Year, int Month, decimal Quantity, decimal? Rate,
    string? Reason, string? Reference, AdditionalPadStatus Status,
    DateTime CreatedUtc, DateTime? DecidedUtc);

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
}
