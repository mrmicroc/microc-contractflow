namespace ContractFlow.Api.Models.Domain;

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
}

/// <summary>
/// One commercially relevant change (design §3 Cross-cutting, §9.3): who, when,
/// which row, and the old → new value per field. Written automatically by
/// <see cref="Data.AppDbContext"/> on save for a whitelist of entity types —
/// never edited afterwards.
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; }
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Acting user; null for system writes (seed, startup).</summary>
    public Guid? UserId { get; set; }
    /// <summary>Display name at the time of the change — survives renames.</summary>
    public string? UserDisplayName { get; set; }
    /// <summary>CLR entity name, e.g. "ContractTerms".</summary>
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public AuditAction Action { get; set; }
    /// <summary>JSON array of { field, old, new } — values formatted invariant.</summary>
    public required string Changes { get; set; }
    public string? Reason { get; set; }
}

/// <summary>A single field change inside <see cref="AuditEntry.Changes"/>.</summary>
public record AuditChange(string Field, string? Old, string? New);
