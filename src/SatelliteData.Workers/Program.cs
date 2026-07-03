using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SatelliteData.Application;
using SatelliteData.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var host = builder.Build();

await DatabaseInitializer.EnsurePostgresDatabaseAsync(
    builder.Configuration,
    host.Services.GetRequiredService<ILogger<Program>>(),
    default).ConfigureAwait(false);

await host.RunAsync().ConfigureAwait(false);
