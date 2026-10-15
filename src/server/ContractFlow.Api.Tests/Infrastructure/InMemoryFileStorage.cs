using System.Collections.Concurrent;
using ContractFlow.Api.Services;

namespace ContractFlow.Api.Tests.Infrastructure;

/// <summary>Test stand-in for <see cref="IFileStorage"/> so upload tests never
/// touch the disk.</summary>
public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public int Count => _files.Count;
    public bool Contains(string key) => _files.ContainsKey(key);

    public async Task<string> SaveAsync(Stream content, string fileName, CancellationToken ct)
    {
        var key = $"{Guid.NewGuid():N}-{Path.GetFileName(fileName)}";
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _files[key] = ms.ToArray();
        return key;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream(_files[storageKey]));

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        _files.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }
}
