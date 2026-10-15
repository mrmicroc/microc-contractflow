using System.ComponentModel.DataAnnotations;
using ContractFlow.Api.Models.Domain;

namespace ContractFlow.Api.Models.Dto;

// ── Customers ───────────────────────────────────────────────────────────────

public record CustomerSummaryDto(
    Guid Id, string Name, string? ExternalReference, bool IsActive, int ContractCount);

public record CustomerDetailDto(
    Guid Id, string Name, string? ExternalReference, string? Notes, bool IsActive,
    List<ContractSummaryDto> Contracts);

public class CustomerRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Name { get; set; }
    [StringLength(100)]
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

// ── End clients ─────────────────────────────────────────────────────────────

public record EndClientDto(Guid Id, string Name, string? Notes);

public class EndClientRequest
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Name { get; set; }
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
    decimal? DayRate, decimal? HalfDayRate, decimal? HalfDayMaxHours,
    decimal? FixedPriceAmount, DateOnly? FixedPriceDate, string? FixedPriceNote,
    decimal PadHoursPerDay, decimal MinPadPerBookedDay, decimal CarryOverMaxPercent,
    int CancellationDeadlineBusinessDays, string HolidayCalendar,
    decimal? MinBillableHoursPerDay,
    string? TravelExpensePolicy, string? Indexation, string? Notes);

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
    public decimal? HalfDayRate { get; set; }
    public decimal? HalfDayMaxHours { get; set; }
    public decimal? FixedPriceAmount { get; set; }
    public DateOnly? FixedPriceDate { get; set; }
    public string? FixedPriceNote { get; set; }

    public decimal PadHoursPerDay { get; set; } = 8m;
    public decimal MinPadPerBookedDay { get; set; } = 1m;
    public decimal CarryOverMaxPercent { get; set; } = 20m;
    public int CancellationDeadlineBusinessDays { get; set; } = 4;
    [Required, StringLength(20)]
    public required string HolidayCalendar { get; set; }
    /// <summary>Minimum billable hours per attendance day (§5 step 4). Null = none.</summary>
    [Range(0, 24)]
    public decimal? MinBillableHoursPerDay { get; set; }
    public string? TravelExpensePolicy { get; set; }
    public string? Indexation { get; set; }
    public string? Notes { get; set; }
}

// ── Projects, locations, quantities ─────────────────────────────────────────

public record ProjectSummaryDto(
    Guid Id, Guid ContractId, string Name, string? Code, bool IsActive);

public record ProjectDetailDto(
    Guid Id, Guid ContractId, string ContractName, string CustomerName,
    string Name, string? Code, string? Description, bool IsActive,
    List<LocationDto> Locations, List<EstimatedQuantityDto> EstimatedQuantities);

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

public record LocationDto(Guid Id, Guid ProjectId, string Name, string? Address, string? Notes);

public class LocationRequest
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
