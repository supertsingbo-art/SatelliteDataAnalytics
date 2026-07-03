using System.Net;
using System.Text;
using System.Text.Json;
using SatelliteData.Application.Assets;
using SatelliteData.Domain.Assets;
using SatelliteData.Infrastructure.HttpClients;
using Xunit;

namespace SatelliteData.UnitTests;

public class MassDataApiClientTests
{
    private const string MassApiBaseUrl = "http://mass.test/";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<(HttpStatusCode, string)>> _respond;

        public StubHandler(Func<HttpRequestMessage, Task<(HttpStatusCode, string)>> respond)
        {
            _respond = respond;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = await _respond(request);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private static MassDataApiClient CreateClient(Func<HttpRequestMessage, Task<(HttpStatusCode, string)>> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri(MassApiBaseUrl) };
        return new MassDataApiClient(http);
    }

    private static Task<(HttpStatusCode, string)> Ok(string body) =>
        Task.FromResult((HttpStatusCode.OK, body));

    [Fact]
    public async Task GetSatellitesAsync_parses_datas_and_enabled()
    {
        var client = CreateClient(_ => Ok("""
            {
              "datas": [
                { "taskNo": "TASK_A", "taskName": "任务A", "satNo": "SAT_01", "satName": "卫星01", "enabled": false },
                { "taskNo": "TASK_A", "taskName": "任务A", "satNo": "SAT_02", "satName": "卫星02", "enabled": true }
              ]
            }
            """));

        var sats = await client.GetSatellitesAsync(default);

        Assert.Equal(2, sats.Count);
        var first = sats.Single(s => s.SatelliteNo == "SAT_01");
        Assert.Equal("TASK_A", first.TasookNo);
        Assert.Equal("任务A", first.TasookName);
        Assert.Equal("卫星01", first.SatelliteName);
        Assert.False(first.IsEnabled);
        Assert.True(sats.Single(s => s.SatelliteNo == "SAT_02").IsEnabled);
    }

    [Fact]
    public async Task GetParametersAsync_request_body_excludes_dbstage()
    {
        string? capturedBody = null;
        var client = CreateClient(async req =>
        {
            capturedBody = await req.Content!.ReadAsStringAsync();
            return await Ok("""
            {
              "datas": [
                { "paraId": 100108, "prmSysId": 1, "paraCode": "P001", "paraDesc": "温度", "paraTypeDesc": "浮点", "minValue": 0, "maxValue": 100 }
              ]
            }
            """);
        });

        var parameters = await client.GetParametersAsync("TASK_A", "SAT_01", default);

        Assert.NotNull(capturedBody);
        using var doc = JsonDocument.Parse(capturedBody!);
        Assert.True(doc.RootElement.TryGetProperty("taskNo", out var tn));
        Assert.Equal("TASK_A", tn.GetString());
        Assert.True(doc.RootElement.TryGetProperty("satNo", out var sn));
        Assert.Equal("SAT_01", sn.GetString());
        Assert.False(doc.RootElement.TryGetProperty("dbStage", out _));

        var param = Assert.Single(parameters);
        Assert.Equal(100108, param.ParaId);
        Assert.Equal("P001", param.ParaCode);
        Assert.Equal("温度", param.ParaDesc);
    }

    [Fact]
    public async Task GetCommandsAsync_parses_command_datas()
    {
        var client = CreateClient(_ => Ok("""
            {
              "datas": [
                { "cmdId": 101, "cmdSysId": 1, "cmdCode": "CMD01", "cmdDesc": "指令", "cmdType": 1, "cmdLen": 16, "validFlag": 1 }
              ]
            }
            """));

        var commands = await client.GetCommandsAsync("TASK_A", "SAT_01", default);

        var cmd = Assert.Single(commands);
        Assert.Equal(101, cmd.CmdId);
        Assert.Equal("CMD01", cmd.CmdCode);
        Assert.Equal("指令", cmd.CmdDesc);
    }

    [Fact]
    public async Task GetMongoInfoAsync_prefers_mongoQueryConn_over_basicConn()
    {
        var client = CreateClient(_ => Ok("""
            {
              "basicConn": "mongodb://basic:27017/basicdb",
              "mongoQueryConn": "mongodb://user:pass@query:27017/querydb",
              "cfgConn": "mongodb://cfg:27017/cfgdb"
            }
            """));

        var info = await client.GetMongoInfoAsync("TASK_A", "SAT_01", default);

        Assert.NotNull(info);
        Assert.Equal("mongodb://query:27017/querydb", info!.MongoUri);
        Assert.Equal("querydb", info.DbName);
    }

    [Fact]
    public async Task GetMongoInfoAsync_falls_back_to_basicConn_when_mongoQueryConn_missing()
    {
        var client = CreateClient(_ => Ok("""
            {
              "basicConn": "mongodb://basic:27017/basicdb"
            }
            """));

        var info = await client.GetMongoInfoAsync("TASK_A", "SAT_01", default);

        Assert.NotNull(info);
        Assert.Equal("mongodb://basic:27017/basicdb", info!.MongoUri);
        Assert.Equal("basicdb", info.DbName);
    }

    [Fact]
    public async Task GetMongoInfoAsync_returns_null_when_no_connection_field()
    {
        var client = CreateClient(_ => Ok("""
            { "judgeConn": "mongodb://judge:27017", "mqttUrl": "mqtt://broker" }
            """));

        var info = await client.GetMongoInfoAsync("TASK_A", "SAT_01", default);

        Assert.Null(info);
    }
}
