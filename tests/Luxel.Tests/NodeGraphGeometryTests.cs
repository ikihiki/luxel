using System.Numerics;
using Luxel.NodeGraph;

namespace Luxel.Tests;

/// <summary>汎用ノードエディタ S3 純射影 (ToDo 25 / ADR-0009) の単体テスト —
/// GraphGeometry のノード矩形・ポートアンカー・ワイヤ端点・HitTest・world↔screen・ContentBounds・
/// レイアウトキャッシュ (Assert.Same/NotSame)。canvas 不要 (NodeMeasure を注入するので Typography 非依存)。</summary>
public class NodeGraphGeometryTests
{
    private static NodePort In(int id) => new(id, PortDir.In, "v", $"in{id}");
    private static NodePort Out(int id) => new(id, PortDir.Out, "v", $"out{id}");
    // 入力ポート 0 + 出力ポート 1 のノード
    private static GraphNode N(int id, float x = 0, float y = 0) => new(id, "op", $"n{id}", new Vector2(x, y), [In(0), Out(1)]);
    private static GraphEdge E(int id, int fromNode, int toNode) => new(id, new PortId(fromNode, 1), new PortId(toNode, 0));

    // 固定サイズを返す測定 (120×80)。core は Typography 非依存なのでこれで幾何が回る。
    private static readonly NodeMeasure Measure = _ => new Size(120, 80);
    private static readonly GraphConfig Cfg = new();   // 既定 (TitleBar 22 / PortRow 18 / PortStartY 4 / PortRadius 5)

    private static GraphGeometry Geo(NodeGraphState state) => new(Cfg, Measure, state);

    private static void AssertVec(Vector2 expected, Vector2 actual, int precision = 3)
    {
        Assert.Equal(expected.X, actual.X, precision);
        Assert.Equal(expected.Y, actual.Y, precision);
    }

    // ---- ノード矩形 / ポートアンカー ----

    [Fact]
    public void NodeRect_MatchesPosAndMeasure()
    {
        var g = Geo(NodeGraphState.Create(NodeGraphDoc.Of([N(1, 10, 20)])));
        GraphRect r = g.NodeRect(1);
        Assert.Equal(10, r.X);
        Assert.Equal(20, r.Y);
        Assert.Equal(120, r.Width);
        Assert.Equal(80, r.Height);
    }

    [Fact]
    public void PortAnchor_OnLeftAndRightEdges()
    {
        var g = Geo(NodeGraphState.Create(NodeGraphDoc.Of([N(1, 0, 0)])));
        // 入力は左端 x=0、出力は右端 x=120。y = TitleBar(22)+PortStartY(4)+PortRow*0.5(9) = 35
        AssertVec(new Vector2(0, 35), g.PortAnchor(new PortId(1, 0)));
        AssertVec(new Vector2(120, 35), g.PortAnchor(new PortId(1, 1)));
    }

    [Fact]
    public void CollapsedNode_PortsAtTitleCenter()
    {
        var doc = NodeGraphDoc.Of([N(1, 0, 0) with { Collapsed = true }]);
        var g = Geo(NodeGraphState.Create(doc));
        // 折り畳み時は両ポートともタイトルバー中央 y=11
        Assert.Equal(11, g.PortAnchor(new PortId(1, 0)).Y, 3);
        Assert.Equal(11, g.PortAnchor(new PortId(1, 1)).Y, 3);
    }

    // ---- ワイヤ ----

    [Fact]
    public void Wire_EndpointsAndTangents()
    {
        var doc = NodeGraphDoc.Of([N(1, 0, 0), N(2, 200, 0)], [E(10, 1, 2)]);
        var g = Geo(NodeGraphState.Create(doc));
        GraphWire w = g.Wire(10);
        AssertVec(new Vector2(120, 35), w.P0);    // 出力アンカー
        AssertVec(new Vector2(200, 35), w.P1);    // 入力アンカー
        Assert.True(w.C0.X > w.P0.X);             // 出力側接線は右へ
        Assert.True(w.C1.X < w.P1.X);             // 入力側接線は左へ
        AssertVec(new Vector2(160, 35), w.At(0.5f));   // 対称配置なので中点
    }

    // ---- world ↔ screen ----

    [Fact]
    public void WorldScreen_RoundTrip()
    {
        var state = NodeGraphState.Create(NodeGraphDoc.Of([N(1)]), viewport: new GraphViewport(new Vector2(30, -20), 1.5f));
        var g = Geo(state);
        var w = new Vector2(40, 50);
        AssertVec(new Vector2(40 * 1.5f + 30, 50 * 1.5f - 20), g.WorldToScreen(w));   // screen = world*zoom+pan
        AssertVec(w, g.ScreenToWorld(g.WorldToScreen(w)));                            // 往復
    }

