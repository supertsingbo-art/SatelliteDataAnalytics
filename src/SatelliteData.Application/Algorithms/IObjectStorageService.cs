namespace SatelliteData.Application.Algorithms;

public interface IObjectStorageService
{
    Task PutAsync(string bucket, string objectKey, ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}
