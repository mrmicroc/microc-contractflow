using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Models.Dto;

public record TimeEntryDto(
    Guid Id, Guid ResourceId, string ResourceName, Guid ProjectId, string ProjectName,
    Guid? BookingId, DateOnly Date, TimeEntryCategory Category, decimal ActualHours,
    EntryBilling Billing, string? Description, decimal? PayableHours, decimal? BillableHours,
    decimal? MinimumAdjustmentHours, Guid? TimesheetId, TimesheetStatus? TimesheetStatus);

public class TimeEntryRequest
{
    /// <summary>Admin/PM may report on behalf of a resource; a Resource user is
    /// always bound to their own linked resource.</summary>
    public Guid? ResourceId { get; set; }
    [Required]
    public Guid ProjectId { get; set; }
    public Guid? BookingId { get; set; }
    [Required]
    public DateOnly Date { get; set; }
    public TimeEntryCategory Category { get; set; } = TimeEntryCategory.Work;
    [Range(0.25, 24)]
    public decimal ActualHours { get; set; }
    /// <summary>'contract' = billed per the terms' components; 'hours' = forced
    /// hourly billing — for self-reported ad-hoc work.</summary>
    public EntryBilling Billing { get; set; } = EntryBilling.Contract;
    [StringLength(500)]
    public string? Description { get; set; }
}

public record TravelRecordDto(
    Guid Id, Guid ResourceId, string ResourceName, Guid? ProjectId, string? ProjectName,
    Guid? BookingId, DateOnly Date, string? FromPlace, string? ToPlace, TravelMode Mode,
    decimal? DistanceKm, decimal? DurationHours, Guid? MobilisationPeriodId,
    string? Notes, Guid? TimesheetId);

public class TravelRecordRequest
{
    public Guid? ResourceId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? BookingId { get; set; }
    [Required]
    public DateOnly Date { get; set; }
    [StringLength(200)]
    public string? FromPlace { get; set; }
    [StringLength(200)]
    public string? ToPlace { get; set; }
    public TravelMode Mode { get; set; } = TravelMode.Car;
    [Range(0, 5000)]
    public decimal? DistanceKm { get; set; }
    [Range(0, 24)]
    public decimal? DurationHours { get; set; }
    [StringLength(500)]
    public string? Notes { get; set; }
}

public record ExpenseDto(
    Guid Id, Guid ResourceId, string ResourceName, Guid? ProjectId, string? ProjectName,
    Guid? BookingId, DateOnly Date, ExpenseCategory Category, decimal Amount,
    string Currency, string? Description, string? ReceiptRef,
    bool ReimbursableToResource, bool RechargeableToCustomer, bool IncludedInRate,
    Guid? TimesheetId);

public class ExpenseRequest
{
    public Guid? ResourceId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? BookingId { get; set; }
    [Required]
    public DateOnly Date { get; set; }
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;
    [Range(0.01, 1_000_000)]
    public decimal Amount { get; set; }
    [Required, StringLength(3, MinimumLength = 3)]
    public required string Currency { get; set; }
    [StringLength(500)]
    public string? Description { get; set; }
    [StringLength(300)]
    public string? ReceiptRef { get; set; }
    public bool ReimbursableToResource { get; set; }
    public bool RechargeableToCustomer { get; set; }
    public bool IncludedInRate { get; set; }
}

public record AllowanceClaimDto(
    Guid Id, Guid ResourceId, string ResourceName, Guid? ProjectId, string? ProjectName,
    DateOnly Date, decimal Amount, string Currency, string? Notes, Guid? TimesheetId);

public class AllowanceClaimRequest
{
    public Guid? ResourceId { get; set; }
    public Guid? ProjectId { get; set; }
    [Required]
    public DateOnly Date { get; set; }
    [Range(0.01, 100_000)]
    public decimal Amount { get; set; }
    [Required, StringLength(3, MinimumLength = 3)]
    public required string Currency { get; set; }
    [StringLength(300)]
    public string? Notes { get; set; }
}

public record MobilisationPeriodDto(
    Guid Id, Guid ProjectId, string ProjectName, Guid ResourceId, string ResourceName,
    DateOnly StartDate, DateOnly EndDate, DateTime ConfirmedUtc,
    Guid? WorkPackageId, string? WorkPackageName);

public record MobilisationSuggestionDto(
    Guid ProjectId, string ProjectName, DateOnly StartDate, DateOnly EndDate);

public class MobilisationConfirmRequest
{
    [Required]
    public Guid ProjectId { get; set; }
    [Required]
    public Guid ResourceId { get; set; }
    [Required]
    public DateOnly StartDate { get; set; }
    [Required]
    public DateOnly EndDate { get; set; }
    /// <summary>Optional work package the period mobilised to — decides which
    /// package-rate override supplies the mobilisation fee.</summary>
    public Guid? WorkPackageId { get; set; }
}

/// <summary>Sets/clears the work package a confirmed period mobilised to.</summary>
public class MobilisationWorkPackageRequest
{
    public Guid? WorkPackageId { get; set; }
}

public class MobilisationSplitRequest
{
    /// <summary>First day of the second half — the original keeps [start, day−1].</summary>
    [Required]
    public DateOnly FromDate { get; set; }
}

public class MobilisationMergeRequest
{
    [Required]
    public Guid OtherId { get; set; }
}

public record TimesheetDto(
    Guid Id, Guid ResourceId, string ResourceName, int Year, int IsoWeek,
    TimesheetStatus Status, DateTime? SubmittedUtc, DateTime? ReviewedUtc,
    string? ReviewerNote, int EntryCount, int TravelCount, int ExpenseCount,
    int AllowanceCount, decimal TotalActualHours);

public record TimesheetDetailDto(
    TimesheetDto Sheet,
    IReadOnlyList<TimeEntryDto> Entries,
    IReadOnlyList<TravelRecordDto> Travel,
    IReadOnlyList<ExpenseDto> Expenses,
    IReadOnlyList<AllowanceClaimDto> Allowances);

public class TimesheetSubmitRequest
{
    /// <summary>Admin/PM may submit on behalf of a resource; a Resource user is
    /// always bound to their own.</summary>
    public Guid? ResourceId { get; set; }
    [Required, Range(2020, 2100)]
    public int Year { get; set; }
    [Required, Range(1, 53)]
    public int IsoWeek { get; set; }
}

public class TimesheetReviewRequest
{
    [StringLength(500)]
    public string? Note { get; set; }
}
