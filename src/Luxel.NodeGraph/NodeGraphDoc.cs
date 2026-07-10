namespace Luxel.NodeGraph;

/// <summary>
/// ノードグラフの文書 — **不変スナップショット** ([[Luxel.Document.TextDoc]] 相当)。ノード列 + 辺列を
/// 挿入順 (描画順) に保ち、id → 索引の辞書で O(1) 引きを提供する。テキストのフラットオフセットと違い
/// ノード/ポート/辺は**安定 id** を持つので、編集をまたいだ座標写像 (ChangeSet.MapPos 相当) は要らない。
/// 編集は <see cref="Apply"/> が新しい <see cref="NodeGraphDoc"/> を返す (元は変えない)。v1 は素朴な配列実装。
/// </summary>
public sealed class NodeGraphDoc
{
    /// <summary>空グラフ (ノード・辺なし)。</summary>
    public static readonly NodeGraphDoc Empty = new([], []);

    private readonly GraphNode[] _nodes;
    private readonly GraphEdge[] _edges;
    private readonly Dictionary<int, int> _nodeAt;   // ノード id → _nodes の索引
    private readonly Dictionary<int, int> _edgeAt;   // 辺 id → _edges の索引

    private NodeGraphDoc(GraphNode[] nodes, GraphEdge[] edges)
    {
        _nodes = nodes;
        _edges = edges;
        _nodeAt = new Dictionary<int, int>(nodes.Length);
        for (int i = 0; i < nodes.Length; i++)
        {
            if (!_nodeAt.TryAdd(nodes[i].Id, i))
                throw new ArgumentException($"重複するノード id: {nodes[i].Id}");
        }
        _edgeAt = new Dictionary<int, int>(edges.Length);
        for (int i = 0; i < edges.Length; i++)
        {
            if (!_edgeAt.TryAdd(edges[i].Id, i))
                throw new ArgumentException($"重複する辺 id: {edges[i].Id}");
        }
    }

    /// <summary>ノード列と辺列から文書を作る (辺の端点は存在チェックされる)。</summary>
    public static NodeGraphDoc Of(IEnumerable<GraphNode> nodes, IEnumerable<GraphEdge>? edges = null)
    {
        var n = nodes.ToArray();
        var e = (edges ?? []).ToArray();
        var doc = new NodeGraphDoc(n, e);
        foreach (GraphEdge edge in e) doc.ValidateEdge(edge);
        return doc;
    }

    /// <summary>ノード列 (挿入順)。</summary>
    public IReadOnlyList<GraphNode> Nodes => _nodes;
    /// <summary>辺列 (挿入順)。</summary>
    public IReadOnlyList<GraphEdge> Edges => _edges;

    /// <summary>ノードが存在するか。</summary>
    public bool HasNode(int id) => _nodeAt.ContainsKey(id);
    /// <summary>辺が存在するか。</summary>
    public bool HasEdge(int id) => _edgeAt.ContainsKey(id);

    /// <summary>ノードを id で引く (無ければ例外)。</summary>
    public GraphNode Node(int id) => _nodes[_nodeAt[id]];
    /// <summary>ノードを id で引く (無ければ null)。</summary>
    public GraphNode? TryNode(int id) => _nodeAt.TryGetValue(id, out int i) ? _nodes[i] : null;
    /// <summary>辺を id で引く (無ければ例外)。</summary>
    public GraphEdge Edge(int id) => _edges[_edgeAt[id]];

    /// <summary>ポートを id で引く (ノード or ポートが無ければ null)。</summary>
    public NodePort? Port(PortId p) => TryNode(p.Node)?.Port(p.Port);

    /// <summary>指定ノードに接続している辺 (入出力どちらも)。</summary>
    public IEnumerable<GraphEdge> EdgesOf(int nodeId)
    {
        foreach (GraphEdge e in _edges)
            if (e.From.Node == nodeId || e.To.Node == nodeId) yield return e;
    }

    /// <summary>変更セットを適用した新しい文書を返す (元は不変)。</summary>
    public NodeGraphDoc Apply(GraphChangeSet changes) => changes.Apply(this);

    // ---- 内部ミューテーション (GraphChange が使う。各々新しい配列で新 Doc を返す) ----

    internal NodeGraphDoc AddNode(GraphNode node)
    {
        if (HasNode(node.Id)) throw new ArgumentException($"ノード id {node.Id} は既に存在する");
        return new NodeGraphDoc([.. _nodes, node], _edges);
    }

    internal NodeGraphDoc RemoveNode(int id)
    {
        if (!HasNode(id)) throw new ArgumentException($"ノード id {id} が無い");
        var nodes = _nodes.Where(n => n.Id != id).ToArray();
        var edges = _edges.Where(e => e.From.Node != id && e.To.Node != id).ToArray();   // 接続辺も掃除
        return new NodeGraphDoc(nodes, edges);
    }

    internal NodeGraphDoc ReplaceNode(GraphNode node)
    {
        int i = _nodeAt[node.Id];
        var nodes = (GraphNode[])_nodes.Clone();
        nodes[i] = node;
        return new NodeGraphDoc(nodes, _edges);
    }

    internal NodeGraphDoc AddEdge(GraphEdge edge)
    {
        if (HasEdge(edge.Id)) throw new ArgumentException($"辺 id {edge.Id} は既に存在する");
        ValidateEdge(edge);
        return new NodeGraphDoc(_nodes, [.. _edges, edge]);
    }

    internal NodeGraphDoc RemoveEdge(int id)
    {
        if (!HasEdge(id)) throw new ArgumentException($"辺 id {id} が無い");
        return new NodeGraphDoc(_nodes, _edges.Where(e => e.Id != id).ToArray());
    }

    // 辺の端点が存在し、向き (From=Out / To=In) が正しいことを検証
    private void ValidateEdge(GraphEdge edge)
    {
        NodePort from = Port(edge.From) ?? throw new ArgumentException($"辺 {edge.Id}: 出力ポート {edge.From} が無い");
        NodePort to = Port(edge.To) ?? throw new ArgumentException($"辺 {edge.Id}: 入力ポート {edge.To} が無い");
        if (from.Dir != PortDir.Out) throw new ArgumentException($"辺 {edge.Id}: From {edge.From} は出力ポートでない");
        if (to.Dir != PortDir.In) throw new ArgumentException($"辺 {edge.Id}: To {edge.To} は入力ポートでない");
    }
}
