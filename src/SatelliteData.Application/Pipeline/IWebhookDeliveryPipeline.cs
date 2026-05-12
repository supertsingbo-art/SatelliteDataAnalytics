namespace SatelliteData.Application.Pipeline;

public interface IWebhookDeliveryPipeline
{
    Task DeliverTerminalAsync(Guid runId, CancellationToken cancellationToken);
}
