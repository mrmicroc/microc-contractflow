namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// One free-form information field on a work package — the type names what it
/// is ("Address", "Access", "Contact", "Coordinates"…) and the description
/// carries the value ("1 Example Road", "Gate code 4521"). Replaces the rigid
/// single-address field so packages can carry whatever context matters.
/// </summary>
public class WorkPackageInfo
{
    public Guid Id { get; set; }
    public Guid WorkPackageId { get; set; }
    public WorkPackage WorkPackage { get; set; } = null!;

    /// <summary>Short type label, e.g. "Address" or "Access".</summary>
    public required string Type { get; set; }
    /// <summary>The free-text content of the field.</summary>
    public required string Description { get; set; }
}
