using System.Collections.Concurrent;
using SatelliteData.Application.Algorithms;

namespace SatelliteData.Infrastructure.Storage;

public sealed class InMemoryObjectStorageService : IObjectStorageService
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public Task PutAsync(string bucket, string objectKey, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var key = $"{bucket}/{objectKey}";
        _blobs[key] = data.ToArray();
        return Task.CompletedTask;
    }
}
