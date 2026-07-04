using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SatelliteData.Infrastructure.ClickHouse;

/// <summary>
/// 周期性触发 <see cref="BatchedClickHouseGateway"/> 的时间阈值刷写，并在进程停机时排空剩余缓冲。
/// </summary>
public sealed class ClickHouseBatchFlushHostedService : BackgroundService
{
    private readonly BatchedClickHouseGateway _gateway;
    private readonly ILogger<ClickHouseBatchFlushHostedService> _logger;
    private readonly TimeSpan _tick;

    public ClickHouseBatchFlushHostedService(
        BatchedClickHouseGateway gateway,
        ILogger<ClickHouseBatchFlushHostedService> logger)
    {
        _gateway = gateway;
        _logger = logger;
        // 以时间阈值的一半频率轮询，保证超时刷写延迟不超过阈值太多。
        var half = gateway.FlushInterval.TotalMilliseconds / 2;
        _tick = TimeSpan.FromMilliseconds(Math.Clamp(half, 100, 1000));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_tick);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _gateway.FlushIfDueAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ClickHouse 定时攒批刷写失败");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 停机信号，进入最终排空。
        }

        try
        {
            await _gateway.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClickHouse 停机刷写剩余攒批数据失败，可能丢失最后一批数据");
        }
    }
}
