namespace ContractFlow.Api.Services;

/// <summary>Dev implementation of <see cref="IFileStorage"/>: files under
/// <c>App_Data/uploads</c> in the content root (or FileStorage:Root config).
/// Keys are <c>{guid}{ext}</c> so nothing attacker-chosen reaches the path.</summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalDiskFileStorage(string? configuredRoot, string contentRoot)
    {
        _root = !string.IsNullOrWhiteSpace(configuredRoot)
            ? configuredRoot
            : Path.Combine(contentRoot, "App_Data", "uploads");
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName);
        if (ext.Length > 10) ext = string.Empty;
        var key = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var path = Path.Combine(_root, key);
        await using var fs = File.Create(path);
        await content.CopyToAsync(fs, ct);
        return key;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        var path = Path.Combine(_root, Path.GetFileName(storageKey));
        Stream s = File.OpenRead(path);
        return Task.FromResult(s);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        var path = Path.Combine(_root, Path.GetFileName(storageKey));
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
