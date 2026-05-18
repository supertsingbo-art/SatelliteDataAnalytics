using Microsoft.Extensions.Options;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;
using SatelliteData.Infrastructure.HttpClients;
using SatelliteData.Infrastructure;

namespace SatelliteData.Infrastructure.PostgreSql;

public sealed class InMemoryDataSourceConfigRepository : IDataSourceConfigRepository
{
    private readonly Dictionary<Guid, DataSourceConfig> _configs = [];

    public InMemoryDataSourceConfigRepository(
        IOptions<AssetProviderOptions> assetProviders,
        IOptions<DatabaseConnectionOptions> databaseConnections)
    {
        var assets = assetProviders.Value;
        var connections = databaseConnections.Value;

        Seed(DataSourceTypes.MassDataApi, "海量数据接口服务-开发", assets.MassDataApiBaseUrl, assets.DefaultDbStage);
        Seed(DataSourceTypes.SatelliteAssetApi, "卫星测试流程规划服务-开发", assets.SatelliteAssetApiBaseUrl, assets.DefaultDbStage);
        Seed(DataSourceTypes.ClickHouse, "ClickHouse 分析库-开发", connections.ClickHouse, assets.DefaultDbStage);
        Seed(DataSourceTypes.Minio, "MinIO 对象存储-开发", assets.MinioBaseUrl, assets.DefaultDbStage);
        Seed(DataSourceTypes.PgMeta, "PostgreSQL 元数据库-开发", connections.Postgres, assets.DefaultDbStage);
    }

    public Task<IReadOnlyCollection<DataSourceConfig>> GetAllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyCollection<DataSourceConfig>>(_configs.Values.OrderBy(item => item.SourceType).ToArray());
    }

    public Task<DataSourceConfig?> GetByIdAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        _configs.TryGetValue(sourceId, out var config);
        return Task.FromResult(config);
    }

    public Task<DataSourceConfig?> GetEnabledByTypeAsync(string sourceType, CancellationToken cancellationToken)
    {
        var config = _configs.Values.FirstOrDefault(item =>
            item.Enabled && string.Equals(item.SourceType, sourceType, StringComparison.Ordinal));
        return Task.FromResult(config);
    }

    public Task SaveAsync(DataSourceConfig config, CancellationToken cancellationToken)
    {
        _configs[config.SourceId] = config;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        _configs.Remove(sourceId);
        return Task.CompletedTask;
    }

    private void Seed(string sourceType, string sourceName, string endpointUrl, string env)
    {
        var now = DateTimeOffset.UtcNow;
        var config = new DataSourceConfig(
            Guid.NewGuid(),
            sourceType,
            sourceName,
            endpointUrl,
            "NONE",
            null,
            10000,
            true,
            env,
            now,
            now);

        _configs[config.SourceId] = config;
    }
}

public sealed class InMemoryAssetCacheRepository : IAssetCacheRepository
{
    private readonly Dictionary<(string TasookNo, string SatelliteNo), SatelliteCache> _satellites = [];
    private readonly Dictionary<(string TasookNo, string SatelliteNo, int ParaId), ParamCache> _parameters = [];
    private readonly Dictionary<(string TasookNo, string SatelliteNo, int CmdId), CommandCache> _commands = [];
    private readonly Dictionary<(string TasookNo, string SatelliteNo, string TestBatchId), TestBatchCache> _testBatches = [];

    public Task UpsertSatelliteAsync(SatelliteCache satellite, CancellationToken cancellationToken)
    {
        _satellites[(satellite.TasookNo, satellite.SatelliteNo)] = satellite;
        return Task.CompletedTask;
    }

    public Task UpsertParametersAsync(IReadOnlyCollection<ParamCache> parameters, CancellationToken cancellationToken)
    {
        foreach (var parameter in parameters)
        {
            _parameters[(parameter.TasookNo, parameter.SatelliteNo, parameter.ParaId)] = parameter;
        }

        return Task.CompletedTask;
    }

    public Task UpsertCommandsAsync(
        string tasookNo,
        string satelliteNo,
        IReadOnlyCollection<CommandCache> commands,
        CancellationToken cancellationToken)
    {
        foreach (var key in _commands.Keys
                     .Where(k => k.TasookNo == tasookNo && k.SatelliteNo == satelliteNo)
                     .ToList())
        {
            _commands.Remove(key);
        }

        foreach (var command in commands)
        {
            _commands[(command.TasookNo, command.SatelliteNo, command.CmdId)] = command;
        }

        return Task.CompletedTask;
    }

    public Task UpsertTestBatchesAsync(IReadOnlyCollection<TestBatchCache> testBatches, CancellationToken cancellationToken)
    {
        foreach (var testBatch in testBatches)
        {
            _testBatches[(testBatch.TasookNo, testBatch.SatelliteNo, testBatch.TestBatchId)] = testBatch;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<SatelliteCache>> GetSatellitesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyCollection<SatelliteCache>>(_satellites.Values.ToArray());
    }

    public Task<SatelliteCache?> GetSatelliteAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        _satellites.TryGetValue((tasookNo, satelliteNo), out var satellite);
        return Task.FromResult(satellite);
    }

    public Task<IReadOnlyCollection<ParamCache>> GetParametersAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        var parameters = _parameters.Values
            .Where(item => item.TasookNo == tasookNo && item.SatelliteNo == satelliteNo)
            .OrderBy(item => item.ParaId)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<ParamCache>>(parameters);
    }

    public Task<IReadOnlyCollection<CommandCache>> GetCommandsAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        var commands = _commands.Values
            .Where(item => item.TasookNo == tasookNo && item.SatelliteNo == satelliteNo)
            .OrderBy(item => item.CmdId)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<CommandCache>>(commands);
    }

    public Task<IReadOnlyCollection<TestBatchCache>> GetTestBatchesAsync(string tasookNo, string satelliteNo, CancellationToken cancellationToken)
    {
        var testBatches = _testBatches.Values
            .Where(item => item.TasookNo == tasookNo && item.SatelliteNo == satelliteNo)
            .OrderByDescending(item => item.StartTs)
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<TestBatchCache>>(testBatches);
    }

    public Task<IReadOnlyDictionary<(string TasookNo, string SatelliteNo), IReadOnlyList<string>>>
        GetDevelopmentPhaseLabelsBySatelliteAsync(CancellationToken cancellationToken)
    {
        var grouped = _testBatches.Values
            .GroupBy(t => (t.TasookNo, t.SatelliteNo))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g
                    .OrderByDescending(t => t.StartTs)
                    .Select(t =>
                    {
                        var label = string.IsNullOrWhiteSpace(t.Scenario) ? t.TestBatchId : t.Scenario!.Trim();
                        return label;
                    })
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        return Task.FromResult<IReadOnlyDictionary<(string TasookNo, string SatelliteNo), IReadOnlyList<string>>>(grouped);
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        _satellites.Clear();
        _parameters.Clear();
        _commands.Clear();
        _testBatches.Clear();
        return Task.CompletedTask;
    }
}
