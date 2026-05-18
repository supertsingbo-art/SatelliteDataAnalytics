using System.Diagnostics;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

public sealed class DataSourceConfigService(IDataSourceConfigRepository repository)
{
    private static readonly HashSet<string> AllowedSourceTypes = new(StringComparer.Ordinal)
    {
        DataSourceTypes.MassDataApi,
        DataSourceTypes.SatelliteAssetApi,
        DataSourceTypes.ClickHouse,
        DataSourceTypes.Minio,
        DataSourceTypes.PgMeta
    };

    public Task<IReadOnlyCollection<DataSourceConfig>> GetConfigsAsync(CancellationToken cancellationToken)
    {
        return repository.GetAllAsync(cancellationToken);
    }

    public async Task<DataSourceConfig> SaveConfigAsync(
        Guid? sourceId,
        SaveDataSourceConfigRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);
        var now = DateTimeOffset.UtcNow;
        var existing = sourceId.HasValue
            ? await repository.GetByIdAsync(sourceId.Value, cancellationToken)
            : null;

        var config = new DataSourceConfig(
            sourceId ?? Guid.NewGuid(),
            request.SourceType,
            request.SourceName,
            request.EndpointUrl.TrimEnd('/'),
            request.AuthType,
            request.AuthSecretRef,
            request.TimeoutMs,
            request.Enabled,
            request.Env,
            existing?.CreatedAt ?? now,
            now);

        await repository.SaveAsync(config, cancellationToken);
        return config;
    }

    public async Task<DataSourceConfig?> SetStatusAsync(
        Guid sourceId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var existing = await repository.GetByIdAsync(sourceId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var updated = existing with
        {
            Enabled = enabled,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.SaveAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        Guid sourceId,
        Func<DataSourceConfig, CancellationToken, Task> tester,
        CancellationToken cancellationToken)
    {
        var config = await repository.GetByIdAsync(sourceId, cancellationToken);
        if (config is null)
        {
            return new ConnectionTestResult(false, "data source config not found", null);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await tester(config, cancellationToken);
            stopwatch.Stop();
            return new ConnectionTestResult(true, "connection test succeeded", (int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            stopwatch.Stop();
            return new ConnectionTestResult(false, ex.Message, (int)stopwatch.ElapsedMilliseconds);
        }
    }

    private static void Validate(SaveDataSourceConfigRequest request)
    {
        if (string.Equals(request.SourceType, "MONGO", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("MONGO source is not allowed; MongoDB address must come from MassData API.");
        }

        if (!AllowedSourceTypes.Contains(request.SourceType))
        {
            throw new InvalidOperationException($"Unsupported source type: {request.SourceType}");
        }

        var httpSourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DataSourceTypes.MassDataApi,
            DataSourceTypes.SatelliteAssetApi,
            DataSourceTypes.Minio
        };

        if (string.IsNullOrWhiteSpace(request.EndpointUrl))
        {
            throw new InvalidOperationException("EndpointUrl must not be empty.");
        }

        if (httpSourceTypes.Contains(request.SourceType) &&
            !Uri.TryCreate(request.EndpointUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("EndpointUrl must be an absolute URI for HTTP-based data sources.");
        }
    }
}
