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
    int PageSize = 50,
    bool Unpaged = false,
    bool? EnabledOnly = null);

public sealed record SetSatelliteEnabledRequest(bool IsEnabled);

public static class AssetErrorCodes
{
    public const string SatelliteDisabled = "ASSET_SATELLITE_DISABLED";
}

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
    bool IsEnabled,
    IReadOnlyList<string> DevelopmentPhases);

/// <summary>
/// 参数缓存列表项（API 出参）。对应 <c>param_cache</c> 结构化列，不含 <c>raw_json</c>。
/// </summary>
public sealed record ParamCacheView(
    string TasookNo,
    string SatelliteNo,
    int ParaId,
    string? ParaCode,
    string? ParaDesc,
    string DisplayLabel,
    string? ParaTypeDesc,
    double? MinValue,
    double? MaxValue,
    int? UpdateTime,
    string? ProcDesc,
    int? PrmSysId,
    string? SourceVersion,
    DateTimeOffset LastSyncedAt);

/// <summary>
/// 指令缓存列表项（API 出参）。对应 <c>command_cache</c> 结构化列，不含 <c>raw_json</c>。
/// </summary>
public sealed record CommandCacheView(
    string TasookNo,
    string SatelliteNo,
    int CmdId,
    string? CmdCode,
    string? CmdName,
    string? CmdDesc,
    int? CmdType,
    int? CmdLen,
    int? ExeTime,
    int? ValidFlag,
    int? CmdSysId,
    string? SourceVersion,
    DateTimeOffset LastSyncedAt);

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

    Task SetSatelliteEnabledAsync(
        string tasookNo,
        string satelliteNo,
        bool isEnabled,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ParamCache>> GetParametersAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CommandCache>> GetCommandsAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TestBatchCache>> GetTestBatchesAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken);

    /// <summary>按星聚合测试阶段名称（<c>test_batch_cache.test_batch_name</c>，去重、按最近阶段时间倒序）。</summary>
    Task<IReadOnlyDictionary<(string TasookNo, string SatelliteNo), IReadOnlyList<string>>> GetDevelopmentPhaseLabelsBySatelliteAsync(
        CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 海量数据接口（Web API v2）。仅服务于 6.1.2 同步流程的 Step 1 / 2 / 2b / 4。
/// v2 卫星主键为 <c>taskNo + satNo</c>，已不再使用 <c>dbStage</c>；
/// Step 3「测试阶段」由 <see cref="ISatelliteAssetProvider"/>（卫星测试流程规划服务）提供。
/// </summary>
public interface IMassDataAssetProvider
{
    Task<IReadOnlyCollection<SatelliteCache>> GetSatellitesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ParamCache>> GetParametersAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CommandCache>> GetCommandsAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken);

    Task<MongoConnectionInfo?> GetMongoInfoAsync(
        string tasookNo,
        string satelliteNo,
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
