using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SatelliteData.Application.Tasks;
using SatelliteData.Domain.Tasks;

namespace SatelliteData.Application.Pipeline;

public sealed class WebhookDeliveryPipeline(
    ITaskRunRepository taskRuns,
    ITaskEventRepository taskEvents,
    IClientCallbackRepository callbacks,
    IHttpClientFactory httpClientFactory,
    ILogger<WebhookDeliveryPipeline> logger) : IWebhookDeliveryPipeline
{
    public async Task DeliverTerminalAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await taskRuns.GetByRunIdAsync(runId, cancellationToken);
        if (run is null) return;

        if (run.Status is not (TaskRunStatus.Succeeded or TaskRunStatus.Failed or TaskRunStatus.Timeout))
        {
            return;
        }

        var eventType = run.Status switch
        {
            TaskRunStatus.Succeeded => "job.succeeded",
            TaskRunStatus.Timeout => "job.timeout",
            _ => "job.failed"
        };

        var targets = await callbacks.GetEnabledCallbacksAsync(cancellationToken);
        if (targets.Count == 0)
        {
            logger.LogDebug("No enabled webhooks; skip delivery for {RunId}", runId);
            await MarkWebhookProgressAsync(run, cancellationToken);
            return;
        }

        var client = httpClientFactory.CreateClient("webhook");
        var payload = JsonSerializer.Serialize(new
        {
            event_type = eventType,
            run_id = runId,
            job_id = run.JobId,
            status = run.Status.ToString(),
            error_code = run.ErrorCode,
            error_msg = run.ErrorMsg,
            tasook_no = run.TasookNo,
            satellite_no = run.SatelliteNo
        });

        foreach (var cb in targets)
        {
            var eventId = $"{runId:N}:{cb.CallbackId:N}:{eventType}";
            var deliveryId = Guid.NewGuid();
            await callbacks.InsertDeliveryAsync(
                deliveryId,
                eventId,
                cb.CallbackId,
                runId,
                eventType,
                payload,
                "Pending",
                cancellationToken);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, cb.CallbackUrl);
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                var resp = await client.SendAsync(req, cancellationToken);
                var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                await callbacks.UpdateDeliveryAsync(
                    deliveryId,
                    resp.IsSuccessStatusCode ? "Delivered" : "Failed",
                    (int)resp.StatusCode,
                    body.Length > 8000 ? body[..8000] : body,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Webhook delivery failed {CallbackId}", cb.CallbackId);
                await callbacks.UpdateDeliveryAsync(deliveryId, "Failed", 0, ex.Message, cancellationToken);
            }
        }

        await MarkWebhookProgressAsync(run, cancellationToken);
    }

    private async Task MarkWebhookProgressAsync(TaskRun run, CancellationToken cancellationToken)
    {
        var r = await taskRuns.GetByRunIdAsync(run.RunId, cancellationToken);
        if (r is null) return;
        var end = DateTimeOffset.UtcNow;
        await taskRuns.UpdateAsync(
            r with { ProgressPercent = TaskProgressBands.WebhookMax, CurrentStep = "webhook_done", EndTime = r.EndTime ?? end },
            cancellationToken);
        await taskEvents.AppendAsync(
            new TaskEvent(
                Guid.NewGuid(),
                r.RunId,
                "webhook.done",
                "Succeeded",
                null,
                null,
                null,
                end),
            cancellationToken);
    }
}
