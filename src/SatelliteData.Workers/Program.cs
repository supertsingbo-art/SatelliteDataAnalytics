using Microsoft.Extensions.Hosting;
using SatelliteData.Application;
using SatelliteData.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
