using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Models.Dto;

public record ResourceSummaryDto(
    Guid Id, string DisplayName, ResourceType Type, bool IsActive, Guid? UserId,
    /// <summary>True when any competency record is due or expired.</summary>
    bool CompetencyWarning);

/// <summary>One competency record — Status computed server-side per today:
/// "unbounded" | "valid" | "due" | "expired".</summary>
public record CompetencyDto(
    Guid Id, string Label, DateOnly? ExpiryDate, int? ReminderDays, string Status);

public record ResourceDetailDto(
    Guid Id, string DisplayName, ResourceType Type, string? Email, string? Phone,
    List<CompetencyDto> Competencies, bool IsActive, Guid? UserId,
    List<ResourceLeaveDto> Leave);

public class ResourceRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string DisplayName { get; set; }
    public ResourceType Type { get; set; } = ResourceType.Subcontractor;
    [EmailAddress, StringLength(320)]
    public string? Email { get; set; }
    [StringLength(50)]
    public string? Phone { get; set; }
    public Guid? UserId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CompetencyRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Label { get; set; }
    /// <summary>Null = never expires.</summary>
    public DateOnly? ExpiryDate { get; set; }
    /// <summary>Days before expiry the record starts flagging for renewal.</summary>
    [Range(0, 3650)]
    public int? ReminderDays { get; set; }
}

public record ResourceLeaveDto(
    Guid Id, Guid ResourceId, DateOnly From, DateOnly To, LeaveType Type, string? Notes);

public class ResourceLeaveRequest
{
    [Required]
    public DateOnly From { get; set; }
    [Required]
    public DateOnly To { get; set; }
    public LeaveType Type { get; set; } = LeaveType.Vacation;
    public string? Notes { get; set; }
}

public record CompensationAgreementDto(
    Guid Id, Guid ResourceId, Guid? ContractId, string? ContractName,
    Guid? WorkPackageId, string? WorkPackageName, string? WorkPackageProjectName,
    int Version, DateOnly EffectiveFrom, DateTime CreatedUtc,
    string Currency, decimal? HourlyRate, decimal? SaturdayRate, decimal? SundayRate,
    decimal? HolidayRate, int OvertimeAfterHours, decimal OvertimePercent,
    decimal? TravelTimePercent, decimal? MileageRate, decimal? MinPayableHoursPerDay,
    PerDiemPolicy PerDiem, decimal? PerDiemAmount, string? ExpensePolicy, string? Notes);

/// <summary>Creates a new agreement version — server assigns Version = max + 1
/// within the same resource + (contract, work-package) scope.</summary>
public class CompensationAgreementRequest
{
    /// <summary>Null = the resource's default agreement across all contracts.</summary>
    public Guid? ContractId { get; set; }
    /// <summary>Optional — scopes the agreement to one work package. When set,
    /// <see cref="ContractId"/> must be the package's owning contract.</summary>
    public Guid? WorkPackageId { get; set; }
    [Required]
    public DateOnly EffectiveFrom { get; set; }
    [Required, StringLength(3, MinimumLength = 3)]
    public required string Currency { get; set; }
    public decimal? HourlyRate { get; set; }
    public decimal? SaturdayRate { get; set; }
    public decimal? SundayRate { get; set; }
    public decimal? HolidayRate { get; set; }
    public int OvertimeAfterHours { get; set; } = 8;
    public decimal OvertimePercent { get; set; } = 150m;
    public decimal? TravelTimePercent { get; set; }
    public decimal? MileageRate { get; set; }
    /// <summary>Minimum payable hours per attendance day (§5). Null = none.</summary>
    [Range(0, 24)]
    public decimal? MinPayableHoursPerDay { get; set; }
    public PerDiemPolicy PerDiem { get; set; } = PerDiemPolicy.None;
    public decimal? PerDiemAmount { get; set; }
    public string? ExpensePolicy { get; set; }
    public string? Notes { get; set; }
}
