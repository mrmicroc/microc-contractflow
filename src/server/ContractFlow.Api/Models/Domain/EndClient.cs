namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// The final customer in a multi-layer chain (e.g. the grid owner) when the
/// invoicing <see cref="Customer"/> is an intermediary. Optional per contract —
/// when no end client is set the customer IS the end client.
/// </summary>
public class EndClient
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>ISO 3166-1 alpha-2 country code — where the work happens.</summary>
    public string? Country { get; set; }
    public string? AddressLine { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Notes { get; set; }

    public List<Contract> Contracts { get; set; } = [];
}
