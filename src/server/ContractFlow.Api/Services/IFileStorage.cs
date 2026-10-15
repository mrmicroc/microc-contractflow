namespace ContractFlow.Api.Services;

/// <summary>
/// File-bytes store for receipts, booking evidence and other documents
/// (design §9). Local disk in dev, Azure Blob in production — callers only
/// ever see the opaque storage key kept on the <see cref="Document"/> row.
/// </summary>
public interface IFileStorage
{
    /// <summary>Persists the stream and returns the storage key to store.</summary>
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct);
    Task DeleteAsync(string storageKey, CancellationToken ct);
}
