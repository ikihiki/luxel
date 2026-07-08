using System.Numerics;
using System.Text;

namespace Luxel.NodeGraph;

/// <summary>
/// ノードエディタの**純射影** ([[Luxel.Editor.EditorGeometry]] 相当) — <see cref="NodeGraphState"/> と
/// <see cref="GraphConfig"/> + view 注入の <see cref="NodeMeasure"/> から、ノード矩形・ポートアンカー・接続線のベジェ・
/// ヒット判定・world↔screen 変換を計算する。**選択状態を持たない** (state を投影するだけ)。core は Luxel.Typography/TwoD に
/// 依存せず、サイズ測定を <see cref="NodeMeasure"/> で外注するので canvas 無しで単体テストできる。ノードは内容 (Pos/サイズ/
/// ポート/レイアウト装飾) を鍵にキャッシュし、オーバーレイ装飾や pan/zoom の変化ではノード幾何を再構築しない
/// (配線中の進行中ワイヤが 60fps の根拠)。
/// </summary>
public sealed class GraphGeometry
{
    private GraphConfig _cfg;
    private NodeMeasure _measure;
    private NodeGraphState _state;
    private NodeLayout[] _layouts = [];
    private Dictionary<int, NodeLayout> _byId = new();
    private Dictionary<string, NodeLayout> _cache = new();

    /// <summary>設定・測定関数・初期状態から作る。</summary>
    public GraphGeometry(GraphConfig config, NodeMeasure measure, NodeGraphState? state = null)
    {
        _cfg = config;
        _measure = measure;
        _state = state ?? NodeGraphState.Create();
        Rebuild();
    }

    /// <summary>現在の状態。</summary>
    public NodeGraphState State => _state;
    /// <summary>現在の設定。</summary>
    public GraphConfig Config => _cfg;
    /// <summary>ノード数。</summary>
    public int NodeCount => _layouts.Length;
    /// <summary>ノードの射影を doc 順に列挙する (view の描画順)。</summary>
    public IReadOnlyList<NodeLayout> Layouts => _layouts;

    /// <summary>設定/測定を差し替える (全キャッシュを捨てて作り直す)。</summary>
    public void Configure(GraphConfig config, NodeMeasure? measure = null)
    {
        _cfg = config;
        if (measure is not null) _measure = measure;
        _cache = new Dictionary<string, NodeLayout>();
        Rebuild();
    }

    /// <summary>状態を差し替える (内容/レイアウト装飾が変わらないノードは射影を再利用)。</summary>
    public void SetState(NodeGraphState state)
    {
        _state = state;
        Rebuild();
    }

    /// <summary>ノードの射影 (無ければ例外)。</summary>
    public NodeLayout Layout(int nodeId) => _byId[nodeId];
    /// <summary>ノードの world 矩形。</summary>
    public GraphRect NodeRect(int nodeId) => _byId[nodeId].Rect;

    /// <summary>ポートのアンカー点 (辺の端点、world)。</summary>
    public Vector2 PortAnchor(PortId port)
    {
        NodeLayout nl = _byId[port.Node];
        return nl.Port(port.Port)?.Anchor ?? nl.Rect.Center;
    }

    /// <summary>接続線 1 本の 3 次ベジェ (world)。</summary>
    public GraphWire Wire(int edgeId)
    {
        GraphEdge e = _state.Doc.Edge(edgeId);
        Vector2 p0 = PortAnchor(e.From), p1 = PortAnchor(e.To);
        float t = MathF.Max(_cfg.WireTangent, MathF.Abs(p1.X - p0.X) * 0.5f);
        return new GraphWire(p0, p0 + new Vector2(t, 0), p1 - new Vector2(t, 0), p1);
    }

    /// <summary>全ノードのインライン枠を平坦に列挙する (view の widget ホスト用)。</summary>
    public IReadOnlyList<WidgetSlot> WidgetSlots()
    {
        var slots = new List<WidgetSlot>();
        foreach (NodeLayout nl in _layouts) slots.AddRange(nl.Slots);
        return slots;
    }

    /// <summary>全ノードを包む world 矩形 (fit-to-view 用。ノードが無ければ原点の 0 サイズ)。</summary>
    public GraphRect ContentBounds()
    {
        if (_layouts.Length == 0) return default;
        GraphRect b = _layouts[0].Rect;
        for (int i = 1; i < _layouts.Length; i++) b = b.Union(_layouts[i].Rect);
        return b;
    }

    /// <summary>world → screen (pan/zoom を適用)。</summary>
    public Vector2 WorldToScreen(Vector2 world) => world * _state.Viewport.Zoom + _state.Viewport.Pan;
    /// <summary>screen → world (pan/zoom の逆)。</summary>
    public Vector2 ScreenToWorld(Vector2 screen) => (screen - _state.Viewport.Pan) / _state.Viewport.Zoom;

    /// <summary>world 点の直下にある対象を返す (ポート → ノード本体 → 辺 → 空白 の優先順、上のノードを優先)。</summary>
    public GraphHit HitTest(Vector2 world)
    {
        // ポート優先 (小さい・辺上に載るので本体より先に)。上に描かれるノード (doc 後方) を優先。
        for (int i = _layouts.Length - 1; i >= 0; i--)
        {
            NodeLayout nl = _layouts[i];
            foreach (PortGeometry p in nl.Inputs) if (p.HitRect.Contains(world)) return GraphHit.Port(PortDir.In, p.Port);
            foreach (PortGeometry p in nl.Outputs) if (p.HitRect.Contains(world)) return GraphHit.Port(PortDir.Out, p.Port);
        }
        // ノード本体
        for (int i = _layouts.Length - 1; i >= 0; i--)
            if (_layouts[i].Rect.Contains(world)) return GraphHit.Node(_layouts[i].NodeId);
        // 辺 (ベジェをサンプルして距離判定。許容は screen px を zoom で world 化)
        float tol = _cfg.EdgeHitTolerance / MathF.Max(_state.Viewport.Zoom, 1e-4f);
        foreach (GraphEdge e in _state.Doc.Edges)
            if (DistanceToWire(Wire(e.Id), world) <= tol) return GraphHit.Edge(e.Id);
        return GraphHit.None;
    }

