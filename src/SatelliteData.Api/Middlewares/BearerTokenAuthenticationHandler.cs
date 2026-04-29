using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using SatelliteData.Application.Identity;

namespace SatelliteData.Api.Middlewares;

public sealed class BearerTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IAccessTokenValidator tokenValidator,
    IApiClientRepository clients)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization["Bearer ".Length..].Trim();
        var validation = tokenValidator.Validate(token, DateTimeOffset.UtcNow);
        if (!validation.Succeeded || validation.Principal is null)
        {
            return AuthenticateResult.Fail(validation.ErrorDescription ?? "invalid token");
        }

        var clientIdText = validation.Principal.FindFirstValue("client_id");
        if (!Guid.TryParse(clientIdText, out var clientId))
        {
            return AuthenticateResult.Fail("client_id claim is invalid");
        }

        var client = await clients.FindByClientIdAsync(clientId, Context.RequestAborted);
        if (client is null || !client.Enabled)
        {
            return AuthenticateResult.Fail("client is disabled or not found");
        }

        var remoteIp = Context.Connection.RemoteIpAddress?.ToString();
        if (client.IpAllowlist.Count > 0 && (remoteIp is null || !client.IpAllowlist.Contains(remoteIp)))
        {
            return AuthenticateResult.Fail("client ip is not allowed");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(validation.Principal, Scheme.Name));
    }
}
