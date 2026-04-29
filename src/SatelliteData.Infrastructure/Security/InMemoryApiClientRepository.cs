using Microsoft.Extensions.Options;
using SatelliteData.Application.Identity;
using SatelliteData.Domain.Common;

namespace SatelliteData.Infrastructure.Security;

public sealed class InMemoryApiClientRepository : IApiClientRepository
{
    private readonly List<ApiClient> _clients = [];
    private readonly List<OAuthScope> _scopes = [];
    private readonly Dictionary<Guid, List<Guid>> _clientScopes = [];
    private readonly List<ApiClientDataScope> _dataScopes = [];
    private readonly List<OAuthTokenLog> _tokenLogs = [];

    public InMemoryApiClientRepository(IOptions<OAuthInfrastructureOptions> options, ISecretHasher secretHasher)
    {
        SeedScopes();
        SeedClients(options.Value, secretHasher);
    }

    public Task<ApiClient?> FindByAppIdAsync(string appId, CancellationToken cancellationToken)
    {
        var client = _clients.SingleOrDefault(item => string.Equals(item.AppId, appId, StringComparison.Ordinal));
        return Task.FromResult(client);
    }

    public Task<ApiClient?> FindByClientIdAsync(Guid clientId, CancellationToken cancellationToken)
    {
        var client = _clients.SingleOrDefault(item => item.ClientId == clientId);
        return Task.FromResult(client);
    }

    public Task<IReadOnlyCollection<OAuthScope>> GetClientOAuthScopesAsync(Guid clientId, CancellationToken cancellationToken)
    {
        if (!_clientScopes.TryGetValue(clientId, out var scopeIds))
        {
            return Task.FromResult<IReadOnlyCollection<OAuthScope>>(Array.Empty<OAuthScope>());
        }

        var scopes = _scopes.Where(scope => scopeIds.Contains(scope.ScopeId)).ToArray();
        return Task.FromResult<IReadOnlyCollection<OAuthScope>>(scopes);
    }

    public Task<IReadOnlyCollection<ApiClientDataScope>> GetClientDataScopesAsync(Guid clientId, CancellationToken cancellationToken)
    {
        var scopes = _dataScopes.Where(scope => scope.ClientId == clientId).ToArray();
        return Task.FromResult<IReadOnlyCollection<ApiClientDataScope>>(scopes);
    }

    public Task AddTokenLogAsync(OAuthTokenLog log, CancellationToken cancellationToken)
    {
        _tokenLogs.Add(log);
        return Task.CompletedTask;
    }

    private void SeedScopes()
    {
        AddScope("job:create", "创建任务", "创建预处理或全链路任务");
        AddScope("job:read", "查询任务", "查询任务状态和结果摘要");
        AddScope("data:read", "查询结果数据", "查询高品质明细、算法结果、比对结果");
        AddScope("report:download", "下载报告", "获取报告下载地址");
        AddScope("webhook:manage", "管理回调", "登记、测试、启停 Webhook");
    }

    private void SeedClients(OAuthInfrastructureOptions options, ISecretHasher secretHasher)
    {
        var seedClients = options.SeedClients.Count > 0
            ? options.SeedClients
            : DefaultSeedClients();

        foreach (var seed in seedClients)
        {
            var clientId = Guid.NewGuid();
            var client = new ApiClient(
                clientId,
                seed.ClientName,
                seed.AppId,
                secretHasher.Hash(seed.AppSecret),
                ["client_credentials"],
                seed.TokenTtlSeconds ?? options.DefaultTokenTtlSeconds,
                true,
                seed.IpAllowlist);

            _clients.Add(client);
            _clientScopes[clientId] = _scopes
                .Where(scope => seed.Scopes.Contains(scope.ScopeCode, StringComparer.Ordinal))
                .Select(scope => scope.ScopeId)
                .ToList();

            foreach (var dataScope in seed.DataScopes)
            {
                _dataScopes.Add(new ApiClientDataScope(
                    Guid.NewGuid(),
                    clientId,
                    dataScope.TasookNo,
                    dataScope.SatelliteNo,
                    dataScope.TestBatchId,
                    dataScope.ScopeLevel,
                    true));
            }
        }
    }

    private void AddScope(string code, string name, string description)
    {
        _scopes.Add(new OAuthScope(Guid.NewGuid(), code, name, description, true));
    }

    private static List<SeedApiClientOptions> DefaultSeedClients()
    {
        return
        [
            new()
            {
                ClientName = "型号测试数据总管平台",
                AppId = "demo-client",
                AppSecret = "demo-secret",
                Scopes = ["job:create", "job:read", "data:read", "report:download", "webhook:manage"],
                DataScopes =
                [
                    new()
                    {
                        TasookNo = "TASK-A100",
                        SatelliteNo = "SAT-001",
                        ScopeLevel = "SATELLITE"
                    },
                    new()
                    {
                        TasookNo = "TASK-A100",
                        SatelliteNo = "SAT-002",
                        TestBatchId = "Bat-2604",
                        ScopeLevel = "TEST_BATCH"
                    }
                ]
            }
        ];
    }
}