    // ベジェを折れ線でサンプルし、点との最短距離を返す
    private static float DistanceToWire(GraphWire w, Vector2 p)
    {
        const int Samples = 24;
        float best = float.MaxValue;
        Vector2 prev = w.P0;
        for (int i = 1; i <= Samples; i++)
        {
            Vector2 cur = w.At(i / (float)Samples);
            best = MathF.Min(best, DistanceToSegment(p, prev, cur));
            prev = cur;
        }
        return best;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-9f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    private void Rebuild()
    {
        NodeGraphDoc doc = _state.Doc;
        var layouts = new NodeLayout[doc.Nodes.Count];
        var byId = new Dictionary<int, NodeLayout>(doc.Nodes.Count);
        var next = new Dictionary<string, NodeLayout>(doc.Nodes.Count);

        for (int i = 0; i < doc.Nodes.Count; i++)
        {
            GraphNode node = doc.Nodes[i];
            Size size = _measure(node);
            var inline = InlineDecorationsOf(node.Id);
            string key = LayoutKey(node, size, inline);
            if (!next.TryGetValue(key, out NodeLayout? nl))
            {
                if (!_cache.TryGetValue(key, out nl)) nl = BuildNode(node, size, inline);
                next[key] = nl;
            }
            layouts[i] = nl;
            byId[node.Id] = nl;
        }

        _layouts = layouts;
        _byId = byId;
        _cache = next;
    }

    // レイアウトに効く装飾 (インライン枠) だけをノード id 順に集める。バッジ/ハイライトは含めない。
    private List<NodeInlineDecoration> InlineDecorationsOf(int nodeId)
    {
        var list = new List<NodeInlineDecoration>();
        foreach (GraphDecoration d in _state.Decorations.All())
            if (d is NodeInlineDecoration nid && nid.NodeId == nodeId) list.Add(nid);
        list.Sort((a, b) => string.CompareOrdinal(a.Key.ToString(), b.Key.ToString()));   // 決定的順序
        return list;
    }

    // キャッシュ鍵: ノード id + Pos + サイズ + 折り畳み + ポート署名 + インライン枠署名。
    // オーバーレイ装飾 (バッジ/ハイライト) と viewport は鍵に含めない (それらの変化では再構築しない)。
    private static string LayoutKey(GraphNode node, Size size, List<NodeInlineDecoration> inline)
    {
        var sb = new StringBuilder();
        sb.Append(node.Id).Append('|').Append(node.Pos.X).Append(',').Append(node.Pos.Y)
          .Append('|').Append(size.Width).Append(',').Append(size.Height)
          .Append('|').Append(node.Collapsed ? '1' : '0').Append('|');
        foreach (NodePort p in node.Ports) sb.Append(p.Id).Append(p.Dir == PortDir.In ? 'i' : 'o').Append(';');
        sb.Append('|');
        foreach (NodeInlineDecoration d in inline) sb.Append(d.Key.GetHashCode()).Append(',').Append(d.Width).Append(',').Append(d.Height).Append(';');
        return sb.ToString();
    }

    private NodeLayout BuildNode(GraphNode node, Size size, List<NodeInlineDecoration> inline)
    {
        GraphRect rect = GraphRect.FromMinSize(node.Pos, size);

        var inputs = new List<PortGeometry>();
        var outputs = new List<PortGeometry>();
        int ii = 0, oi = 0;
        float portsTop = node.Pos.Y + _cfg.TitleBarHeight + _cfg.PortStartY;
        foreach (NodePort port in node.Ports)
        {
            bool input = port.Dir == PortDir.In;
            int idx = input ? ii++ : oi++;
            float y = node.Collapsed
                ? node.Pos.Y + _cfg.TitleBarHeight * 0.5f
                : portsTop + _cfg.PortRowHeight * (idx + 0.5f);
            float x = input ? rect.X : rect.Right;
            var anchor = new Vector2(x, y);
            var hit = new GraphRect(x - _cfg.PortRadius, y - _cfg.PortRadius, _cfg.PortRadius * 2, _cfg.PortRadius * 2);
            var pg = new PortGeometry(new PortId(node.Id, port.Id), port.Dir, anchor, hit);
            (input ? inputs : outputs).Add(pg);
        }

        // インライン枠はポート領域の下に縦積み
        var slots = new List<WidgetSlot>();
        int rows = Math.Max(ii, oi);
        float contentTop = node.Collapsed ? node.Pos.Y + _cfg.TitleBarHeight : portsTop + _cfg.PortRowHeight * rows;
        float y2 = contentTop + _cfg.SlotGap;
        foreach (NodeInlineDecoration d in inline)
        {
            var r = new GraphRect(rect.X + _cfg.SlotPadding, y2, MathF.Max(0, size.Width - _cfg.SlotPadding * 2), d.Height);
            slots.Add(new WidgetSlot(node.Id, d.Key, r));
            y2 += d.Height + _cfg.SlotGap;
        }

        return new NodeLayout(node.Id, rect, inputs.ToArray(), outputs.ToArray(), slots.ToArray());
    }
}
