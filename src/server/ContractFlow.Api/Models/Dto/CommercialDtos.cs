using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Models.Dto;

// ── Customers ───────────────────────────────────────────────────────────────

public record CustomerSummaryDto(
    Guid Id, string Name, string? ExternalReference, string? VatNumber,
    string? Country, bool IsActive, int ContractCount);

public record ContactPersonDto(string Name, string? Role, string? Email, string? Phone);

public record CustomerDetailDto(
    Guid Id, string Name, string? ExternalReference, string? VatNumber,
    string? AddressLine, string? PostalCode, string? City, string? Country,
    List<ContactPersonDto> Contacts, string? Notes, bool IsActive,
    List<ContractSummaryDto> Contracts);

public class CustomerRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Name { get; set; }
    [StringLength(100)]
    public string? ExternalReference { get; set; }
    [StringLength(50)]
    public string? VatNumber { get; set; }
    [StringLength(200)]
    public string? AddressLine { get; set; }
    [StringLength(20)]
    public string? PostalCode { get; set; }
    [StringLength(100)]
    public string? City { get; set; }
    [StringLength(2)]
    public string? Country { get; set; }
    public List<ContactPersonDto> Contacts { get; set; } = [];
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

// ── End clients ─────────────────────────────────────────────────────────────

public record EndClientDto(
    Guid Id, string Name, string? Country, string? AddressLine,
    string? PostalCode, string? City, string? Notes);

public class EndClientRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Name { get; set; }
    [StringLength(2)]
    public string? Country { get; set; }
    [StringLength(200)]
    public string? AddressLine { get; set; }
    [StringLength(20)]
    public string? PostalCode { get; set; }
    [StringLength(100)]
    public string? City { get; set; }
    public string? Notes { get; set; }
}

// ── Contracts & terms ───────────────────────────────────────────────────────

public record ContractSummaryDto(
    Guid Id, Guid CustomerId, string Name, string? ExternalReference,
    List<string> Components,
    DateOnly StartDate, DateOnly? EndDate, bool IsActive);

public record ContractDetailDto(
    Guid Id, Guid CustomerId, string CustomerName, Guid? EndClientId, string? EndClientName,
    string Name, string? ExternalReference,
    List<string> Components,
    DateOnly StartDate, DateOnly? EndDate, bool IsActive,
    List<ContractTermsDto> TermsVersions, List<ProjectSummaryDto> Projects);

