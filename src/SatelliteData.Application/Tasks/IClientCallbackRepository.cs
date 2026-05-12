namespace SatelliteData.Application.Tasks;

public sealed record ClientCallbackRow(
    Guid CallbackId,
    string CallbackUrl,
    string SecretRef,
    int MaxRetryCount,
    bool Enabled);

public interface IClientCallbackRepository
{
    Task<IReadOnlyList<ClientCallbackRow>> GetEnabledCallbacksAsync(CancellationToken cancellationToken);

    Task InsertDeliveryAsync(
        Guid deliveryId,
        string eventId,
        Guid callbackId,
        Guid? runId,
        string eventType,
        string payloadJson,
        string status,
        CancellationToken cancellationToken);

    Task UpdateDeliveryAsync(
        Guid deliveryId,
        string status,
        int responseStatus,
        string? responseBody,
        CancellationToken cancellationToken);
}
