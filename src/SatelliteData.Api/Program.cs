using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using SatelliteData.Api.Controllers;
using SatelliteData.Api.Middlewares;
using SatelliteData.Application;
using SatelliteData.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Satellite Data Analytics API",
        Version = "v1",
        Description = "卫星测试数据预处理与数据分析平台后端接口"
    });
});

//fqb temp, 不用授权
//builder.Services
//    .AddAuthentication("Bearer")
//    .AddScheme<AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>("Bearer", _ => { });
//builder.Services.AddAuthorization();

var app = builder.Build();

await DatabaseInitializer.EnsurePostgresDatabaseAsync(
    builder.Configuration,
    app.Services.GetRequiredService<ILogger<Program>>(),
    default).ConfigureAwait(false);

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Satellite Data Analytics API v1");
    options.RoutePrefix = string.Empty;
    options.DocumentTitle = "Satellite Data Analytics API";
});

app.UseAuthentication();
app.UseAuthorization();
app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new LocalRequestsOnlyAuthorizationFilter()] });

app.MapGet("/health/live", () => Results.Ok(new
{
    service = "SatelliteData.Backend",
    status = "Running",
    version = "0.1.0"
}));
app.MapOAuthEndpoints();
app.MapControllers();

app.Run();
