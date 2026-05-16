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

/// <summary>卫星列表项：含测试阶段标签（来自 <c>test_batch_cache</c> / 卫星测试流程规划 <c>POST /api/testplan/teststages</c>）。</summary>
public sealed record SatelliteListItem(
    string TasookNo,
    string? TasookName,
    string SatelliteNo,
    string SatelliteName,
    string? DbStage,
    MongoConnectionInfo? MongoInfo,
    string? SourceVersion,
    DateTimeOffset LastSyncedAt,
    int CachedParameterCount,
    int CachedCommandCount,
    IReadOnlyList<string> DevelopmentPhases);

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

    Task DeleteAsync(Guid sourceId, CancellationToken cancellationToken);
}

public interface IAssetCacheRepository
{
    Task UpsertSatelliteAsync(SatelliteCache satellite, CancellationToken cancellationToken);

    Task UpsertParametersAsync(IReadOnlyCollection<ParamCache> parameters, CancellationToken cancellationToken);

    Task UpsertCommandsAsync(
        string tasookNo,
        string satelliteNo,
        IReadOnlyCollection<CommandCache> commands,
        CancellationToken cancellationToken);

    Task UpsertTestBatchesAsync(IReadOnlyCollection<TestBatchCache> testBatches, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<SatelliteCache>> GetSatellitesAsync(CancellationToken cancellationToken);

    Task<SatelliteCache?> GetSatelliteAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ParamCache>> GetParametersAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TestBatchCache>> GetTestBatchesAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken);

    /// <summary>按星聚合测试阶段名称（<c>test_batch_cache.scenario</c>，去重、按最近阶段时间倒序）。</summary>
    Task<IReadOnlyDictionary<(string TasookNo, string SatelliteNo), IReadOnlyList<string>>> GetDevelopmentPhaseLabelsBySatelliteAsync(
        CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 海量数据接口。仅服务于 6.1.2 同步流程的 Step 1 / 2 / 2b / 4。
/// Step 3「测试阶段」由 <see cref="ISatelliteAssetProvider"/>（卫星测试流程规划服务）提供。
/// </summary>
public interface IMassDataAssetProvider
{
    Task<IReadOnlyCollection<SatelliteCache>> GetSatellitesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ParamCache>> GetParametersAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CommandCache>> GetCommandsAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        CancellationToken cancellationToken);

    Task<MongoConnectionInfo?> GetMongoInfoAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        CancellationToken cancellationToken);
}

/// <summary>
/// 卫星测试流程规划服务适配。仅服务于 Step 3「测试阶段」拉取（<c>POST /api/testplan/teststages</c>）。
/// </summary>
public interface ISatelliteAssetProvider
{
    Task<IReadOnlyCollection<TestBatchCache>> GetTestPhasesAsync(
        string tasookNo,
        string satelliteNo,
        string? dbStage,
        CancellationToken cancellationToken);
}
