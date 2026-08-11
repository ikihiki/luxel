using System.Numerics;
using Luxel.NodeGraph;

namespace Luxel.Tests;

/// <summary>汎用ノードエディタ S1 コア (ToDo 25 / ADR-0009) の単体テスト —
/// NodeGraphDoc の id 索引・辺掃除、GraphChange の Apply/Invert 往復、GraphChangeSet の反転、
/// NodeGraphState/GraphTransaction、GraphHistory の undo/redo (1 tx=1 undo, coalesce)。canvas 不要 (純データ)。</summary>
public class NodeGraphCoreTests
{
    // ---- テスト用のグラフ組み立てヘルパ ----

    private static NodePort In(int id, string type = "v") => new(id, PortDir.In, type, $"in{id}");
    private static NodePort Out(int id, string type = "v") => new(id, PortDir.Out, type, $"out{id}");

    // ノード id n を「入力ポート 0 + 出力ポート 1」で作る
    private static GraphNode N(int id, float x = 0, float y = 0)
        => new(id, "op", $"n{id}", new Vector2(x, y), [In(0), Out(1)]);

    private static GraphEdge E(int id, int fromNode, int toNode)
        => new(id, new PortId(fromNode, 1), new PortId(toNode, 0));

    // ---- NodeGraphDoc ----

    [Fact]
    public void Doc_IndexAndLookup()
    {
        var doc = NodeGraphDoc.Of([N(1), N(2, 100)], [E(10, 1, 2)]);
        Assert.Equal(2, doc.Nodes.Count);
        Assert.Single(doc.Edges);
        Assert.True(doc.HasNode(1));
        Assert.False(doc.HasNode(99));
        Assert.Equal(new Vector2(100, 0), doc.Node(2).Pos);
        Assert.Null(doc.TryNode(99));
        Assert.Equal(PortDir.Out, doc.Port(new PortId(1, 1))!.Dir);
        Assert.Null(doc.Port(new PortId(1, 5)));   // 無いポート
    }

    [Fact]
    public void Doc_RejectsDuplicateNodeId()
        => Assert.Throws<ArgumentException>(() => NodeGraphDoc.Of([N(1), N(1)]));

    [Fact]
    public void Doc_RejectsBadEdgeDirectionAndMissingEndpoint()
    {
        // From が入力ポートを指す (向き違反)
        Assert.Throws<ArgumentException>(() => NodeGraphDoc.Of([N(1), N(2)], [new GraphEdge(10, new PortId(1, 0), new PortId(2, 0))]));
        // 端点ノードが無い
        Assert.Throws<ArgumentException>(() => NodeGraphDoc.Of([N(1)], [E(10, 1, 2)]));
    }

    [Fact]
    public void Doc_EdgesOf_FindsIncident()
    {
        var doc = NodeGraphDoc.Of([N(1), N(2), N(3)], [E(10, 1, 2), E(11, 2, 3)]);
        Assert.Equal([10, 11], doc.EdgesOf(2).Select(e => e.Id).OrderBy(x => x));
        Assert.Equal([10], doc.EdgesOf(1).Select(e => e.Id));
    }

    // ---- 個別変更の Apply/Invert ----

    [Fact]
    public void AddRemoveNode_ApplyInvertRoundTrip()
    {
        var doc = NodeGraphDoc.Of([N(1)]);
        var add = new AddNode(N(2, 50));
        var doc2 = add.Apply(doc);
        Assert.True(doc2.HasNode(2));

        // 逆適用で元に戻る
        var inv = add.InvertAgainst(doc);
        var back = inv.Aggregate(doc2, (d, c) => c.Apply(d));
        Assert.False(back.HasNode(2));
        Assert.Single(back.Nodes);
    }

