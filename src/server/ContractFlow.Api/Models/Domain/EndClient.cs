namespace ContractFlow.Api.Models.Domain;

/// <summary>
/// The final customer in a multi-layer chain (e.g. the grid owner) when the
/// invoicing <see cref="Customer"/> is an intermediary. Optional per contract.
/// </summary>
public class EndClient
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Notes { get; set; }

    public List<Contract> Contracts { get; set; } = [];
}
