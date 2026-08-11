using System.Numerics;
using Luxel.NodeGraph;

namespace Luxel.Tests;

/// <summary>汎用ノードエディタ S2 装飾 (ToDo 25 / ADR-0009) の単体テスト —
/// GraphDecoration の Map (削除対象を落とす)・AffectsLayout 分類、GraphDecorationSet/Table、
/// StateEffect (SetGraphDecorations/RemoveGraphDecorations)、NodeGraphState への統合 (編集/undo 追従)、
/// IGraphDecorationProvider。canvas 不要 (純データ)。</summary>
public class NodeGraphDecorationTests
{
    private static NodePort In(int id) => new(id, PortDir.In, "v", $"in{id}");
    private static NodePort Out(int id) => new(id, PortDir.Out, "v", $"out{id}");
    private static GraphNode N(int id, float x = 0, float y = 0) => new(id, "op", $"n{id}", new Vector2(x, y), [In(0), Out(1)]);
    private static GraphEdge E(int id, int fromNode, int toNode) => new(id, new PortId(fromNode, 1), new PortId(toNode, 0));

    // ---- AffectsLayout 分類 ----

    [Fact]
    public void AffectsLayout_OverlayVsLayout()
    {
        Assert.False(new NodeBadgeDecoration(1, GraphBadge.Error, 0xFFFF0000).AffectsLayout);
        Assert.False(new EdgeHighlightDecoration(10, 0xFF00FF00).AffectsLayout);
        Assert.False(new PortHintDecoration(new PortId(1, 0), 0xFF00FF00).AffectsLayout);
        Assert.False(new PendingWireDecoration(new PortId(1, 1), new Vector2(5, 5), 0xFFFFFFFF).AffectsLayout);
        Assert.True(new NodeInlineDecoration(1, 40, 12, "slider").AffectsLayout);   // ノード内枠のみレイアウトに効く
    }

    // ---- Map: 削除された対象の装飾は落ちる ----

    [Fact]
    public void Decoration_Map_DropsWhenTargetRemoved()
    {
        var doc = NodeGraphDoc.Of([N(1), N(2)], [E(10, 1, 2)]);
        var removed = doc.RemoveNode(2);   // ノード 2 と辺 10 が消える

        Assert.NotNull(new NodeBadgeDecoration(1, GraphBadge.Info, 0).Map(removed));   // 生存
        Assert.Null(new NodeBadgeDecoration(2, GraphBadge.Info, 0).Map(removed));       // 落ちる
        Assert.Null(new EdgeHighlightDecoration(10, 0).Map(removed));                   // 辺も落ちる
        Assert.Null(new PortHintDecoration(new PortId(2, 0), 0).Map(removed));          // ポートも落ちる
        Assert.Null(new PendingWireDecoration(new PortId(2, 1), Vector2.Zero, 0).Map(removed));
        Assert.Null(new NodeInlineDecoration(2, 10, 10, "k").Map(removed));
    }

    // ---- Set / Table ----

    [Fact]
    public void Set_SortsAndReportsLayout()
    {
        var set = new GraphDecorationSet([
            new NodeBadgeDecoration(3, GraphBadge.Error, 0),
            new NodeBadgeDecoration(1, GraphBadge.Warning, 0),
        ]);
        Assert.Equal([1, 3], set.Decorations.Select(d => d.SortKey));   // SortKey 昇順
        Assert.False(set.AnyAffectsLayout);

        var withInline = new GraphDecorationSet([new NodeInlineDecoration(1, 10, 10, "k")]);
        Assert.True(withInline.AnyAffectsLayout);
    }

    [Fact]
    public void Set_Map_DropsDeadDecorations()
    {
        var doc = NodeGraphDoc.Of([N(1), N(2)]);
        var set = new GraphDecorationSet([new NodeBadgeDecoration(1, GraphBadge.Info, 0), new NodeBadgeDecoration(2, GraphBadge.Info, 0)]);
        var mapped = set.Map(doc.RemoveNode(2));
        Assert.Single(mapped.Decorations);
        Assert.Equal(1, mapped.Decorations[0].SortKey);
    }

    [Fact]
    public void Table_SetGetRemove()
    {
        var t = GraphDecorationTable.Empty;
        Assert.True(t.IsEmpty);

        var badges = new GraphDecorationSet([new NodeBadgeDecoration(1, GraphBadge.Error, 0)]);
        t = t.Set("diagnostics", badges);
        Assert.Equal(badges, t.Get("diagnostics"));
        Assert.Contains("diagnostics", t.Owners);

        t = t.Set("diagnostics", GraphDecorationSet.Empty);   // 空集合 = owner 削除
        Assert.True(t.IsEmpty);
    }

