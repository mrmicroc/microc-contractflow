using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Models.Dto;

// ── Customers ───────────────────────────────────────────────────────────────

/// <summary>One organisation of the shared register — <c>ContractCount</c> =
/// contracts where it is the party, <c>EndClientContractCount</c> = contracts
/// an intermediary holds on its behalf.</summary>
public record CustomerSummaryDto(
    Guid Id, string Name, string? ExternalReference, string? VatNumber,
    string? Country, bool IsActive, int ContractCount, int EndClientContractCount = 0);

public record ContactPersonDto(string Name, string? Role, string? Email, string? Phone);

public record CustomerDetailDto(
    Guid Id, string Name, string? ExternalReference, string? VatNumber,
    string? AddressLine, string? PostalCode, string? City, string? Country,
    List<ContactPersonDto> Contacts, string? Notes, bool IsActive,
    List<ContractSummaryDto> Contracts,
    List<EndClientContractDto> EndClientContracts);

/// <summary>A contract where the organisation is the end client — named
/// with the intermediary that holds it.</summary>
public record EndClientContractDto(
    Guid Id, string Name, Guid CustomerId, string CustomerName,
    string? EndClientReference, DateOnly StartDate, DateOnly? EndDate, bool IsActive);

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

// ── Contracts & terms ───────────────────────────────────────────────────────

public record ContractSummaryDto(
    Guid Id, Guid CustomerId, string Name, string? ExternalReference,
    List<string> Components,
    DateOnly StartDate, DateOnly? EndDate, bool IsActive);

public record ContractDetailDto(
    Guid Id, Guid CustomerId, string CustomerName, Guid? EndClientId, string? EndClientName,
    string? EndClientReference,
    string Name, string? ExternalReference,
    List<string> Components,
    DateOnly StartDate, DateOnly? EndDate, bool IsActive,
    List<ContractTermsDto> TermsVersions, List<ProjectSummaryDto> Projects,
    List<AuthorisedRepresentativeDto> Representatives);

/// <summary>Authorised customer representative (see
/// <see cref="AuthorisedRepresentative"/>).</summary>
public record AuthorisedRepresentativeDto(
    [Required, StringLength(200, MinimumLength = 1)] string Name,
    [StringLength(320)] string? Email,
    [StringLength(50)] string? Phone,
    [StringLength(300)] string? Authority);

public class ContractRequest
{
    /// <summary>Authorised representatives — replace-all on update.</summary>
    public List<AuthorisedRepresentativeDto> Representatives { get; set; } = [];
    /// <summary>The contract party — who signs and pays.</summary>
    [Required]
    public Guid CustomerId { get; set; }
    /// <summary>End client from the same register; null (or the party
    /// itself) = direct contract.</summary>
    public Guid? EndClientId { get; set; }
    [StringLength(100)]
    public string? EndClientReference { get; set; }
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
    string Currency, TimeBilling TimeBilling, bool PadEnabled,
    decimal? PadRate, decimal? HourlyRate, decimal? SaturdayRate,
    decimal? SundayRate, decimal? HolidayRate, int OvertimeAfterHours,
    decimal OvertimePercent, decimal? MobilisationFee, decimal ExpenseMarkupPercent,
    decimal? DayRate, decimal? SaturdayDayRate, decimal? SundayDayRate,
    decimal? HolidayDayRate, decimal? HalfDayRate, decimal? HalfDayMaxHours,
    decimal PadHoursPerDay, decimal MinPadPerBookedDay, decimal CarryOverMaxPercent,
    int CarryOverMonths,
    int CancellationDeadlineBusinessDays, List<CancellationStepDto> CancellationSteps,
    string HolidayCalendar,
    decimal? MinBillableHoursPerDay,
    string? TravelExpensePolicy, string? Indexation, string? Notes,
    int? IncludedVisits = null,
    PadRulesDto? PadRules = null,
    List<ExpenseMarkupDto>? ExpenseMarkups = null);

/// <summary>One rung of the cancellation ladder — see
/// <see cref="CancellationStep"/>.</summary>
public record CancellationStepDto(
    int MinBusinessDaysBefore, decimal? ChargeHours, decimal? ChargeAmount,
    decimal? ChargeDayFraction = null, decimal? ChargePercent = null);

/// <summary>Creates a new terms version — server assigns Version = max + 1.</summary>
public partial class ContractTermsRequest
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
    /// <summary>Included visits over the contract (null = not limited).</summary>
    [Range(0, 100_000)]
    public int? IncludedVisits { get; set; }

    // ── Billing components (which models apply — may combine) ───────────────
    public TimeBilling TimeBilling { get; set; } = TimeBilling.Hourly;
    public bool PadEnabled { get; set; }
    public decimal? DayRate { get; set; }
    public decimal? SaturdayDayRate { get; set; }
    public decimal? SundayDayRate { get; set; }
    public decimal? HolidayDayRate { get; set; }
    public decimal? HalfDayRate { get; set; }
    public decimal? HalfDayMaxHours { get; set; }

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

/// <summary><c>Currency</c>: the contract currency in force today — filled
/// on the reporting picker (/api/projects/mine) so expense forms can default
/// to it; null elsewhere.</summary>
public record ProjectSummaryDto(
    Guid Id, Guid ContractId, string Name, string? Code, bool IsActive, string? Currency = null);

public record ProjectDetailDto(
    Guid Id, Guid ContractId, string ContractName, string CustomerName,
    string Name, string? Code, string? Description, bool IsActive,
    List<WorkPackageDto> WorkPackages, List<EstimatedQuantityDto> EstimatedQuantities,
    decimal? ContractedBaselinePad = null);

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
    /// <summary>The project's contracted total baseline in PAD (null = not tracked).</summary>
    [Range(0, 1_000_000)]
    public decimal? ContractedBaselinePad { get; set; }
}

public record WorkPackageDto(
    Guid Id, Guid ProjectId, string Name, string? Notes,
    List<WorkPackageInfoDto> InfoItems,
    WorkPackagePricing PricingMode = WorkPackagePricing.Contract, bool CountsAsVisit = false);

public record WorkPackageInfoDto(Guid Id, string Type, string Description);

/// <summary>Contract-scoped work-package row (GET /api/contracts/{id}/work-packages)
/// — the scope picker's options, labelled with their owning project.</summary>
public record ContractWorkPackageDto(Guid Id, string Name, Guid ProjectId, string ProjectName);

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
    public string? Notes { get; set; }
    /// <summary>Typed info fields — replace-all semantics on update.</summary>
    public List<WorkPackageInfoItem>? InfoItems { get; set; }
    /// <summary>Inside the contract scope (default) or additional work.</summary>
    public WorkPackagePricing PricingMode { get; set; } = WorkPackagePricing.Contract;
    /// <summary>Mobilisations here count against the included visits.</summary>
    public bool CountsAsVisit { get; set; }
}

public class WorkPackageInfoItem
{
    [Required, StringLength(60, MinimumLength = 1)]
    public required string Type { get; set; }
    [Required, StringLength(500, MinimumLength = 1)]
    public required string Description { get; set; }
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
