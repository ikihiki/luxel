using System.Numerics;
using Luxel.NodeGraph;

namespace Luxel.Tests;

/// <summary>汎用ノードエディタ S4 コマンド (ToDo 25 / ADR-0009) の単体テスト —
/// GraphCommands の純関数 (AddNode/DeleteSelection/MoveNodes/Select*/Connect) と履歴の 1 tx=1 undo。
/// view (NodeGraphView) の対話は story の play (golden) で担保する。canvas 不要。</summary>
public class NodeGraphCommandTests
{
    private static NodePort In(int id) => new(id, PortDir.In, "v", $"in{id}");
    private static NodePort Out(int id) => new(id, PortDir.Out, "v", $"out{id}");
    private static GraphNode N(int id, float x = 0, float y = 0) => new(id, "op", $"n{id}", new Vector2(x, y), [In(0), Out(1)]);
    private static GraphEdge E(int id, int fromNode, int toNode) => new(id, new PortId(fromNode, 1), new PortId(toNode, 0));

    [Fact]
    public void AddNode_AddsAndSelects()
    {
        var s = NodeGraphState.Create();
        var s1 = GraphCommands.AddNode(s, N(1)).State;
        Assert.True(s1.Doc.HasNode(1));
        Assert.True(s1.Selection.ContainsNode(1));
    }

    [Fact]
    public void DeleteSelection_RemovesNodesAndIncidentEdges_OneUndo()
    {
        var s = NodeGraphState.Create(NodeGraphDoc.Of([N(1), N(2), N(3)], [E(10, 1, 2), E(11, 2, 3)]));
        s = GraphCommands.SelectNodes(s, [2]).State;
        var history = new GraphHistory();
        var tr = GraphCommands.DeleteSelection(s);
        history.Record(tr);
        var s1 = tr.State;

        Assert.False(s1.Doc.HasNode(2));
        Assert.False(s1.Doc.HasEdge(10));    // 接続辺も消える
        Assert.False(s1.Doc.HasEdge(11));
        Assert.True(s1.Selection.IsEmpty);
        Assert.Equal(1, history.UndoDepth);   // 削除は 1 undo

        var back = history.Undo(s1);
        Assert.True(back.Doc.HasNode(2) && back.Doc.HasEdge(10) && back.Doc.HasEdge(11));
    }

    [Fact]
    public void MoveNodes_MovesAllAsOneTransaction()
    {
        var s = NodeGraphState.Create(NodeGraphDoc.Of([N(1, 0, 0), N(2, 10, 10)]));
        var tr = GraphCommands.MoveNodes(s, [1, 2], new Vector2(5, 7));
        Assert.Equal(new Vector2(5, 7), tr.State.Doc.Node(1).Pos);
        Assert.Equal(new Vector2(15, 17), tr.State.Doc.Node(2).Pos);
        Assert.True(tr.DocChanged);
    }

    [Fact]
    public void SelectAll_And_None()
    {
        var s = NodeGraphState.Create(NodeGraphDoc.Of([N(1), N(2), N(3)]));
        Assert.Equal(3, GraphCommands.SelectAll(s).State.Selection.Nodes.Count);
        var some = GraphCommands.SelectNodes(s, [1, 2]).State;
        Assert.True(GraphCommands.SelectNone(some).State.Selection.IsEmpty);
    }

    [Fact]
    public void Select_IsNotDocChanging()
    {
        var s = NodeGraphState.Create(NodeGraphDoc.Of([N(1)]));
        var tr = GraphCommands.SelectNodes(s, [1]);
        Assert.False(tr.DocChanged);   // 選択のみ = 履歴に積まれない
    }

    [Fact]
    public void Connect_AddsEdgeAndSelectsIt()
    {
        var s = NodeGraphState.Create(NodeGraphDoc.Of([N(1), N(2)]));
        var tr = GraphCommands.Connect(s, E(10, 1, 2));
        Assert.True(tr.State.Doc.HasEdge(10));
        Assert.True(tr.State.Selection.ContainsEdge(10));
    }

    // ---- GraphConnect (配線可否、S5) ----

    // out(num) / in(num) / in(str) の 3 ノード
    private static NodeGraphDoc WireDoc() => NodeGraphDoc.Of([
        new GraphNode(1, "a", "A", Vector2.Zero, [new NodePort(0, PortDir.Out, "num", "o")]),
        new GraphNode(2, "b", "B", new Vector2(100, 0), [new NodePort(0, PortDir.In, "num", "i"), new NodePort(1, PortDir.Out, "num", "o")]),
        new GraphNode(3, "c", "C", new Vector2(200, 0), [new NodePort(0, PortDir.In, "str", "i")]),
    ]);

