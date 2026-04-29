using SatelliteData.Application.Identity;

namespace SatelliteData.Application.Integration;

public sealed class DataScopeAuthorizer(IApiClientRepository clients)
{
    public async Task<bool> IsAllowedAsync(
        Guid clientId,
        DataScopeCheckRequest request,
        CancellationToken cancellationToken)
    {
        var scopes = await clients.GetClientDataScopesAsync(clientId, cancellationToken);

        return scopes.Any(scope =>
            scope.Enabled
            && string.Equals(scope.TasookNo, request.TasookNo, StringComparison.Ordinal)
            && string.Equals(scope.SatelliteNo, request.SatelliteNo, StringComparison.Ordinal)
            && IsBatchAllowed(scope.ScopeLevel, scope.TestBatchId, request.TestBatchId));
    }

    private static bool IsBatchAllowed(string scopeLevel, string? allowedBatchId, string? requestedBatchId)
    {
        if (string.Equals(scopeLevel, "SATELLITE", StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(allowedBatchId, requestedBatchId, StringComparison.Ordinal);
    }
}
