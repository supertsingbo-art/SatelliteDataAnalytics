using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using SatelliteData.Application.Pipeline;
using SatelliteData.Application.Tasks;

namespace SatelliteData.Infrastructure.Pipeline;

public sealed class PipelineJobDispatcher(IServiceScopeFactory scopeFactory)
{
    [Queue("preprocess")]
    public async Task RunPreprocess(Guid runId)
    {
        using var scope = scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ITaskRunCancellationRegistry>();
        using var reg = registry.Register(runId);
        var p = scope.ServiceProvider.GetRequiredService<IPreprocessPipeline>();
        try
        {
            await p.ExecuteAsync(runId, reg.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (reg.Token.IsCancellationRequested)
        {
            // 用户取消，正常结束
        }
    }

    [Queue("algorithm")]
    public async Task RunAlgorithm(Guid runId)
    {
        using var scope = scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ITaskRunCancellationRegistry>();
        using var reg = registry.Register(runId);
        var p = scope.ServiceProvider.GetRequiredService<IAlgorithmExecutionPipeline>();
        try
        {
            await p.ExecuteAsync(runId, reg.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (reg.Token.IsCancellationRequested)
        {
            // 用户取消，正常结束
        }
    }

    [Queue("webhook")]
    public async Task RunWebhook(Guid runId)
    {
        using var scope = scopeFactory.CreateScope();
        var p = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryPipeline>();
        await p.DeliverTerminalAsync(runId, CancellationToken.None).ConfigureAwait(false);
    }

    [Queue("preprocess")]
    public async Task RunDailyPreprocessSchedule(Guid scheduleId)
    {
        using var scope = scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<PreprocessScheduleService>();
        await svc.TriggerInstanceAsync(scheduleId, CancellationToken.None).ConfigureAwait(false);
    }
}
