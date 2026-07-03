using Microsoft.Extensions.Logging;
using SatelliteData.Domain.Assets;

namespace SatelliteData.Application.Assets;

/// <summary>
/// 资产同步编排服务。严格按照 6.1.2 流程执行：
/// Step 1：拉取全量卫星列表，确定 (taskNo, satNo) 二元组并 upsert <c>satellite_cache</c>；
/// Step 2：每星拉取参数元数据 → upsert <c>param_cache</c>，并更新 <c>satellite_cache.cached_parameter_count</c>；
/// Step 2b：每星拉取指令元数据 → upsert <c>command_cache</c>，并更新 <c>satellite_cache.cached_command_count</c>；
/// Step 3：每星拉取测试阶段 → upsert <c>test_batch_cache</c>；
/// Step 4：每星拉取 Mongo 连接配置 → 更新 <c>satellite_cache.mongo_uri</c> 等列。
///
/// 故障隔离：Step 1 失败整体退出；Step 2/2b/3/4 单星失败仅记录该星，不影响其它星；
/// 失败比例 ≥ <see cref="FailureRatioThreshold"/> 时整体标记 <see cref="AssetSyncStatus.PartialSucceeded"/>。
/// </summary>
public sealed class AssetSyncService(
    IMassDataAssetProvider massDataProvider,
    ISatelliteAssetProvider satelliteAssetProvider,
    IAssetCacheRepository cacheRepository,
    MongoConnectionPool mongoConnectionPool,
    ILogger<AssetSyncService> logger)
{
    private const double FailureRatioThreshold = 0.5d;

    public async Task<AssetSyncResult> SyncAllAsync(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SatelliteCache> satellites;
        try
        {
            satellites = await massDataProvider.GetSatellitesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "全量卫星列表拉取失败 (Step 1)");
            return new AssetSyncResult(
                AssetSyncStatus.Failed,
                0, 0, 0, 0, 0,
                DateTimeOffset.UtcNow,
                "ASSET_001",
                "Step 1 全量卫星列表拉取失败：" + ex.Message,
                Array.Empty<SatelliteSyncOutcome>());
        }

        var outcomes = new List<SatelliteSyncOutcome>(satellites.Count);
        var totalParameters = 0;
        var totalCommands = 0;
        var totalTestPhases = 0;
        var failedCount = 0;

        foreach (var satellite in satellites)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await cacheRepository.UpsertSatelliteAsync(satellite, cancellationToken);

            var outcome = await SyncSubstepsAsync(satellite, cancellationToken);
            outcomes.Add(outcome);
            totalParameters += outcome.ParameterCount;
            totalCommands += outcome.CommandCount;
            totalTestPhases += outcome.TestPhaseCount;
            if (!outcome.FullySucceeded)
            {
                failedCount++;
            }
        }

        var status = ResolveStatus(satellites.Count, failedCount);

        return new AssetSyncResult(
            status,
            satellites.Count,
            totalParameters,
            totalCommands,
            totalTestPhases,
            failedCount,
            DateTimeOffset.UtcNow,
            ErrorCode: null,
            ErrorMessage: null,
            outcomes);
    }

    public async Task<AssetSyncResult> SyncSatelliteAsync(
        string tasookNo,
        string satelliteNo,
        CancellationToken cancellationToken)
    {
        var cached = await cacheRepository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
        if (cached is null)
        {
            // 无缓存时尝试从全量列表中找一次
            var satellites = await massDataProvider.GetSatellitesAsync(cancellationToken);
            cached = satellites.SingleOrDefault(s =>
                s.TasookNo == tasookNo && s.SatelliteNo == satelliteNo);

            if (cached is null)
            {
                return new AssetSyncResult(
                    AssetSyncStatus.Failed,
                    0, 0, 0, 0, 1,
                    DateTimeOffset.UtcNow,
                    "ASSET_002",
                    $"卫星不存在：{tasookNo}/{satelliteNo}",
                    Array.Empty<SatelliteSyncOutcome>());
            }

            await cacheRepository.UpsertSatelliteAsync(cached, cancellationToken);
        }

        var outcome = await SyncSubstepsAsync(cached, cancellationToken);
        return new AssetSyncResult(
            outcome.FullySucceeded ? AssetSyncStatus.Succeeded : AssetSyncStatus.PartialSucceeded,
            1,
            outcome.ParameterCount,
            outcome.CommandCount,
            outcome.TestPhaseCount,
            outcome.FullySucceeded ? 0 : 1,
            DateTimeOffset.UtcNow,
            ErrorCode: null,
            ErrorMessage: outcome.FailureReason,
            new[] { outcome });
    }

    public async Task RefreshIfExpiredAsync(
        string tasookNo,
        string satelliteNo,
        TimeSpan maxAge,
        CancellationToken cancellationToken)
    {
        var cached = await cacheRepository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
        if (cached is null || DateTimeOffset.UtcNow - cached.LastSyncedAt > maxAge)
        {
            await SyncSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
        }
    }

    public Task ClearAllCacheAsync(CancellationToken cancellationToken)
    {
        return cacheRepository.ClearAsync(cancellationToken);
    }

    private async Task<SatelliteSyncOutcome> SyncSubstepsAsync(
        SatelliteCache satellite,
        CancellationToken cancellationToken)
    {
        var (tasookNo, satelliteNo) = (satellite.TasookNo, satellite.SatelliteNo);

        var parametersOk = false;
        var commandsOk = false;
        var phasesOk = false;
        var mongoOk = false;
        var paramCount = 0;
        var commandCount = 0;
        var phaseCount = 0;
        string? failureReason = null;

        try
        {
            var parameters = await massDataProvider.GetParametersAsync(tasookNo, satelliteNo, cancellationToken);
            await cacheRepository.UpsertParametersAsync(parameters, cancellationToken);
            paramCount = parameters.Count;
            parametersOk = true;
            await TouchSatelliteCountsAsync(tasookNo, satelliteNo, paramCount, null, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failureReason = AppendReason(failureReason, $"Step 2 参数同步失败：{ex.Message}");
            logger.LogWarning(ex, "参数同步失败 {TasookNo}/{SatelliteNo}", tasookNo, satelliteNo);
        }

        try
        {
            var commands = await massDataProvider.GetCommandsAsync(tasookNo, satelliteNo, cancellationToken);
            await cacheRepository.UpsertCommandsAsync(tasookNo, satelliteNo, commands, cancellationToken);
            commandCount = commands.Count;
            commandsOk = true;
            await TouchSatelliteCountsAsync(tasookNo, satelliteNo, null, commandCount, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failureReason = AppendReason(failureReason, $"Step 2b 指令同步失败：{ex.Message}");
            logger.LogWarning(ex, "指令同步失败 {TasookNo}/{SatelliteNo}", tasookNo, satelliteNo);
        }

        try
        {
            var phases = await satelliteAssetProvider.GetTestPhasesAsync(tasookNo, satelliteNo, cancellationToken);
            await cacheRepository.UpsertTestBatchesAsync(phases, cancellationToken);
            phaseCount = phases.Count;
            phasesOk = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failureReason = AppendReason(failureReason, $"Step 3 测试阶段同步失败：{ex.Message}");
            logger.LogWarning(ex, "测试阶段同步失败 {TasookNo}/{SatelliteNo}", tasookNo, satelliteNo);
        }

        try
        {
            var mongoInfo = await massDataProvider.GetMongoInfoAsync(tasookNo, satelliteNo, cancellationToken);
            var latest = await cacheRepository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken) ?? satellite;
            if (mongoInfo is not null)
            {
                var refreshed = latest with { MongoInfo = mongoInfo, LastSyncedAt = DateTimeOffset.UtcNow };
                await cacheRepository.UpsertSatelliteAsync(refreshed, cancellationToken);

                if (latest.MongoInfo is null
                    || !string.Equals(latest.MongoInfo.MongoUri, mongoInfo.MongoUri, StringComparison.Ordinal))
                {
                    mongoConnectionPool.Invalidate(tasookNo, satelliteNo);
                }
            }

            mongoOk = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failureReason = AppendReason(failureReason, $"Step 4 Mongo 配置同步失败：{ex.Message}");
            logger.LogWarning(ex, "Mongo 配置同步失败 {TasookNo}/{SatelliteNo}", tasookNo, satelliteNo);
        }

        return new SatelliteSyncOutcome(
            tasookNo, satelliteNo,
            parametersOk, commandsOk, phasesOk, mongoOk,
            paramCount, commandCount, phaseCount,
            failureReason);
    }

    private async Task TouchSatelliteCountsAsync(
        string tasookNo,
        string satelliteNo,
        int? cachedParameterCount,
        int? cachedCommandCount,
        CancellationToken cancellationToken)
    {
        var latest = await cacheRepository.GetSatelliteAsync(tasookNo, satelliteNo, cancellationToken);
        if (latest is null)
        {
            return;
        }

        var merged = latest with
        {
            CachedParameterCount = cachedParameterCount ?? latest.CachedParameterCount,
            CachedCommandCount = cachedCommandCount ?? latest.CachedCommandCount,
            LastSyncedAt = DateTimeOffset.UtcNow
        };

        await cacheRepository.UpsertSatelliteAsync(merged, cancellationToken);
    }

    private static AssetSyncStatus ResolveStatus(int total, int failed)
    {
        if (total == 0)
        {
            return AssetSyncStatus.Succeeded;
        }

        if (failed == 0)
        {
            return AssetSyncStatus.Succeeded;
        }

        var ratio = (double)failed / total;
        return ratio >= FailureRatioThreshold
            ? AssetSyncStatus.Failed
            : AssetSyncStatus.PartialSucceeded;
    }

    private static string AppendReason(string? existing, string newReason)
    {
        return string.IsNullOrEmpty(existing) ? newReason : existing + "; " + newReason;
    }
}
