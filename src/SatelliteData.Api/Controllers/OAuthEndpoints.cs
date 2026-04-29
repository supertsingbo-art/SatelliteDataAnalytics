using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using SatelliteData.Application.Identity;
using SatelliteData.Application.Integration;

namespace SatelliteData.Api.Controllers;

public static class OAuthEndpoints
{
    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/oauth2/token", async (
            HttpContext httpContext,
            OAuthTokenService tokenService,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadBasicCredentials(httpContext, out var appId, out var clientSecret))
            {
                return Results.Json(
                    new OAuthErrorContract("invalid_client", "client authentication failed"),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var form = await httpContext.Request.ReadFormAsync(cancellationToken);
            var command = new ClientCredentialsTokenCommand(
                form["grant_type"].ToString(),
                appId,
                clientSecret,
                form["scope"].ToString(),
                httpContext.Connection.RemoteIpAddress?.ToString(),
                httpContext.Request.Headers.UserAgent.ToString());

            var result = await tokenService.IssueClientCredentialsTokenAsync(command, cancellationToken);
            if (!result.Succeeded || result.Error is null || result.Token is null)
            {
                return Results.Json(
                    new OAuthErrorContract(result.Error?.Error ?? "temporarily_unavailable",
                        result.Error?.ErrorDescription ?? "authorization service is unavailable"),
                    statusCode: result.Error?.HttpStatus ?? StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new TokenEndpointResponse(
                result.Token.AccessToken,
                result.Token.TokenType,
                result.Token.ExpiresIn,
                result.Token.Scope));
        })
        .AllowAnonymous()
        .DisableAntiforgery();

        endpoints.MapGet("/openapi/v1/oauth2/me", (ClaimsPrincipal user) =>
        {
            var scopes = user.FindFirstValue("scope") ?? "";
            return Results.Ok(new
            {
                client_id = user.FindFirstValue("client_id"),
                app_id = user.FindFirstValue("app_id"),
                scope = scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                expires_at = DateTimeOffset.FromUnixTimeSeconds(long.Parse(user.FindFirstValue("exp") ?? "0"))
            });
        })
        .RequireAuthorization();

        endpoints.MapPost("/openapi/v1/jobs/pipeline", async (
            [FromBody] CreatePipelineJobRequest request,
            ClaimsPrincipal user,
            DataScopeAuthorizer dataScopeAuthorizer,
            CancellationToken cancellationToken) =>
        {
            if (!HasScope(user, "job:create"))
            {
                return Results.Json(
                    new OAuthErrorContract("insufficient_scope", "job:create scope is required"),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var clientId = Guid.Parse(user.FindFirstValue("client_id")!);
            var allowed = await dataScopeAuthorizer.IsAllowedAsync(
                clientId,
                new DataScopeCheckRequest(request.TasookNo, request.SatelliteNo, request.TestBatchId),
                cancellationToken);

            if (!allowed)
            {
                return Results.Json(
                    new OAuthErrorContract("access_denied", "client data scope is not allowed"),
                    statusCode: StatusCodes.Status403Forbidden);
            }

            return Results.Accepted(
                $"/openapi/v1/jobs/JOB-{DateTime.UtcNow:yyyyMMddHHmmss}",
                new AcceptedJobResponse(
                    $"JOB-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    Guid.NewGuid(),
                    "Queued"));
        })
        .RequireAuthorization();

        return endpoints;
    }

    private static bool TryReadBasicCredentials(HttpContext httpContext, out string appId, out string clientSecret)
    {
        appId = "";
        clientSecret = "";
        var authorization = httpContext.Request.Headers.Authorization.ToString();

        if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
            !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        appId = decoded[..separator];
        clientSecret = decoded[(separator + 1)..];
        return !string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(clientSecret);
    }

    private static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        var scopes = user.FindFirstValue("scope") ?? "";
        return scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(requiredScope, StringComparer.Ordinal);
    }
}