    [Fact]
    public void MoveNode_ApplyInvert()
    {
        var doc = NodeGraphDoc.Of([N(1, 10, 20)]);
        var mv = new MoveNode(1, new Vector2(5, -5));
        var moved = mv.Apply(doc);
        Assert.Equal(new Vector2(15, 15), moved.Node(1).Pos);

        var back = mv.InvertAgainst(doc).Aggregate(moved, (d, c) => c.Apply(d));
        Assert.Equal(new Vector2(10, 20), back.Node(1).Pos);
    }

    [Fact]
    public void SetNodeData_ApplyInvertRestoresOld()
    {
        var doc = NodeGraphDoc.Of([N(1) with { Data = "old" }]);
        var set = new SetNodeData(1, "new");
        var doc2 = set.Apply(doc);
        Assert.Equal("new", doc2.Node(1).Data);

        var back = set.InvertAgainst(doc).Aggregate(doc2, (d, c) => c.Apply(d));
        Assert.Equal("old", back.Node(1).Data);
    }

    [Fact]
    public void ConnectDisconnect_ApplyInvert()
    {
        var doc = NodeGraphDoc.Of([N(1), N(2)]);
        var con = new Connect(E(10, 1, 2));
        var doc2 = con.Apply(doc);
        Assert.True(doc2.HasEdge(10));

        var back = con.InvertAgainst(doc).Aggregate(doc2, (d, c) => c.Apply(d));
        Assert.False(back.HasEdge(10));
    }

    // ---- 辺の掃除 + 削除ノードの逆 (辺再接続) ----

    [Fact]
    public void RemoveNode_DropsIncidentEdges_AndInvertRestoresThem()
    {
        var doc = NodeGraphDoc.Of([N(1), N(2), N(3)], [E(10, 1, 2), E(11, 2, 3)]);
        var rm = new RemoveNode(2);
        var doc2 = rm.Apply(doc);
        Assert.False(doc2.HasNode(2));
        Assert.False(doc2.HasEdge(10));   // 接続辺が掃除された
        Assert.False(doc2.HasEdge(11));
        Assert.Empty(doc2.Edges);

        // 逆 = ノード復活 + 両辺の再接続
        var back = rm.InvertAgainst(doc).Aggregate(doc2, (d, c) => c.Apply(d));
        Assert.True(back.HasNode(2));
        Assert.True(back.HasEdge(10));
        Assert.True(back.HasEdge(11));
        Assert.Equal(new Vector2(0, 0), back.Node(2).Pos);
    }

    // ---- GraphChangeSet の反転 (複数変更) ----

    [Fact]
    public void ChangeSet_InvertAgainst_RestoresStart()
    {
        var start = NodeGraphDoc.Of([N(1)]);
        var set = new GraphChangeSet([
            new AddNode(N(2, 40)),
            new Connect(E(10, 1, 2)),
            new MoveNode(1, new Vector2(7, 0)),
        ]);
        var end = set.Apply(start);
        Assert.True(end.HasNode(2) && end.HasEdge(10));
        Assert.Equal(new Vector2(7, 0), end.Node(1).Pos);

        // 逆セットを end に適用すると start に完全復帰
        var back = set.InvertAgainst(start).Apply(end);
        Assert.Single(back.Nodes);
        Assert.Empty(back.Edges);
        Assert.Equal(new Vector2(0, 0), back.Node(1).Pos);
    }

    // ---- Transaction / State ----

