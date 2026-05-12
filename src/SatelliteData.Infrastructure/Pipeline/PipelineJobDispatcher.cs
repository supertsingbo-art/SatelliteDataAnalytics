using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using SatelliteData.Application.Pipeline;

namespace SatelliteData.Infrastructure.Pipeline;

public sealed class PipelineJobDispatcher(IServiceScopeFactory scopeFactory)
{
    [Queue("preprocess")]
    public async Task RunPreprocess(Guid runId)
    {
        using var scope = scopeFactory.CreateScope();
        var p = scope.ServiceProvider.GetRequiredService<IPreprocessPipeline>();
        await p.ExecuteAsync(runId, CancellationToken.None).ConfigureAwait(false);
    }

    [Queue("algorithm")]
    public async Task RunAlgorithm(Guid runId)
    {
        using var scope = scopeFactory.CreateScope();
        var p = scope.ServiceProvider.GetRequiredService<IAlgorithmExecutionPipeline>();
        await p.ExecuteAsync(runId, CancellationToken.None).ConfigureAwait(false);
    }

    [Queue("webhook")]
    public async Task RunWebhook(Guid runId)
    {
        using var scope = scopeFactory.CreateScope();
        var p = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryPipeline>();
        await p.DeliverTerminalAsync(runId, CancellationToken.None).ConfigureAwait(false);
    }
}
