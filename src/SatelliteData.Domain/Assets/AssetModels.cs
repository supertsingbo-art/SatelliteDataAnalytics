using System.Text.Json;

namespace SatelliteData.Domain.Assets;

public static class DataSourceTypes
{
    public const string MassDataApi = "MASS_DATA_API";
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
/// 卫星资产缓存。<paramref name="DbStage"/> 取自海量接口 <c>satellites</c> 列表，
/// 与 <paramref name="TasookNo"/>、<paramref name="SatelliteNo"/> 共同构成参数 / 阶段 / Mongo 三个二级接口的入参三元组。
/// </summary>
public sealed record SatelliteCache(
    string TasookNo,
    string SatelliteNo,
    string SatelliteName,
    string? SatelliteType,
    string? DbStage,
    MongoConnectionInfo? MongoInfo,
    string? SourceVersion,
    DateTimeOffset LastSyncedAt,
    JsonElement RawJson);

public sealed record ParamCache(
    string TasookNo,
    string SatelliteNo,
    string ParamId,
    string ParamName,
    string? Unit,
    string? ValueType,
    double? ValueMin,
    double? ValueMax,
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
    bool TestPhasesSucceeded,
    bool MongoConfigSucceeded,
    int ParameterCount,
    int TestPhaseCount,
    string? FailureReason)
{
    public bool FullySucceeded => ParametersSucceeded && TestPhasesSucceeded && MongoConfigSucceeded;
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
    int TestBatchCount,
    int FailedSatelliteCount,
    DateTimeOffset SyncedAt,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyCollection<SatelliteSyncOutcome> Outcomes);
