using Microsoft.AspNetCore.Authentication;
using SatelliteData.Api.Controllers;
using SatelliteData.Api.Middlewares;
using SatelliteData.Application;
using SatelliteData.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services
    .AddAuthentication("Bearer")
    .AddScheme<AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>("Bearer", _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "SatelliteData.Backend",
    status = "Running",
    version = "0.1.0"
}));
app.MapOAuthEndpoints();
app.MapAssetEndpoints();

app.Run();
