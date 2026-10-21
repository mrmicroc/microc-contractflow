namespace ContractFlow.Api.Models.Domain;

/// <summary>How work at a package prices relative to the contract.</summary>
public enum WorkPackagePricing
{
    /// <summary>Follows the contract's billing components (e.g. PAD).</summary>
    Contract,
    /// <summary>The location lies outside the contract's scope (e.g. outside a
    /// lump sum): work there is additional — billed by time at the package /
    /// contract rates even when PAD is enabled and available, bookings commit
    /// no PAD, travel time bills and travel expenses recharge.</summary>
    OutsideContract,
}

/// <summary>A named sub-assignment under a project — a site, region, depot or
/// any other part bookings and reporting can target. Carries versioned rate
/// overrides over the contract terms.</summary>
public class WorkPackage
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public required string Name { get; set; }
    public string? Notes { get; set; }

    /// <summary>Inside the contract's scope (default) or additional work.</summary>
    public WorkPackagePricing PricingMode { get; set; } = WorkPackagePricing.Contract;
    /// <summary>A confirmed mobilisation to this package counts as one "visit"
    /// against the contract's <see cref="ContractTerms.IncludedVisits"/> —
    /// within the limit travel time and travel costs are included; beyond it
    /// they bill.</summary>
    public bool CountsAsVisit { get; set; }

    /// <summary>Free-form typed information fields (address, access, contact…).</summary>
    public List<WorkPackageInfo> InfoItems { get; set; } = [];
    /// <summary>Versioned package-specific price overrides over the contract terms.</summary>
    public List<WorkPackageRate> Rates { get; set; } = [];
}
