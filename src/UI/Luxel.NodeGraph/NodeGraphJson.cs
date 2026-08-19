using System.Numerics;
using System.Text.Json.Nodes;

namespace Luxel.NodeGraph;

/// <summary>
/// <see cref="NodeGraphDoc"/> ⇄ JSON の往復 (ADR-0010 の serialize 契約)。ノード
/// (id/kind/title/pos/collapsed/ports)、標準 <see cref="NodeParameterValues"/>、辺を保存する。
/// legacy の文字列 <see cref="GraphNode.Data"/> も保存するが、その他の任意オブジェクトは
/// ドメイン (INodeCatalog) が kind から復元する責務。
/// </summary>
public static class NodeGraphJson
{
    public static string Serialize(NodeGraphDoc doc)
    {
        var nodes = new JsonArray();
        foreach (GraphNode n in doc.Nodes)
        {
            var ports = new JsonArray();
            foreach (NodePort p in n.Ports)
                ports.Add(new JsonObject
                {
                    ["id"] = p.Id,
                    ["dir"] = p.Dir == PortDir.Out ? "out" : "in",
                    ["type"] = p.TypeKey,
                    ["label"] = p.Label,
                    ["multi"] = p.Multi,
                });
            var o = new JsonObject
            {
                ["id"] = n.Id,
                ["kind"] = n.Kind,
                ["title"] = n.Title,
                ["x"] = n.Pos.X,
                ["y"] = n.Pos.Y,
                ["collapsed"] = n.Collapsed,
                ["ports"] = ports,
            };
            if (n.Data is string s) o["data"] = s;
            else if (n.Data is NodeParameterValues parameters) o["parameters"] = SerializeParameters(parameters);
            nodes.Add(o);
        }
        var edges = new JsonArray();
        foreach (GraphEdge e in doc.Edges)
            edges.Add(new JsonObject
            {
                ["id"] = e.Id,
                ["from"] = new JsonObject { ["n"] = e.From.Node, ["p"] = e.From.Port },
                ["to"] = new JsonObject { ["n"] = e.To.Node, ["p"] = e.To.Port },
            });
        return new JsonObject { ["nodes"] = nodes, ["edges"] = edges }.ToJsonString();
    }

    private static JsonObject SerializeParameters(NodeParameterValues parameters)
    {
        var result = new JsonObject();
        foreach ((string key, object? value) in parameters.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            (string type, JsonNode? jsonValue) = value switch
            {
                null => ("null", null),
                bool v => ("bool", JsonValue.Create(v)),
                int v => ("int", JsonValue.Create(v)),
                long v => ("long", JsonValue.Create(v)),
                float v => ("float", JsonValue.Create(v)),
                double v => ("double", JsonValue.Create(v)),
                decimal v => ("decimal", JsonValue.Create(v)),
                string v => ("string", JsonValue.Create(v)),
                _ => throw new NotSupportedException($"Node parameter '{key}' has unsupported JSON type {value.GetType().Name}.")
            };
            result[key] = new JsonObject { ["type"] = type, ["value"] = jsonValue };
        }
        return result;
    }

    private static NodeParameterValues DeserializeParameters(JsonObject parameters)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string key, JsonNode? node) in parameters)
        {
            var entry = node as JsonObject ?? throw new FormatException($"Node parameter '{key}' must be an object.");
            string type = (string?)entry["type"] ?? throw new FormatException($"Node parameter '{key}' has no type.");
            JsonNode? value = entry["value"];
            values[key] = type switch
            {
                "null" => null,
                "bool" => value!.GetValue<bool>(),
                "int" => value!.GetValue<int>(),
                "long" => value!.GetValue<long>(),
                "float" => value!.GetValue<float>(),
                "double" => value!.GetValue<double>(),
                "decimal" => value!.GetValue<decimal>(),
                "string" => value!.GetValue<string>(),
                _ => throw new FormatException($"Node parameter '{key}' has unknown type '{type}'.")
            };
        }
        return new NodeParameterValues(values);
    }

    public static NodeGraphDoc Deserialize(string json)
    {
        var o = (JsonObject)JsonNode.Parse(json)!;
        var nodes = new List<GraphNode>();
        foreach (JsonNode? nn in (JsonArray)o["nodes"]!)
        {
            var n = (JsonObject)nn!;
            var ports = new List<NodePort>();
            foreach (JsonNode? pn in (JsonArray)n["ports"]!)
            {
                var p = (JsonObject)pn!;
                ports.Add(new NodePort((int)p["id"]!, (string)p["dir"]! == "out" ? PortDir.Out : PortDir.In,
                    (string)p["type"]!, (string?)p["label"] ?? "", (bool)(p["multi"] ?? false)));
            }
            object? data = n["parameters"] is JsonObject parameters
                ? DeserializeParameters(parameters)
                : (string?)n["data"];
            nodes.Add(new GraphNode((int)n["id"]!, (string)n["kind"]!, (string)n["title"]!,
                new Vector2((float)n["x"]!, (float)n["y"]!), ports,
                Data: data, Collapsed: (bool)(n["collapsed"] ?? false)));
        }
        var edges = new List<GraphEdge>();
        foreach (JsonNode? en in (JsonArray)o["edges"]!)
        {
            var e = (JsonObject)en!;
            var from = (JsonObject)e["from"]!;
            var to = (JsonObject)e["to"]!;
            edges.Add(new GraphEdge((int)e["id"]!,
                new PortId((int)from["n"]!, (int)from["p"]!),
                new PortId((int)to["n"]!, (int)to["p"]!)));
        }
        return NodeGraphDoc.Of(nodes, edges);
    }
}
