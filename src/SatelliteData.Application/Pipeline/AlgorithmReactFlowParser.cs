using System.Text.Json;

namespace SatelliteData.Application.Pipeline;

public sealed record FlowNodeRef(string Id, string Type, string? AlgorithmCode, string? Runtime, JsonElement Data);

public sealed record FlowEdgeRef(string Id, string Source, string Target);

public static class AlgorithmReactFlowParser
{
    public static IReadOnlyList<FlowNodeRef> ParseNodes(JsonElement reactFlowJson)
    {
        if (reactFlowJson.ValueKind != JsonValueKind.Object
            || !reactFlowJson.TryGetProperty("nodes", out var nodes)
            || nodes.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<FlowNodeRef>();
        }

        var list = new List<FlowNodeRef>();
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object) continue;
            var id = ReadString(node, "id");
            var type = ReadString(node, "type");
            JsonElement data = default;
            _ = node.TryGetProperty("data", out data);
            string? algo = null;
            string? runtime = null;
            if (data.ValueKind == JsonValueKind.Object)
            {
                algo = ReadString(data, "algorithmCode");
                runtime = ReadString(data, "runtime");
            }

            list.Add(new FlowNodeRef(id, type, algo, runtime, data.ValueKind == JsonValueKind.Object ? data : default));
        }

        return list;
    }

    public static IReadOnlyList<FlowEdgeRef> ParseEdges(JsonElement reactFlowJson)
    {
        if (reactFlowJson.ValueKind != JsonValueKind.Object
            || !reactFlowJson.TryGetProperty("edges", out var edges)
            || edges.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<FlowEdgeRef>();
        }

        var list = new List<FlowEdgeRef>();
        foreach (var edge in edges.EnumerateArray())
        {
            if (edge.ValueKind != JsonValueKind.Object) continue;
            list.Add(new FlowEdgeRef(
                ReadString(edge, "id"),
                ReadString(edge, "source"),
                ReadString(edge, "target")));
        }

        return list;
    }

    public static IReadOnlyList<string> TopologicalSort(IReadOnlyList<FlowNodeRef> nodes, IReadOnlyList<FlowEdgeRef> edges)
    {
        var ids = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var adj = ids.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        var indeg = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var e in edges)
        {
            if (!ids.Contains(e.Source) || !ids.Contains(e.Target)) continue;
            adj[e.Source].Add(e.Target);
            indeg[e.Target]++;
        }

        var q = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<string>();
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            order.Add(u);
            foreach (var v in adj[u])
            {
                indeg[v]--;
                if (indeg[v] == 0) q.Enqueue(v);
            }
        }

        if (order.Count != ids.Count)
        {
            throw new InvalidOperationException("DAG 存在环路，无法拓扑排序");
        }

        return order;
    }

    private static string ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) return "";
        return p.GetString() ?? "";
    }
}