    [Fact]
    public void Connect_Valid_NormalizesOutToIn()
    {
        var doc = WireDoc();
        // 入力(2,0) → 出力(1,0) の順で渡しても out→in に正規化される
        Assert.True(GraphConnect.TryResolve(doc, new PortId(2, 0), new PortId(1, 0), out PortId outP, out PortId inP));
        Assert.Equal(new PortId(1, 0), outP);
        Assert.Equal(new PortId(2, 0), inP);
    }

    [Fact]
    public void Connect_Rejects_SelfSameDirTypeMismatchDuplicate()
    {
        var doc = WireDoc();
        Assert.False(GraphConnect.CanConnect(doc, new PortId(1, 0), new PortId(1, 0)));   // 自己
        Assert.False(GraphConnect.CanConnect(doc, new PortId(1, 0), new PortId(2, 1)));   // 同方向 (out×out)
        Assert.False(GraphConnect.CanConnect(doc, new PortId(1, 0), new PortId(3, 0)));   // 型不一致 (num×str)

        var connected = doc.AddEdge(new GraphEdge(10, new PortId(1, 0), new PortId(2, 0)));
        Assert.False(GraphConnect.CanConnect(connected, new PortId(1, 0), new PortId(2, 0)));   // 重複
    }

    // ---- INodeCatalog (S6) ----

    [Fact]
    public void Catalog_Entry_CreatesNodeAtPosition()
    {
        var catalog = new NodeCatalog(
            new NodeCatalogEntry("gain", "Gain", (id, pos) => new GraphNode(id, "gain", "Gain", pos, [Out(1)])),
            new NodeCatalogEntry("out", "Output", (id, pos) => new GraphNode(id, "out", "Output", pos, [In(0)])));

        Assert.Equal(2, catalog.Entries.Count);
        GraphNode n = catalog.Entries[0].Create(7, new Vector2(50, 60));
        Assert.Equal(7, n.Id);
        Assert.Equal("gain", n.Kind);
        Assert.Equal(new Vector2(50, 60), n.Pos);

        // カタログ工場 → AddNode で追加・選択される
        var s = GraphCommands.AddNode(NodeGraphState.Create(), n).State;
        Assert.True(s.Doc.HasNode(7) && s.Selection.ContainsNode(7));
    }

    // ---- AutoLayout (S7) ----

    [Fact]
    public void AutoLayout_RanksChainLeftToRight_OneUndo()
    {
        // A→B→C の鎖を全部同じ位置に置いてから整列
        var s = NodeGraphState.Create(NodeGraphDoc.Of(
            [N(1, 500, 500), N(2, 500, 500), N(3, 500, 500)],
            [E(10, 1, 2), E(11, 2, 3)]));
        var history = new GraphHistory();
        NodeMeasure measure = _ => new NodeSize(120, 60);
        var tr = GraphCommands.AutoLayout(s, measure);
        history.Record(tr);
        var s1 = tr.State;

        float ax = s1.Doc.Node(1).Pos.X, bx = s1.Doc.Node(2).Pos.X, cx = s1.Doc.Node(3).Pos.X;
        Assert.True(ax < bx && bx < cx);        // 依存に沿って左→右
        Assert.Equal(1, history.UndoDepth);     // 全ノード移動で 1 undo
        Assert.Equal(500, history.Undo(s1).Doc.Node(2).Pos.X);   // undo で元位置へ
    }

    [Fact]
    public void AutoLayout_SameRankStacksVertically()
    {
        // 2 と 3 はどちらも 1 からの辺 → 同じランク (縦に積む)
        var s = NodeGraphState.Create(NodeGraphDoc.Of(
            [N(1), N(2), N(3)], [E(10, 1, 2), new GraphEdge(11, new PortId(1, 1), new PortId(3, 0))]));
        var s1 = GraphCommands.AutoLayout(s, _ => new NodeSize(100, 50)).State;
        Assert.Equal(s1.Doc.Node(2).Pos.X, s1.Doc.Node(3).Pos.X, 3);   // 同ランク = 同 x
        Assert.NotEqual(s1.Doc.Node(2).Pos.Y, s1.Doc.Node(3).Pos.Y);   // 縦にずれる
    }
}
