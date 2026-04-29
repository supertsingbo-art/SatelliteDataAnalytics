using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

public sealed record SaveDataSourceConfigRequest(
    string SourceType,
    string SourceName,
    string EndpointUrl,
    string AuthType,
    string? AuthSecretRef,
    int TimeoutMs,
    bool Enabled,
    string Env);

public sealed record SetDataSourceStatusRequest(bool Enabled);

public sealed record AssetPageRequest(
    string? Keyword,
    int PageNo = 1,
    int PageSize = 50);

public sealed record PagedResult<T>(
    int PageNo,
    int PageSize,
    int Total,
    IReadOnlyCollection<T> Items);

public sealed record ConnectionTestResult(
    bool Success,
    string Message,
    int? ElapsedMs);

public interface IDataSourceConfigRepository
{
    Task<IReadOnlyCollection<DataSourceConfig>> GetAllAsync(CancellationToken cancellationToken);

    Task<DataSourceConfig?> GetByIdAsync(Guid sourceId, CancellationToken cancellationToken);

    Task<DataSourceConfig?> GetEnabledByTypeAsync(string sourceType, CancellationToken cancellationToken);

    Task SaveAsync(DataSourceConfig config, CancellationToken cancellationToken);

    Task DeleteAllAssetCacheAsync(CancellationToken cancellationToken);
}

public interface IAssetCacheRepository
{
    Task UpsertSatelliteAsync(SatelliteCache satellite, CancellationToken cancellationToken);

    Task UpsertParametersAsync(IReadOnlyCollection<ParamCache> parameters, CancellationToken cancellationToken);

    Task UpsertTestBatchesAsync(IReadOnlyCollection<TestBatchCache> testBatches, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SatelliteCache>> GetSatellitesAsync(CancellationToken cancellationToken);

    Task<SatelliteCache?> GetSatelliteAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ParamCache>> GetParametersAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TestBatchCache>> GetTestBatchesAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

public interface IAssetProvider
{
    Task<IReadOnlyCollection<SatelliteCache>> GetSatellitesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ParamCache>> GetParametersAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken);

    Task<MongoConnectionInfo?> GetMongoInfoAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TestBatchCache>> GetTestBatchesAsync(
        string tasookNo,
        string satelliteNo,
        DateTimeOffset? start,
        DateTimeOffset? end,
        CancellationToken cancellationToken);
}

public interface IMassDataAssetProvider : IAssetProvider;

public interface ISatelliteAssetProvider : IAssetProvider;
