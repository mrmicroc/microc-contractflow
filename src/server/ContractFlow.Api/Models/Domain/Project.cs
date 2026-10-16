namespace ContractFlow.Api.Models.Domain;

public class Project
{
    public Guid Id { get; set; }
    public Guid ContractId { get; set; }
    public Contract Contract { get; set; } = null!;

    public required string Name { get; set; }
    /// <summary>Short project code used on bookings, timesheets and invoices.</summary>
    public string? Code { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public List<WorkPackage> WorkPackages { get; set; } = [];
    public List<EstimatedQuantity> EstimatedQuantities { get; set; } = [];
}
