using System.Numerics;

namespace Luxel.NodeGraph;

/// <summary>
/// 状態遷移を組み立てる純関数群 ([[Luxel.Document.EditCommands]] 相当) — <c>(NodeGraphState) → GraphTransaction</c>。
/// view はヒット/幾何を解決してからこれらを呼ぶ (幾何依存の box 選択などは view が id を集めて <see cref="SelectNodes"/> に渡す)。
/// </summary>
public static class GraphCommands
{
    /// <summary>ノードを 1 個追加し、それを選択する。</summary>
    public static GraphTransaction AddNode(NodeGraphState s, GraphNode node)
        => s.Update(new GraphTransactionSpec { Changes = [new AddNode(node)], Selection = GraphSelection.Node(node.Id) });

    /// <summary>選択中のノード + 辺を削除する (ノード削除で接続辺も掃除される)。1 トランザクション = 1 undo。</summary>
    public static GraphTransaction DeleteSelection(NodeGraphState s)
    {
        var changes = new List<GraphChange>();
        foreach (int edgeId in s.Selection.Edges) changes.Add(new Disconnect(edgeId));
        foreach (int nodeId in s.Selection.Nodes) changes.Add(new RemoveNode(nodeId));
        return s.Update(new GraphTransactionSpec { Changes = changes, Selection = GraphSelection.Empty });
    }

    /// <summary>複数ノードをまとめて相対移動する (1 トランザクション = 1 undo)。</summary>
    public static GraphTransaction MoveNodes(NodeGraphState s, IEnumerable<int> ids, Vector2 delta)
        => s.Update(new GraphTransactionSpec { Changes = ids.Select(id => (GraphChange)new MoveNode(id, delta)).ToList() });

    /// <summary>選択を差し替える (文書は変えない)。</summary>
    public static GraphTransaction Select(NodeGraphState s, GraphSelection selection)
        => s.Update(new GraphTransactionSpec { Selection = selection });

    /// <summary>指定ノード群を選択する。</summary>
    public static GraphTransaction SelectNodes(NodeGraphState s, IEnumerable<int> ids, int main = -1)
        => Select(s, GraphSelection.Of(ids, null, main));

    /// <summary>全ノードを選択する。</summary>
    public static GraphTransaction SelectAll(NodeGraphState s)
        => Select(s, GraphSelection.Of(s.Doc.Nodes.Select(n => n.Id)));

    /// <summary>選択を解除する。</summary>
    public static GraphTransaction SelectNone(NodeGraphState s) => Select(s, GraphSelection.Empty);

    /// <summary>辺を 1 本張って選択する (端点の存在/向きは Doc が検証)。</summary>
    public static GraphTransaction Connect(NodeGraphState s, GraphEdge edge)
        => s.Update(new GraphTransactionSpec { Changes = [new Connect(edge)], Selection = GraphSelection.Edge(edge.Id) });

    /// <summary>
    /// 辺の依存 (出力→入力) に沿ってノードを左→右のランクに整列する ([[Luxel.Diagram.DiagramLayout]] と同じ
    /// 最長経路ランキングを辺モデルで自前実装 — core を依存ゼロに保つ)。サイズは <paramref name="measure"/> で外注。
    /// 全ノードを 1 トランザクションで移動 (= 1 undo)。循環は反復回数を辺数で打ち切って安全に扱う。
    /// </summary>
    public static GraphTransaction AutoLayout(NodeGraphState s, NodeMeasure measure, float gapX = 60f, float gapY = 24f, Vector2? origin = null)
    {
        NodeGraphDoc doc = s.Doc;
        if (doc.Nodes.Count == 0) return s.Update(new GraphTransactionSpec());

        // 最長経路ランク: sources=0、辺ごとに rank[to] = max(rank[to], rank[from]+1)。循環安全に反復を打ち切る。
        var rank = new Dictionary<int, int>(doc.Nodes.Count);
        foreach (GraphNode n in doc.Nodes) rank[n.Id] = 0;
        for (int iter = 0; iter <= doc.Edges.Count; iter++)
        {
            bool changed = false;
            foreach (GraphEdge e in doc.Edges)
            {
                int r = rank[e.From.Node] + 1;
                if (r > rank[e.To.Node]) { rank[e.To.Node] = r; changed = true; }
            }
            if (!changed) break;
        }

        // ランク別にまとめ、各ランク内は id 昇順 (決定的)
        var byRank = new SortedDictionary<int, List<GraphNode>>();
        foreach (GraphNode n in doc.Nodes)
        {
            if (!byRank.TryGetValue(rank[n.Id], out List<GraphNode>? list)) byRank[rank[n.Id]] = list = new();
            list.Add(n);
        }

        Vector2 o = origin ?? new Vector2(40, 40);
        var target = new Dictionary<int, Vector2>(doc.Nodes.Count);
        float x = o.X;
        foreach ((_, List<GraphNode> group) in byRank)
        {
            group.Sort((a, b) => a.Id.CompareTo(b.Id));
            float colW = 0, y = o.Y;
            foreach (GraphNode n in group)
            {
                NodeSize sz = measure(n);
                target[n.Id] = new Vector2(x, y);
                y += sz.Height + gapY;
                colW = MathF.Max(colW, sz.Width);
            }
            x += colW + gapX;
        }

        var changes = doc.Nodes.Select(n => (GraphChange)new MoveNode(n.Id, target[n.Id] - n.Pos)).ToList();
        return s.Update(new GraphTransactionSpec { Changes = changes });
    }
}
