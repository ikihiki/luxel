using System.Numerics;

namespace Luxel.NodeGraph;

/// <summary>
/// 状態遷移を組み立てる純関数群 ([[Luxel.Editor.EditCommands]] 相当) — <c>(NodeGraphState) → GraphTransaction</c>。
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
}
