using System.Numerics;

namespace Luxel.NodeGraph;

/// <summary>
/// グラフへの 1 個の変更 — [[Luxel.Document.ChangeSpec]] 相当のアトム。<see cref="Apply"/> が新 Doc を返し、
/// <see cref="InvertAgainst"/> が**適用直前**の Doc に対する逆変更 (複数になりうる) を返す。
/// 安定 id 前提なので座標写像は不要。逆変更が複数になる例: <see cref="RemoveNode"/> の逆は
/// 「ノード復活 + 接続していた辺の再接続」。
/// </summary>
public abstract record GraphChange
{
    /// <summary>この変更を適用した新しい Doc。</summary>
    public abstract NodeGraphDoc Apply(NodeGraphDoc doc);

    /// <summary>この変更を打ち消す逆変更列 (<paramref name="doc"/> = 適用直前の Doc)。</summary>
    public abstract IReadOnlyList<GraphChange> InvertAgainst(NodeGraphDoc doc);
}

/// <summary>ノードを 1 個追加する。</summary>
public sealed record AddNode(GraphNode Node) : GraphChange
{
    public override NodeGraphDoc Apply(NodeGraphDoc doc) => doc.AddNode(Node);
    public override IReadOnlyList<GraphChange> InvertAgainst(NodeGraphDoc doc) => [new RemoveNode(Node.Id)];
}

/// <summary>ノードを 1 個削除する (接続していた辺も一緒に消える)。</summary>
public sealed record RemoveNode(int NodeId) : GraphChange
{
    public override NodeGraphDoc Apply(NodeGraphDoc doc) => doc.RemoveNode(NodeId);
    public override IReadOnlyList<GraphChange> InvertAgainst(NodeGraphDoc doc)
    {
        // 逆 = ノードを戻し、接続していた辺を張り直す (ノードを先に)。
        var inv = new List<GraphChange> { new AddNode(doc.Node(NodeId)) };
        foreach (GraphEdge e in doc.EdgesOf(NodeId)) inv.Add(new Connect(e));
        return inv;
    }
}

/// <summary>ノードを相対移動する (<see cref="Delta"/> を Pos に加算)。</summary>
public sealed record MoveNode(int NodeId, Vector2 Delta) : GraphChange
{
    public override NodeGraphDoc Apply(NodeGraphDoc doc)
    {
        GraphNode n = doc.Node(NodeId);
        return doc.ReplaceNode(n with { Pos = n.Pos + Delta });
    }
    public override IReadOnlyList<GraphChange> InvertAgainst(NodeGraphDoc doc) => [new MoveNode(NodeId, -Delta)];
}

/// <summary>ノードの不透明ペイロード (<see cref="GraphNode.Data"/>) を差し替える。</summary>
public sealed record SetNodeData(int NodeId, object? Data) : GraphChange
{
    public override NodeGraphDoc Apply(NodeGraphDoc doc) => doc.ReplaceNode(doc.Node(NodeId) with { Data = Data });
    public override IReadOnlyList<GraphChange> InvertAgainst(NodeGraphDoc doc) => [new SetNodeData(NodeId, doc.Node(NodeId).Data)];
}

/// <summary>ノードの折り畳み状態を設定する。</summary>
public sealed record SetNodeCollapsed(int NodeId, bool Collapsed) : GraphChange
{
    public override NodeGraphDoc Apply(NodeGraphDoc doc) => doc.ReplaceNode(doc.Node(NodeId) with { Collapsed = Collapsed });
    public override IReadOnlyList<GraphChange> InvertAgainst(NodeGraphDoc doc) => [new SetNodeCollapsed(NodeId, doc.Node(NodeId).Collapsed)];
}

/// <summary>辺を 1 本張る (端点の存在・向きは Doc が検証)。</summary>
public sealed record Connect(GraphEdge Edge) : GraphChange
{
    public override NodeGraphDoc Apply(NodeGraphDoc doc) => doc.AddEdge(Edge);
    public override IReadOnlyList<GraphChange> InvertAgainst(NodeGraphDoc doc) => [new Disconnect(Edge.Id)];
}

/// <summary>辺を 1 本外す。</summary>
public sealed record Disconnect(int EdgeId) : GraphChange
{
    public override NodeGraphDoc Apply(NodeGraphDoc doc) => doc.RemoveEdge(EdgeId);
    public override IReadOnlyList<GraphChange> InvertAgainst(NodeGraphDoc doc) => [new Connect(doc.Edge(EdgeId))];
}

/// <summary>
/// 変更の列 — [[Luxel.Document.ChangeSet]] 相当。1 トランザクションが束ねる変更の単位で、まとめて適用/反転する。
/// <see cref="InvertAgainst"/> は各変更を**適用直前**の中間 Doc に対して反転し、逆順に並べて返す
/// (テキストの ChangeSet.Invert と同じ手筋)。
/// </summary>
public sealed class GraphChangeSet
{
    /// <summary>空の変更セット。</summary>
    public static readonly GraphChangeSet Empty = new([]);

    /// <summary>変更列 (適用順)。</summary>
    public IReadOnlyList<GraphChange> Changes { get; }

    /// <summary>変更列から作る。</summary>
    public GraphChangeSet(IReadOnlyList<GraphChange> changes) => Changes = changes;

    /// <summary>変更が無いか。</summary>
    public bool IsEmpty => Changes.Count == 0;
    /// <summary>変更の個数。</summary>
    public int Count => Changes.Count;

    /// <summary>全変更を順に適用した新しい Doc。</summary>
    public NodeGraphDoc Apply(NodeGraphDoc doc)
    {
        foreach (GraphChange c in Changes) doc = c.Apply(doc);
        return doc;
    }

    /// <summary>この変更セットの逆 (<paramref name="startDoc"/> に対して)。適用すると startDoc に戻る。</summary>
    public GraphChangeSet InvertAgainst(NodeGraphDoc startDoc)
    {
        // 各変更を適用する直前の Doc を記録し、その Doc に対して反転する。逆順に連結。
        var before = new NodeGraphDoc[Changes.Count];
        NodeGraphDoc d = startDoc;
        for (int i = 0; i < Changes.Count; i++) { before[i] = d; d = Changes[i].Apply(d); }

        var inv = new List<GraphChange>();
        for (int i = Changes.Count - 1; i >= 0; i--) inv.AddRange(Changes[i].InvertAgainst(before[i]));
        return new GraphChangeSet(inv);
    }

    /// <summary>連結 (this の後に <paramref name="other"/>)。連続移動などの undo 畳み込みに使う。</summary>
    public GraphChangeSet Concat(GraphChangeSet other)
        => other.IsEmpty ? this : IsEmpty ? other : new GraphChangeSet([.. Changes, .. other.Changes]);
}