    [Fact]
    public void Transaction_State_IsImmutableSnapshot()
    {
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1)]));
        var tr = s0.Apply(new AddNode(N(2)));
        var s1 = tr.State;

        Assert.True(tr.DocChanged);
        Assert.Single(s0.Doc.Nodes);           // 元は不変
        Assert.Equal(2, s1.Doc.Nodes.Count);
        Assert.Same(s1, tr.State);             // キャッシュ
    }

    [Fact]
    public void Transaction_Selection_RetainedAgainstNewDoc()
    {
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1), N(2)]), GraphSelection.Of([1, 2], null, 2));
        // ノード 2 を削除 → 選択は自動で 2 を落とす (Retain)
        var s1 = s0.Apply(new RemoveNode(2)).State;
        Assert.Equal([1], s1.Selection.Nodes);
        Assert.Equal(1, s1.Selection.Main);
    }

    [Fact]
    public void Transaction_EmptyChanges_NotDocChanged()
    {
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1)]));
        var tr = s0.WithSelection(GraphSelection.Node(1));
        Assert.False(tr.DocChanged);
        Assert.Equal(1, tr.State.Selection.Main);
    }

    [Fact]
    public void Viewport_PreservedThroughEdit_AndSettable()
    {
        var vp = new GraphViewport(new Vector2(30, 40), 2f);
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1)]), viewport: vp);
        var s1 = s0.Apply(new MoveNode(1, new Vector2(1, 1))).State;
        Assert.Equal(vp, s1.Viewport);   // 編集で viewport は保たれる

        var s2 = s0.WithViewport(GraphViewport.Default).State;
        Assert.Equal(GraphViewport.Default, s2.Viewport);
    }

    // ---- History ----

    [Fact]
    public void History_UndoRedo_SingleTransaction()
    {
        var history = new GraphHistory();
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1)]));
        var tr = s0.Apply(new AddNode(N(2, 60)));
        history.Record(tr);
        var s1 = tr.State;

        Assert.True(history.CanUndo);
        Assert.Equal(1, history.UndoDepth);

        var undone = history.Undo(s1);
        Assert.False(undone.Doc.HasNode(2));
        Assert.True(history.CanRedo);

        var redone = history.Redo(undone);
        Assert.True(redone.Doc.HasNode(2));
        Assert.Equal(new Vector2(60, 0), redone.Doc.Node(2).Pos);
    }

    [Fact]
    public void History_MultiChangeTransaction_IsOneUndo()
    {
        var history = new GraphHistory();
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1), N(2), N(3)]));
        // 3 ノードをまとめて移動 = 1 Transaction
        var tr = s0.Apply(new MoveNode(1, new Vector2(5, 0)), new MoveNode(2, new Vector2(5, 0)), new MoveNode(3, new Vector2(5, 0)));
        history.Record(tr);
        Assert.Equal(1, history.UndoDepth);   // 3 変更でも 1 undo

        var undone = history.Undo(tr.State);
        Assert.Equal(new Vector2(0, 0), undone.Doc.Node(1).Pos);
        Assert.Equal(new Vector2(0, 0), undone.Doc.Node(3).Pos);
    }

    [Fact]
    public void History_Coalesce_FoldsContinuousMoves()
    {
        var history = new GraphHistory();
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1, 0, 0)]));

        var t1 = s0.Apply(new MoveNode(1, new Vector2(3, 0)));
        history.Record(t1);
        var t2 = t1.State.Apply(new MoveNode(1, new Vector2(4, 0)));
        history.Record(t2, coalesce: true);   // 直前と畳む

        Assert.Equal(1, history.UndoDepth);    // 2 移動が 1 undo に
        Assert.Equal(new Vector2(7, 0), t2.State.Doc.Node(1).Pos);

        var undone = history.Undo(t2.State);   // 1 手で最初へ戻る
        Assert.Equal(new Vector2(0, 0), undone.Doc.Node(1).Pos);
    }

    [Fact]
    public void History_Record_ClearsRedo()
    {
        var history = new GraphHistory();
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1)]));
        var t1 = s0.Apply(new AddNode(N(2)));
        history.Record(t1);
        var undone = history.Undo(t1.State);
        Assert.True(history.CanRedo);

        // 新しい編集を記録すると redo は捨てられる
        var t2 = undone.Apply(new AddNode(N(3)));
        history.Record(t2);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void History_IgnoresNonDocChangingTransaction()
    {
        var history = new GraphHistory();
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1)]));
        history.Record(s0.WithSelection(GraphSelection.Node(1)));   // 選択のみ = 文書非変更
        Assert.False(history.CanUndo);
    }
}
