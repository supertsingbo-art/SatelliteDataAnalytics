using System.Text.Json;

namespace SatelliteData.Domain.Assets;

public static class DataSourceTypes
{
    public const string MassDataApi = "MASS_DATA_API";
    /// <summary>卫星测试流程规划服务（配置枚举值保持 <c>SATELLITE_ASSET_API</c> 兼容）。</summary>
    public const string SatelliteAssetApi = "SATELLITE_ASSET_API";
    public const string ClickHouse = "CLICKHOUSE";
    public const string Minio = "MINIO";
    public const string PgMeta = "PG_META";
}

public sealed record DataSourceConfig(
    Guid SourceId,
    string SourceType,
    string SourceName,
    string EndpointUrl,
    string AuthType,
    string? AuthSecretRef,
    int TimeoutMs,
    bool Enabled,
    string Env,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 卫星测试流程规划相关缓存（卫星维度元数据）。<paramref name="DbStage"/> 取自海量接口 <c>satellites</c> 列表，
/// 与 <paramref name="TasookNo"/>、<paramref name="SatelliteNo"/> 共同构成参数 / 阶段 / Mongo 三个二级接口的入参三元组。
/// </summary>
public sealed record SatelliteCache(
    string TasookNo,
    /// <summary>型号名称，海量接口 <c>taskName</c>。</summary>
    string? TasookName,
    string SatelliteNo,
    string SatelliteName,
    string? SatelliteType,
    /// <summary>版本号，海量接口 <c>dbStage</c>（与 tasook/satellite 构成三元组）。</summary>
    string? DbStage,
    MongoConnectionInfo? MongoInfo,
    string? SourceVersion,
    DateTimeOffset LastSyncedAt,
    int CachedParameterCount,
    int CachedCommandCount,
    JsonElement RawJson);

/// <summary>
/// 参数元数据缓存（海量 <c>POST /api/mass-data/basic/parameters</c>）。
/// </summary>
public sealed record ParamCache(
    string TasookNo,
    string SatelliteNo,
    int ParaId,
    string? ParaCode,
    string? ParaDesc,
    string? ParaTypeDesc,
    double? MinValue,
    double? MaxValue,
    int? UpdateTime,
    string? ProcDesc,
    int? PrmSysId,
    string? SourceVersion,
    DateTimeOffset LastSyncedAt,
    JsonElement RawJson)
{
    public string ParamId => ParaId.ToString();

    public string ParamName => ParaCode ?? ParaDesc ?? ParamId;

    public double? ValueMin => MinValue;

    public double? ValueMax => MaxValue;

    public string? ValueType => ParaTypeDesc;
}

/// <summary>
/// 指令元数据缓存，对应海量接口 <c>POST /api/mass-data/basic/commands</c> 返回清单。
/// </summary>
public sealed record CommandCache(
    string TasookNo,
    string SatelliteNo,
    string CommandId,
    string CommandName,
    string? SourceVersion,
    DateTimeOffset LastSyncedAt,
    JsonElement RawJson);

/// <summary>
/// 测试阶段缓存。<paramref name="TestBatchId"/> 即阶段编号，<paramref name="Scenario"/> 即阶段名（如「单机测试」「整星综测」「在轨」）。
/// 表名 <c>test_batch_cache</c> 沿用以保证向后兼容；语义自 V2.0.1 起统一改述为「测试阶段」。
/// </summary>
public sealed record TestBatchCache(
    string TasookNo,
    string SatelliteNo,
    string TestBatchId,
    string? Scenario,
    DateTimeOffset StartTs,
    DateTimeOffset EndTs,
    string? SourceVersion,
    DateTimeOffset LastSyncedAt,
    JsonElement RawJson);

/// <summary>
/// MongoDB 连接信息。<paramref name="MongoUri"/> 已剥离嵌入式凭据；密码引用通过 <paramref name="AuthRef"/> 指向密钥管理服务。
/// </summary>
public sealed record MongoConnectionInfo(
    string MongoUri,
    string DbName,
    string? AuthRef);

/// <summary>
/// 单颗卫星的同步结果（Step 2/3/4 子步骤）。
/// </summary>
public sealed record SatelliteSyncOutcome(
    string TasookNo,
    string SatelliteNo,
    bool ParametersSucceeded,
    bool CommandsSucceeded,
    bool TestPhasesSucceeded,
    bool MongoConfigSucceeded,
    int ParameterCount,
    int CommandCount,
    int TestPhaseCount,
    string? FailureReason)
{
    public bool FullySucceeded =>
        ParametersSucceeded && CommandsSucceeded && TestPhasesSucceeded && MongoConfigSucceeded;
}

public enum AssetSyncStatus
{
    Succeeded = 0,
    PartialSucceeded = 1,
    Failed = 2
}

/// <summary>
/// 全量资产同步结果。Step 1（卫星列表）失败时 <see cref="Status"/> 为 <see cref="AssetSyncStatus.Failed"/>，
/// 单星失败比例 ≥ 50% 时为 <see cref="AssetSyncStatus.PartialSucceeded"/>，全部成功时为 <see cref="AssetSyncStatus.Succeeded"/>。
/// </summary>
public sealed record AssetSyncResult(
    AssetSyncStatus Status,
    int SatelliteCount,
    int ParameterCount,
    int CommandCount,
    int TestBatchCount,
    int FailedSatelliteCount,
    DateTimeOffset SyncedAt,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyCollection<SatelliteSyncOutcome> Outcomes);