    // ---- State 統合: effect で装飾を載せる ----

    [Fact]
    public void State_WithDecorations_AppliesEffect()
    {
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1)]));
        Assert.True(s0.Decorations.IsEmpty);

        var set = new GraphDecorationSet([new NodeBadgeDecoration(1, GraphBadge.Error, 0xFFFF0000)]);
        var tr = s0.WithDecorations("diag", set);
        Assert.False(tr.DocChanged);   // 装飾のみ = 文書非変更 (履歴に積まれない)
        Assert.Same(set, tr.State.Decorations.Get("diag"));   // 文書非変更なので同一 set がそのまま流れる
    }

    // ---- State 統合: 編集で装飾が写る (削除対象は落ちる) ----

    [Fact]
    public void State_Edit_MapsDecorationsAndDropsRemoved()
    {
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1), N(2)], [E(10, 1, 2)]));
        s0 = s0.WithDecorations("diag", new GraphDecorationSet([
            new NodeBadgeDecoration(1, GraphBadge.Info, 0),
            new NodeBadgeDecoration(2, GraphBadge.Warning, 0),
            new EdgeHighlightDecoration(10, 0),
        ])).State;
        Assert.Equal(3, s0.Decorations.Get("diag")!.Count);

        // ノード 2 を削除 → その装飾と辺の装飾が落ち、ノード 1 のは残る
        var s1 = s0.Apply(new RemoveNode(2)).State;
        var diag = s1.Decorations.Get("diag")!;
        Assert.Single(diag.Decorations);
        Assert.Equal(1, diag.Decorations[0].SortKey);
    }

    [Fact]
    public void State_Effect_UsesNewDocCoordinates()
    {
        // 追加したノードにバッジを載せる effect が同一トランザクション内で成立する
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1)]));
        var badge = new GraphDecorationSet([new NodeBadgeDecoration(2, GraphBadge.Error, 0)]);
        var tr = s0.Update(new GraphTransactionSpec
        {
            Changes = [new AddNode(N(2))],
            Effects = [new SetGraphDecorations("diag", badge)],
        });
        // effect は新 Doc 基準なので、ノード 2 のバッジは生き残る
        Assert.Single(tr.State.Decorations.Get("diag")!.Decorations);
    }

    // ---- undo/redo で装飾が追従 ----

    [Fact]
    public void History_UndoRedo_RetainsDecorations()
    {
        var history = new GraphHistory();
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1), N(2)]));
        s0 = s0.WithDecorations("diag", new GraphDecorationSet([new NodeBadgeDecoration(1, GraphBadge.Info, 0)])).State;

        var tr = s0.Apply(new RemoveNode(1));   // ノード 1 削除 → バッジも落ちる
        history.Record(tr);
        var s1 = tr.State;
        Assert.True(s1.Decorations.IsEmpty);

        // undo でノード 1 は戻るが、装飾はコアには保存されない (provider が再供給する契約) —
        // undo 後もクラッシュせず、生きている装飾だけが保持されることを確認
        var undone = history.Undo(s1);
        Assert.True(undone.Doc.HasNode(1));
        Assert.True(undone.Decorations.IsEmpty);   // 落ちた装飾は戻らない (現状態の装飾を Retain するだけ)
    }

    // ---- provider ----

    [Fact]
    public void Provider_Collect_ProducesEffects()
    {
        var s0 = NodeGraphState.Create(NodeGraphDoc.Of([N(1), N(2)]));
        var provider = new SelectedNodesProvider();
        var s1 = s0.WithSelection(GraphSelection.Of([1, 2])).State;

        var effects = GraphDecorationProviders.Collect(s1, [provider]);
        var applied = s1.Update(new GraphTransactionSpec { Effects = effects }).State;
        Assert.Equal(2, applied.Decorations.Get("selected")!.Count);   // 選択 2 ノードにハイライト
    }

    // 選択ノードにバッジを付ける同期プロバイダ (テスト用)
    private sealed class SelectedNodesProvider : IGraphDecorationProvider
    {
        public string Owner => "selected";
        public GraphDecorationSet Provide(NodeGraphState state)
            => new(state.Selection.Nodes.Select(id => new NodeBadgeDecoration(id, GraphBadge.Info, 0xFF3399FF)));
    }
}