    // ---- HitTest ----

    [Fact]
    public void HitTest_Node_Port_Empty()
    {
        var g = Geo(NodeGraphState.Create(NodeGraphDoc.Of([N(1, 0, 0)])));
        Assert.Equal(GraphHitKind.Node, g.HitTest(new Vector2(60, 40)).Kind);          // 本体中央

        GraphHit inHit = g.HitTest(new Vector2(0, 35));                                 // 入力ポート
        Assert.Equal(GraphHitKind.InputPort, inHit.Kind);
        Assert.Equal(new PortId(1, 0), new PortId(inHit.NodeId, inHit.PortId));

        GraphHit outHit = g.HitTest(new Vector2(120, 35));                              // 出力ポート
        Assert.Equal(GraphHitKind.OutputPort, outHit.Kind);

        Assert.Equal(GraphHitKind.Empty, g.HitTest(new Vector2(500, 500)).Kind);        // 空白
    }

    [Fact]
    public void HitTest_Edge()
    {
        var doc = NodeGraphDoc.Of([N(1, 0, 0), N(2, 200, 0)], [E(10, 1, 2)]);
        var g = Geo(NodeGraphState.Create(doc));
        GraphHit hit = g.HitTest(new Vector2(160, 35));   // ワイヤ中点付近
        Assert.Equal(GraphHitKind.Edge, hit.Kind);
        Assert.Equal(10, hit.EdgeId);
    }

    [Fact]
    public void HitTest_TopmostNodeWins()
    {
        // 重なる 2 ノード。doc 後方 (2) が上に描かれるので本体ヒットは 2
        var doc = NodeGraphDoc.Of([N(1, 0, 0), N(2, 20, 20)]);
        var g = Geo(NodeGraphState.Create(doc));
        Assert.Equal(2, g.HitTest(new Vector2(60, 60)).NodeId);
    }

    // ---- ContentBounds ----

    [Fact]
    public void ContentBounds_UnionOfNodes()
    {
        var doc = NodeGraphDoc.Of([N(1, 0, 0), N(2, 200, 100)]);
        var g = Geo(NodeGraphState.Create(doc));
        GraphRect b = g.ContentBounds();
        Assert.Equal(0, b.X);
        Assert.Equal(0, b.Y);
        Assert.Equal(320, b.Right);    // 200 + 120
        Assert.Equal(180, b.Bottom);   // 100 + 80
    }

    // ---- キャッシュ (Assert.Same/NotSame) ----

    [Fact]
    public void Cache_ReusedForOverlayAndViewportChanges()
    {
        var g = Geo(NodeGraphState.Create(NodeGraphDoc.Of([N(1)])));
        NodeLayout before = g.Layout(1);

        // オーバーレイ装飾 (バッジ) 追加 → 再構築しない
        var withBadge = g.State.WithDecorations("diag", new GraphDecorationSet([new NodeBadgeDecoration(1, GraphBadge.Error, 0)])).State;
        g.SetState(withBadge);
        Assert.Same(before, g.Layout(1));

        // viewport (pan/zoom) 変更 → 再構築しない
        g.SetState(g.State.WithViewport(new GraphViewport(new Vector2(50, 50), 2f)).State);
        Assert.Same(before, g.Layout(1));
    }

    [Fact]
    public void Cache_RebuiltWhenNodeMovesOrGainsInlineSlot()
    {
        var g = Geo(NodeGraphState.Create(NodeGraphDoc.Of([N(1, 0, 0)])));
        NodeLayout before = g.Layout(1);

        // ノード移動 → 再構築
        g.SetState(g.State.Apply(new MoveNode(1, new Vector2(10, 0))).State);
        NodeLayout moved = g.Layout(1);
        Assert.NotSame(before, moved);
        Assert.Equal(10, moved.Rect.X);

        // インライン枠 (レイアウトに効く装飾) 追加 → 再構築
        g.SetState(g.State.WithDecorations("ui", new GraphDecorationSet([new NodeInlineDecoration(1, 40, 12, "slider")])).State);
        NodeLayout withSlot = g.Layout(1);
        Assert.NotSame(moved, withSlot);
        Assert.Single(withSlot.Slots);
        Assert.Single(g.WidgetSlots());
    }
}
