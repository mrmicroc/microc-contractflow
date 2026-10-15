namespace ContractFlow.Api.Models.Domain;

/// <summary>What a <see cref="Document"/> hangs on (design §3: contracts,
/// bookings, expenses, approvals… — the set grows as more surfaces need
/// attachments).</summary>
public enum DocumentOwnerType
{
    Expense,
    Booking,
}

/// <summary>
/// An uploaded file's metadata. The bytes live in IFileStorage
/// under <see cref="StorageKey"/> — local disk in dev, Azure Blob later —
/// so the row stays small and replaceable (versioned attachments come via new
/// rows, never by mutating this one).
/// </summary>
public class Document
{
    public Guid Id { get; set; }
    public DocumentOwnerType OwnerType { get; set; }
    public Guid OwnerId { get; set; }

    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string StorageKey { get; set; }
    public string? Label { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Guid? UploadedByUserId { get; set; }
}