public class ContractRequest
{
    [Required]
    public Guid CustomerId { get; set; }
    public Guid? EndClientId { get; set; }
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Name { get; set; }
    [StringLength(100)]
    public string? ExternalReference { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public record ContractTermsDto(
    Guid Id, Guid ContractId, int Version, DateOnly EffectiveFrom, DateTime CreatedUtc,
    string Currency, TimeBilling TimeBilling, bool PadEnabled, bool FixedPriceEnabled,
    decimal? PadRate, decimal? HourlyRate, decimal? SaturdayRate,
    decimal? SundayRate, decimal? HolidayRate, int OvertimeAfterHours,
    decimal OvertimePercent, decimal? MobilisationFee, decimal ExpenseMarkupPercent,
    decimal? DayRate, decimal? SaturdayDayRate, decimal? SundayDayRate,
    decimal? HolidayDayRate, decimal? HalfDayRate, decimal? HalfDayMaxHours,
    decimal? FixedPriceAmount, DateOnly? FixedPriceDate, string? FixedPriceNote,
    decimal PadHoursPerDay, decimal MinPadPerBookedDay, decimal CarryOverMaxPercent,
    int CarryOverMonths,
    int CancellationDeadlineBusinessDays, List<CancellationStepDto> CancellationSteps,
    string HolidayCalendar,
    decimal? MinBillableHoursPerDay,
    string? TravelExpensePolicy, string? Indexation, string? Notes);

/// <summary>One rung of the cancellation ladder — see
/// <see cref="CancellationStep"/>.</summary>
public record CancellationStepDto(
    int MinBusinessDaysBefore, decimal? ChargeHours, decimal? ChargeAmount);

/// <summary>Creates a new terms version — server assigns Version = max + 1.</summary>
public class ContractTermsRequest
{
    [Required]
    public DateOnly EffectiveFrom { get; set; }
    [Required, StringLength(3, MinimumLength = 3)]
    public required string Currency { get; set; }
    public decimal? PadRate { get; set; }
    public decimal? HourlyRate { get; set; }
    public decimal? SaturdayRate { get; set; }
    public decimal? SundayRate { get; set; }
    public decimal? HolidayRate { get; set; }
    public int OvertimeAfterHours { get; set; } = 8;
    public decimal OvertimePercent { get; set; } = 150m;
    public decimal? MobilisationFee { get; set; }
    public decimal ExpenseMarkupPercent { get; set; }

    // ── Billing components (which models apply — may combine) ───────────────
    public TimeBilling TimeBilling { get; set; } = TimeBilling.Hourly;
    public bool PadEnabled { get; set; }
    public bool FixedPriceEnabled { get; set; }
    public decimal? DayRate { get; set; }
    public decimal? SaturdayDayRate { get; set; }
    public decimal? SundayDayRate { get; set; }
    public decimal? HolidayDayRate { get; set; }
    public decimal? HalfDayRate { get; set; }
    public decimal? HalfDayMaxHours { get; set; }
    public decimal? FixedPriceAmount { get; set; }
    public DateOnly? FixedPriceDate { get; set; }
    public string? FixedPriceNote { get; set; }

    public decimal PadHoursPerDay { get; set; } = 8m;
    public decimal MinPadPerBookedDay { get; set; } = 1m;
    public decimal CarryOverMaxPercent { get; set; } = 20m;
    /// <summary>How many following months a carried PAD may live (1 = only the
    /// next month). Default 1.</summary>
    public int CarryOverMonths { get; set; } = 1;
    public int CancellationDeadlineBusinessDays { get; set; } = 4;
    /// <summary>Cancellation ladder — when non-empty it replaces the binary
    /// deadline rule above.</summary>
    public List<CancellationStepDto> CancellationSteps { get; set; } = [];
    [Required, StringLength(20)]
    public required string HolidayCalendar { get; set; }
    /// <summary>Minimum billable hours per attendance day (§5 step 4). Null = none.</summary>
    [Range(0, 24)]
    public decimal? MinBillableHoursPerDay { get; set; }
    public string? TravelExpensePolicy { get; set; }
    public string? Indexation { get; set; }
    public string? Notes { get; set; }
}

// ── Projects, workPackages, quantities ─────────────────────────────────────────

public record ProjectSummaryDto(
    Guid Id, Guid ContractId, string Name, string? Code, bool IsActive);

public record ProjectDetailDto(
    Guid Id, Guid ContractId, string ContractName, string CustomerName,
    string Name, string? Code, string? Description, bool IsActive,
    List<WorkPackageDto> WorkPackages, List<EstimatedQuantityDto> EstimatedQuantities);

public class ProjectRequest
{
    [Required]
    public Guid ContractId { get; set; }
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Name { get; set; }
    [StringLength(50)]
    public string? Code { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public record WorkPackageDto(Guid Id, Guid ProjectId, string Name, string? Address, string? Notes);

/// <summary>One versioned package-rate override row (GET /api/work-packages/{id}/rates).
/// ResourceId set = resource×package matrix cell; null = package-wide default.</summary>
public record WorkPackageRateDto(
    Guid Id, Guid WorkPackageId, string EffectiveFrom,
    Guid? ResourceId, string? ResourceName, bool NeedsReview,
    decimal? HourlyRate, decimal? SaturdayRate, decimal? SundayRate, decimal? HolidayRate,
    decimal? DayRate, decimal? SaturdayDayRate, decimal? SundayDayRate, decimal? HolidayDayRate,
    decimal? HalfDayRate, decimal? HalfDayMaxHours, decimal? MobilisationFee, string? Notes);

public class WorkPackageRateRequest
{
    /// <summary>Optional project member the rate applies to — null = the
    /// package-wide default row.</summary>
    public Guid? ResourceId { get; set; }
    [Required]
    public DateOnly EffectiveFrom { get; set; }
    public decimal? HourlyRate { get; set; }
    public decimal? SaturdayRate { get; set; }
    public decimal? SundayRate { get; set; }
    public decimal? HolidayRate { get; set; }
    public decimal? DayRate { get; set; }
    public decimal? SaturdayDayRate { get; set; }
    public decimal? SundayDayRate { get; set; }
    public decimal? HolidayDayRate { get; set; }
    public decimal? HalfDayRate { get; set; }
    public decimal? HalfDayMaxHours { get; set; }
    public decimal? MobilisationFee { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A resource allowed to self-report on the project (access list row).</summary>
public record ProjectAccessDto(Guid ResourceId, string ResourceName, DateTime CreatedUtc);

public class ProjectAccessRequest
{
    [Required]
    public Guid ResourceId { get; set; }
}

/// <summary>A resource staffed on a project (GET /api/projects/{id}/resources).
/// PendingReviews counts inherited rate rows not yet reviewed per package.</summary>
public record ProjectResourceDto(
    Guid ResourceId, string ResourceName, string ResourceType,
    DateTime AddedUtc, int PendingReviews);

public class ProjectResourceRequest
{
    [Required]
    public Guid ResourceId { get; set; }
}

public class WorkPackageRequest
{
    [Required]
    public Guid ProjectId { get; set; }
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
}

public record EstimatedQuantityDto(
    Guid Id, Guid ProjectId, string Label, decimal Quantity, string Unit, string? Notes);

public class EstimatedQuantityRequest
{
    [Required]
    public Guid ProjectId { get; set; }
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Label { get; set; }
    public decimal Quantity { get; set; }
    [Required, StringLength(30, MinimumLength = 1)]
    public required string Unit { get; set; }
    public string? Notes { get; set; }
}
