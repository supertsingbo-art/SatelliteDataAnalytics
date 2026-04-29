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

public sealed record SatelliteCache(
    string TasookNo,
    string SatelliteNo,
    string SatelliteName,
    string? SatelliteType,
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

public sealed record MongoConnectionInfo(
    string MongoUri,
    string DbName,
    string? AuthRef);

public sealed record AssetSyncResult(
    int SatelliteCount,
    int ParameterCount,
    int TestBatchCount,
    DateTimeOffset SyncedAt);
