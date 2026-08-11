using System.Numerics;
using System.Text.Json.Nodes;

namespace Luxel.NodeGraph;

/// <summary>
/// <see cref="NodeGraphDoc"/> ⇄ JSON の往復 (ADR-0010 の serialize 契約)。ノード
/// (id/kind/title/pos/collapsed/ports) と辺を保存する。<see cref="GraphNode.Data"/> は
/// **文字列のときだけ**保存する — 任意オブジェクトはドメイン (INodeCatalog) が
/// kind から復元する責務。
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
                    (string)p["type"]!, (string)p["label"]!, (bool)(p["multi"] ?? false)));
            }
            nodes.Add(new GraphNode((int)n["id"]!, (string)n["kind"]!, (string)n["title"]!,
                new Vector2((float)n["x"]!, (float)n["y"]!), ports,
                Data: (string?)n["data"], Collapsed: (bool)(n["collapsed"] ?? false)));
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
