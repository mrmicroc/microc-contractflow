namespace ContractFlow.Api.Models.Domain;

public enum LeaveType
{
    Vacation,
    Leave,
    Unavailable,
}

/// <summary>
/// A period when a resource cannot be booked — feeds conflict warnings in the
/// planning calendar (design §7.1).
/// </summary>
public class ResourceLeave
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;

    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public LeaveType Type { get; set; }
    public string? Notes { get; set; }
}
