using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Api.Controllers;

public static class AssetEndpoints
{
    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/asset");

        group.MapGet("/sources", async (
            HttpContext context,
            DataSourceConfigService service,
            CancellationToken cancellationToken) =>
        {
            var configs = await service.GetConfigsAsync(cancellationToken);
            return Results.Ok(ApiResponse<IReadOnlyCollection<DataSourceConfig>>.Ok(configs, context));
        });

        group.MapPost("/sources", async (
            HttpContext context,
            [FromBody] SaveDataSourceConfigRequest request,
            DataSourceConfigService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var config = await service.SaveConfigAsync(null, request, cancellationToken);
                return Results.Ok(ApiResponse<DataSourceConfig>.Ok(config, context));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponse<object>.Fail("ASSET_CONFIG_INVALID", ex.Message, context));
            }
        });

        group.MapPut("/sources/{sourceId:guid}", async (
            HttpContext context,
            Guid sourceId,
            [FromBody] SaveDataSourceConfigRequest request,
            DataSourceConfigService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var config = await service.SaveConfigAsync(sourceId, request, cancellationToken);
                return Results.Ok(ApiResponse<DataSourceConfig>.Ok(config, context));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponse<object>.Fail("ASSET_CONFIG_INVALID", ex.Message, context));
            }
        });

        group.MapPatch("/sources/{sourceId:guid}/status", async (
            HttpContext context,
            Guid sourceId,
            [FromBody] SetDataSourceStatusRequest request,
            DataSourceConfigService service,
            CancellationToken cancellationToken) =>
        {
            var config = await service.SetStatusAsync(sourceId, request.Enabled, cancellationToken);
            return config is null
                ? Results.NotFound(ApiResponse<object>.Fail("ASSET_SOURCE_NOT_FOUND", "data source config not found", context))
                : Results.Ok(ApiResponse<DataSourceConfig>.Ok(config, context));
        });

        group.MapPost("/sources/{sourceId:guid}/test", async (
            HttpContext context,
            Guid sourceId,
            DataSourceConfigService service,
            IHttpClientFactory httpClientFactory,
            CancellationToken cancellationToken) =>
        {
            var result = await service.TestConnectionAsync(
                sourceId,
                async (config, token) =>
                {
                    if (config.SourceType is DataSourceTypes.MassDataApi or DataSourceTypes.SatelliteAssetApi)
                    {
                        var client = httpClientFactory.CreateClient();
                        client.Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs);
                        using var response = await client.GetAsync(config.EndpointUrl, token);
                        response.EnsureSuccessStatusCode();
                    }
                },
                cancellationToken);

            return Results.Ok(ApiResponse<ConnectionTestResult>.Ok(result, context));
        });

        group.MapPost("/sync", async (
            HttpContext context,
            AssetSyncService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SyncAllAsync(cancellationToken);
            return Results.Ok(ApiResponse<AssetSyncResult>.Ok(result, context));
        });

        group.MapPost("/satellites/{tasookNo}/{satelliteNo}/sync", async (
            HttpContext context,
            string tasookNo,
            string satelliteNo,
            AssetSyncService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SyncSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
            return Results.Ok(ApiResponse<AssetSyncResult>.Ok(result, context));
        });

        group.MapGet("/satellites", async (
            HttpContext context,
            IAssetCacheRepository repository,
            CancellationToken cancellationToken) =>
        {
            var satellites = await repository.GetSatellitesAsync(cancellationToken);
            return Results.Ok(ApiResponse<IReadOnlyCollection<SatelliteCache>>.Ok(satellites, context));
        });

        group.MapGet("/satellites/{tasookNo}/{satelliteNo}", async (
            HttpContext context,
            string tasookNo,
            string satelliteNo,
            IAssetCacheRepository repository,
            CancellationToken cancellationToken) =>
        {
            var satellite = await repository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
            return satellite is null
                ? Results.NotFound(ApiResponse<object>.Fail("ASSET_SATELLITE_NOT_FOUND", "satellite cache not found", context))
                : Results.Ok(ApiResponse<SatelliteCache>.Ok(satellite, context));
        });

        group.MapGet("/satellites/{tasookNo}/{satelliteNo}/params", async (
            HttpContext context,
            string tasookNo,
            string satelliteNo,
            [FromQuery] string? keyword,
            [FromQuery] int pageNo,
            [FromQuery] int pageSize,
            IAssetCacheRepository repository,
            CancellationToken cancellationToken) =>
        {
            pageNo = pageNo <= 0 ? 1 : pageNo;
            pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 500);
            var parameters = await repository.GetParametersAsync(tasookNo, satelliteNo, cancellationToken);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                parameters = parameters
                    .Where(item => item.ParamId.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                        || item.ParamName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            var page = new PagedResult<ParamCache>(
                pageNo,
                pageSize,
                parameters.Count,
                parameters.Skip((pageNo - 1) * pageSize).Take(pageSize).ToArray());

            return Results.Ok(ApiResponse<PagedResult<ParamCache>>.Ok(page, context));
        });

        group.MapGet("/satellites/{tasookNo}/{satelliteNo}/test-batches", async (
            HttpContext context,
            string tasookNo,
            string satelliteNo,
            IAssetCacheRepository repository,
            CancellationToken cancellationToken) =>
        {
            var testBatches = await repository.GetTestBatchesAsync(tasookNo, satelliteNo, cancellationToken);
            return Results.Ok(ApiResponse<IReadOnlyCollection<TestBatchCache>>.Ok(testBatches, context));
        });

        group.MapGet("/satellites/{tasookNo}/{satelliteNo}/mongo-info", async (
            HttpContext context,
            string tasookNo,
            string satelliteNo,
            MongoConnectionPool mongoConnectionPool,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var mongoInfo = await mongoConnectionPool.GetConnectionInfoAsync(tasookNo, satelliteNo, cancellationToken);
                var summary = new
                {
                    mongoInfo.DbName,
                    mongoInfo.AuthRef,
                    MongoUri = MaskMongoUri(mongoInfo.MongoUri)
                };
                return Results.Ok(ApiResponse<object>.Ok(summary, context));
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(ApiResponse<object>.Fail("ASSET_MONGO_NOT_FOUND", ex.Message, context));
            }
        });

        group.MapDelete("/cache", async (
            HttpContext context,
            IAssetCacheRepository repository,
            CancellationToken cancellationToken) =>
        {
            await repository.ClearAsync(cancellationToken);
            return Results.Ok(ApiResponse<object>.Ok(new { cleared = true }, context));
        });

        return endpoints;
    }

    private static string MaskMongoUri(string uri)
    {
        var at = uri.IndexOf('@', StringComparison.Ordinal);
        var scheme = uri.IndexOf("://", StringComparison.Ordinal);
        if (at > 0 && scheme > 0 && at > scheme)
        {
            return uri[..(scheme + 3)] + "***:***" + uri[at..];
        }

        return uri;
    }
}
