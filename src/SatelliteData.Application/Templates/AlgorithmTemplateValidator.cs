using System.Text.Json;
using SatelliteData.Domain.Templates;

namespace SatelliteData.Application.Templates;

/// <summary>
/// 算法模板 DAG 校验器。实现 6.2.4.5 / 6.5.3 的全部静态校验规则：
/// 节点至少 1 个、≥1 个数据输入节点、≥1 个数据输出节点、无环、运行时白名单、引用算法包必须 Published。
/// </summary>
public sealed class AlgorithmTemplateValidator(AlgorithmRegistryService registryService)
{
    public async Task<AlgorithmTemplateValidationResult> ValidateAsync(
        JsonElement reactFlowJson,
        JsonElement configJson,
        CancellationToken cancellationToken)
    {
        var issues = new List<AlgorithmTemplateValidationIssue>();

        var nodes = ExtractNodes(reactFlowJson);
        var edges = ExtractEdges(reactFlowJson);
        var nodeCount = nodes.Count;
        var edgeCount = edges.Count;

        // 规则 1：节点至少 1 个
        if (nodeCount == 0)
        {
            issues.Add(new("DAG_001", "DAG 至少需要 1 个节点", null));
            return new(false, 0, 0, issues);
        }

        // 规则 2：≥1 个数据输入节点（category='source'）
        var sourceNodes = nodes.Where(n => string.Equals(n.Category, "source", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sourceNodes.Length == 0)
        {
            issues.Add(new("DAG_002", "DAG 必须至少包含 1 个数据输入节点（category='source'）", null));
        }

        // 规则 2b：每个数据输入节点必须配置恰好 1 个 paramId
        foreach (var source in sourceNodes)
        {
            if (!AlgorithmTemplateConfigParser.TryValidateSourceParam(source.Data, source.Id, configJson, out var failure))
            {
                issues.Add(failure switch
                {
                    SourceParamResolveFailure.Multiple => new(
                        "DAG_011",
                        $"数据输入节点 {source.Id} 只能配置 1 个参数",
                        source.Id),
                    _ => new(
                        "DAG_010",
                        $"数据输入节点 {source.Id} 未配置参数",
                        source.Id)
                });
            }
        }

        // 规则 3：≥1 个数据输出节点（category='dataoutput' 或 algorithmCode='save_result'）
        var dataOutputNodes = nodes.Where(n =>
            string.Equals(n.Category, "dataoutput", StringComparison.OrdinalIgnoreCase)
            || string.Equals(n.AlgorithmCode, "save_result", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (dataOutputNodes.Length == 0)
        {
            issues.Add(new(
                "DAG_009",
                "DAG 必须至少包含 1 个数据输出节点（结果落库 save_result，category='dataoutput'）",
                null));
        }

        // 规则 3b：save_result 节点必须有且仅有 1 条入边
        foreach (var sink in nodes.Where(n =>
                     string.Equals(n.AlgorithmCode, "save_result", StringComparison.OrdinalIgnoreCase)))
        {
            var inCount = edges.Count(e => string.Equals(e.Target, sink.Id, StringComparison.Ordinal));
            if (inCount != 1)
            {
                issues.Add(new(
                    "DAG_008",
                    $"结果落库节点 {sink.Id} 必须有且仅有 1 条入边（当前 {inCount} 条）",
                    sink.Id));
            }
        }

        // 规则 4：无环
        if (HasCycle(nodes, edges, out var cycleNodeId))
        {
            issues.Add(new("DAG_004", "DAG 存在环路", cycleNodeId));
        }

        // 规则 5：边的两端节点必须存在
        var nodeIds = nodes.Select(n => n.Id).ToHashSet();
        foreach (var edge in edges)
        {
            if (!nodeIds.Contains(edge.Source) || !nodeIds.Contains(edge.Target))
            {
                issues.Add(new("DAG_005", $"边 {edge.Id} 的端点引用了不存在的节点", edge.Id));
            }
        }

        // 规则 6：运行时白名单 + 算法包必须 Published（数据输入 source 节点除外）
        foreach (var node in nodes)
        {
            if (string.Equals(node.Category, "source", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrEmpty(node.AlgorithmCode))
            {
                continue; // 由 config_json 解析阶段补充
            }

            var runtime = node.Runtime?.ToUpperInvariant();
            if (runtime is not null && runtime is not ("BUILTIN" or "PYTHON" or "JS"))
            {
                issues.Add(new("DAG_006", $"节点 {node.Id} 的 runtime '{node.Runtime}' 不在白名单 [BUILTIN/PYTHON/JS]", node.Id));
                continue;
            }

            var published = await registryService.IsPublishedAsync(node.AlgorithmCode, cancellationToken);
            if (!published)
            {
                issues.Add(new("DAG_007", $"节点 {node.Id} 引用的算法 '{node.AlgorithmCode}' 没有已发布的算法包", node.Id));
            }
        }

        return new AlgorithmTemplateValidationResult(issues.Count == 0, nodeCount, edgeCount, issues);
    }

    private static IReadOnlyList<NodeRef> ExtractNodes(JsonElement reactFlowJson)
    {
        if (reactFlowJson.ValueKind != JsonValueKind.Object) return Array.Empty<NodeRef>();
        if (!reactFlowJson.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<NodeRef>();
        }

        var result = new List<NodeRef>();
        foreach (var node in nodes.EnumerateArray())
        {
            var id = node.TryGetProperty("id", out var idNode) && idNode.ValueKind == JsonValueKind.String ? idNode.GetString() ?? "" : "";
            var category = node.TryGetProperty("type", out var typeNode) && typeNode.ValueKind == JsonValueKind.String ? typeNode.GetString() : null;

            string? algoCode = null;
            string? runtime = null;
            JsonElement data = default;
            if (node.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            {
                data = dataEl;
                if (data.TryGetProperty("algorithmCode", out var ac) && ac.ValueKind == JsonValueKind.String)
                {
                    algoCode = ac.GetString();
                }
                if (data.TryGetProperty("runtime", out var rt) && rt.ValueKind == JsonValueKind.String)
                {
                    runtime = rt.GetString();
                }
            }

            result.Add(new NodeRef(id, category ?? "", algoCode, runtime, data));
        }
        return result;
    }

    private static IReadOnlyList<EdgeRef> ExtractEdges(JsonElement reactFlowJson)
    {
        if (reactFlowJson.ValueKind != JsonValueKind.Object) return Array.Empty<EdgeRef>();
        if (!reactFlowJson.TryGetProperty("edges", out var edges) || edges.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<EdgeRef>();
        }

        var result = new List<EdgeRef>();
        foreach (var edge in edges.EnumerateArray())
        {
            var id = edge.TryGetProperty("id", out var idNode) && idNode.ValueKind == JsonValueKind.String ? idNode.GetString() ?? "" : "";
            var source = edge.TryGetProperty("source", out var src) && src.ValueKind == JsonValueKind.String ? src.GetString() ?? "" : "";
            var target = edge.TryGetProperty("target", out var tgt) && tgt.ValueKind == JsonValueKind.String ? tgt.GetString() ?? "" : "";
            result.Add(new EdgeRef(id, source, target));
        }
        return result;
    }

    private static bool HasCycle(IReadOnlyList<NodeRef> nodes, IReadOnlyList<EdgeRef> edges, out string? cycleNodeId)
    {
        var graph = nodes.ToDictionary(n => n.Id, _ => new List<string>());
        foreach (var edge in edges)
        {
            if (graph.TryGetValue(edge.Source, out var children) && graph.ContainsKey(edge.Target))
            {
                children.Add(edge.Target);
            }
        }

        var visiting = new HashSet<string>();
        var visited = new HashSet<string>();

        foreach (var node in nodes)
        {
            if (visited.Contains(node.Id)) continue;
            if (Dfs(node.Id, graph, visiting, visited, out var hit))
            {
                cycleNodeId = hit;
                return true;
            }
        }

        cycleNodeId = null;
        return false;
    }

    private static bool Dfs(
        string nodeId,
        Dictionary<string, List<string>> graph,
        HashSet<string> visiting,
        HashSet<string> visited,
        out string? cycleNodeId)
    {
        visiting.Add(nodeId);
        foreach (var next in graph.GetValueOrDefault(nodeId) ?? new List<string>())
        {
            if (visiting.Contains(next))
            {
                cycleNodeId = next;
                return true;
            }
            if (!visited.Contains(next) && Dfs(next, graph, visiting, visited, out cycleNodeId))
            {
                return true;
            }
        }
        visiting.Remove(nodeId);
        visited.Add(nodeId);
        cycleNodeId = null;
        return false;
    }

    private sealed record NodeRef(string Id, string Category, string? AlgorithmCode, string? Runtime, JsonElement Data);

    private sealed record EdgeRef(string Id, string Source, string Target);
}
