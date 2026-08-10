using System.Numerics;
using Luxel.Diagnostics;
using Luxel.NodeGraph;

namespace Luxel.Controls;

/// <summary>
/// レンダーグラフの診断 (<see cref="DiagRenderGraph"/>) を <see cref="NodeGraphDoc"/> に変換する橋 —
/// **パス = ノード**、**リソース依存 = 辺** (書き手パスの出力ポート → 読み手パスの入力ポート)。DevTools の
/// レンダーグラフ可視化を [読み取り専用の] <see cref="NodeGraphView"/> で描くために使う。<see cref="GraphCommands.AutoLayout"/>
/// で辺依存に沿って左→右に整列済みの doc を返す。
/// </summary>
public static class RenderGraphNodes
{
    // 入力ポート id = リソース id、出力ポート id = リソース id + Offset (同一パスが同一リソースを読み書きしても衝突しない)
    private const int OutPortOffset = 1_000_000;

    /// <summary>診断からノードグラフ文書を組み立てる (整列済み)。</summary>
    public static NodeGraphDoc Build(DiagRenderGraph rg)
    {
        var resName = new Dictionary<int, string>();
        foreach (DiagRenderGraphResource r in rg.Resources) resName[r.Id] = r.Name;
        string Name(int rid) => resName.TryGetValue(rid, out string? n) ? n : $"#{rid}";

        var nodes = new List<GraphNode>(rg.Passes.Length);
        foreach (DiagRenderGraphPass p in rg.Passes)
        {
            var ports = new List<NodePort>();
            foreach (int rid in p.Reads.Distinct()) ports.Add(new NodePort(rid, PortDir.In, "res", Name(rid)));
            foreach (int rid in p.Writes.Distinct()) ports.Add(new NodePort(rid + OutPortOffset, PortDir.Out, "res", Name(rid)));
            string title = p.Culled ? $"{p.Name} (culled)" : p.Name;
            nodes.Add(new GraphNode(p.Index, p.Queue, title, Vector2.Zero, ports));
        }

        // リソースごとに 書き手パス.out(res) → 読み手パス.in(res) を張る
        var edges = new List<GraphEdge>();
        int eid = 1;
        foreach (DiagRenderGraphResource r in rg.Resources)
        {
            foreach (DiagRenderGraphPass w in rg.Passes)
            {
                if (!w.Writes.Contains(r.Id)) continue;
                foreach (DiagRenderGraphPass rd in rg.Passes)
                {
                    if (rd.Index == w.Index || !rd.Reads.Contains(r.Id)) continue;
                    edges.Add(new GraphEdge(eid++, new PortId(w.Index, r.Id + OutPortOffset), new PortId(rd.Index, r.Id)));
                }
            }
        }

        var state = NodeGraphState.Create(NodeGraphDoc.Of(nodes, edges));
        return GraphCommands.AutoLayout(state, EstimateSize, gapX: 70f, gapY: 28f).State.Doc;
    }

    // view の実測 (フォント依存) 無しで整列するための概算サイズ (NodeGraphView.MeasureNode と同じ式)
    private static NodeSize EstimateSize(GraphNode n)
    {
        int inN = 0, outN = 0;
        foreach (NodePort p in n.Ports) { if (p.Dir == PortDir.In) inN++; else outN++; }
        float w = MathF.Max(140, n.Title.Length * 8 + 28);
        float h = 22 + 4 + Math.Max(1, Math.Max(inN, outN)) * 18 + 6;
        return new NodeSize(w, h);
    }
}
