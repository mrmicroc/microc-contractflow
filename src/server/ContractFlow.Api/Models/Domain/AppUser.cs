namespace ContractFlow.Api.Models.Domain;

public class AppUser
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    public string? Email { get; set; }
    public AppRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
