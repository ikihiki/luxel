using Luxel.Editor;

namespace Luxel.Tests;

/// <summary>エディタ新スタック S2 (ToDo 22 / ADR-0006) — 装飾を第一級状態として持つ機構の単体テスト。
/// Decoration の編集追従写像、レイアウト依存分類、DecorationSet/Table、プロバイダ契約、非同期の古い結果写像、
/// EditorState への統合と undo 追従。canvas 不要。</summary>
public class EditorDecorationTests
{
    private static uint Red = 0xFFFF0000, Blue = 0xFF0000FF;

    // ---- Mark の写像 ----

    [Fact]
    public void Mark_MapsThroughInsertBefore()
    {
        // "abcdef" の [2,4) にマーク。先頭に "XX" 挿入 → [4,6) へ
        var m = new MarkDecoration(2, 4, Background: Red);
        var cs = ChangeSet.Of(6, [new ChangeSpec(0, 0, "XX")]);
        var m2 = (MarkDecoration)m.Map(cs)!;
        Assert.Equal(4, m2.From);
        Assert.Equal(6, m2.To);
    }

    [Fact]
    public void Mark_TypingInsideExtends_BoundaryDoesNot()
    {
        // [2,4) に排他マーク。範囲内 (pos 3) に "Q" を打つと範囲が伸びる
        var m = new MarkDecoration(2, 4, Background: Red);
        var inside = (MarkDecoration)m.Map(ChangeSet.Of(6, [new ChangeSpec(3, 3, "Q")]))!;
        Assert.Equal(2, inside.From);
        Assert.Equal(5, inside.To);                       // 4 → 5 (内側で伸長)

        // 終端 (pos 4) の外側への挿入は含めない (排他)
        var atEnd = (MarkDecoration)m.Map(ChangeSet.Of(6, [new ChangeSpec(4, 4, "Q")]))!;
        Assert.Equal(4, atEnd.To);                        // 伸びない
    }

    [Fact]
    public void Mark_InclusiveEnd_ExtendsAtBoundary()
    {
        var m = new MarkDecoration(2, 4, Background: Red, InclusiveEnd: true);
        var atEnd = (MarkDecoration)m.Map(ChangeSet.Of(6, [new ChangeSpec(4, 4, "Q")]))!;
        Assert.Equal(5, atEnd.To);                        // 包含なので伸びる
    }

    [Fact]
    public void Mark_FullyDeleted_IsDropped()
    {
        var m = new MarkDecoration(2, 4, Underline: new UnderlineStyle(Red, Wavy: true));
        Assert.Null(m.Map(ChangeSet.Of(6, [new ChangeSpec(1, 5, "")])));   // 範囲を含む削除 → 消える
    }

    // ---- レイアウト依存の分類 ----

    [Fact]
    public void AffectsLayout_Classification()
    {
        Assert.True(new MarkDecoration(0, 1, Foreground: Blue).AffectsLayout);          // 色 = 効く
        Assert.False(new MarkDecoration(0, 1, Background: Blue).AffectsLayout);         // 背景 = 効かない
        Assert.False(new MarkDecoration(0, 1, Box: new BoxStyle(Blue)).AffectsLayout);  // 囲み = 効かない
        Assert.True(new WidgetDecoration(2, 2, 20, 12, "w").AffectsLayout);             // widget = 効く
        Assert.True(new LinePrefixDecoration(0, "1.", Blue).AffectsLayout);             // 行頭 = 効く
        Assert.False(new LineDecoration(0, Blue).AffectsLayout);                        // 行背景 = 効かない
        Assert.False(new BlockDecoration(0, 5).AffectsLayout);                          // ブロック = 効かない
    }

    // ---- Widget / Line / Block の写像 ----

    [Fact]
    public void Widget_Anchor_MapsPoint()
    {
        var w = new WidgetDecoration(3, 3, 20, 12, "slider");
        var w2 = (WidgetDecoration)w.Map(ChangeSet.Of(6, [new ChangeSpec(0, 0, "XX")]))!;
        Assert.True(w2.IsAnchor);
        Assert.Equal(5, w2.From);
    }

    [Fact]
    public void Widget_Replace_DroppedWhenTargetGone()
    {
        var w = new WidgetDecoration(2, 4, 20, 12, "img");
        Assert.Null(w.Map(ChangeSet.Of(6, [new ChangeSpec(1, 5, "")])));
    }

    [Fact]
    public void LineDecoration_FollowsItsLine()
    {
        // 2 行目 (offset 4) の行背景。先頭行に文字挿入で offset がずれても追従
        var d = new LineDecoration(4, Red);
        var d2 = (LineDecoration)d.Map(ChangeSet.Of(6, [new ChangeSpec(0, 0, "ZZ")]))!;
        Assert.Equal(6, d2.At);
    }

    // ---- DecorationSet ----

    [Fact]
    public void DecorationSet_SortsAndKeepsOverlaps()
    {
        var set = new DecorationSet(
        [
            new MarkDecoration(5, 8, Background: Red),
            new MarkDecoration(0, 3, Foreground: Blue),
            new MarkDecoration(1, 4, Underline: new UnderlineStyle(Red)),  // [0,3) と重なる
        ]);
        Assert.Equal(3, set.Count);                       // 重なりは共存
        Assert.Equal(0, set.Decorations[0].SortFrom);     // From 昇順
        Assert.Equal(1, set.Decorations[1].SortFrom);
        Assert.Equal(5, set.Decorations[2].SortFrom);
    }

    [Fact]
    public void DecorationSet_MapDropsInvalid()
    {
        var set = new DecorationSet(
        [
            new MarkDecoration(0, 2, Background: Red),
            new MarkDecoration(3, 5, Background: Blue),   // これは削除で消える
        ]);
        var mapped = set.Map(ChangeSet.Of(6, [new ChangeSpec(3, 6, "")]));
        Assert.Equal(1, mapped.Count);
        Assert.Equal(0, mapped.Decorations[0].SortFrom);
    }

    [Fact]
    public void DecorationSet_Touching()
    {
        var set = new DecorationSet([new MarkDecoration(0, 3), new MarkDecoration(5, 8), new LineDecoration(10, Red)]);
        Assert.Equal(2, set.Touching(2, 6).Count());      // [0,3) と [5,8)
    }

    // ---- DecorationTable ----

    [Fact]
    public void Table_OwnersIndependent()
    {
        DecorationTable t = DecorationTable.Empty
            .Set("syntax", new DecorationSet([new MarkDecoration(0, 3, Foreground: Blue)]))
            .Set("search", new DecorationSet([new MarkDecoration(1, 2, Background: Red)]));
        Assert.Equal(2, t.Owners.Count);
        Assert.Equal(1, t.Get("syntax")!.Count);

        t = t.Set("syntax", DecorationSet.Empty);         // 空を渡すと owner 除去
        Assert.Null(t.Get("syntax"));
        Assert.NotNull(t.Get("search"));
    }

    [Fact]
    public void Table_MapAllOwners()
    {
        DecorationTable t = DecorationTable.Empty
            .Set("a", new DecorationSet([new MarkDecoration(2, 4, Background: Red)]))
            .Set("b", new DecorationSet([new LineDecoration(4, Blue)]));
        DecorationTable mapped = t.Map(ChangeSet.Of(6, [new ChangeSpec(0, 0, "XX")]));
        Assert.Equal(4, ((MarkDecoration)mapped.Get("a")!.Decorations[0]).From);
        Assert.Equal(6, ((LineDecoration)mapped.Get("b")!.Decorations[0]).At);
    }

    // ---- EditorState への統合 ----

    [Fact]
    public void State_EditMapsExistingDecorations()
    {
        var s0 = EditorState.Create("abcdef")
            .WithDecorations("m", new DecorationSet([new MarkDecoration(2, 4, Background: Red)])).State;
        Assert.Equal(1, s0.Decorations.Get("m")!.Count);

        // 先頭に挿入すると既存装飾が写る
        EditorState s1 = s0.Update(new TransactionSpec { Changes = [new ChangeSpec(0, 0, "XX")] }).State;
        var m = (MarkDecoration)s1.Decorations.Get("m")!.Decorations[0];
        Assert.Equal(4, m.From);
        Assert.Equal(6, m.To);
    }

    [Fact]
    public void State_EffectSetsNewCoords_ExistingMapped()
    {
        // 1 トランザクションで編集 + 別 owner の装飾差し替え: 新 set は新座標のまま、既存は写る
        var s0 = EditorState.Create("abcdef")
            .WithDecorations("old", new DecorationSet([new MarkDecoration(0, 2, Background: Red)])).State;

        EditorState s1 = s0.Update(new TransactionSpec
        {
            Changes = [new ChangeSpec(0, 0, "XX")],       // "XXabcdef"
            Effects = [new SetDecorations("new", new DecorationSet([new MarkDecoration(0, 2, Background: Blue)]))],
        }).State;

        var oldM = (MarkDecoration)s1.Decorations.Get("old")!.Decorations[0];
        Assert.Equal(2, oldM.From);                       // 既存は +2 写像
        var newM = (MarkDecoration)s1.Decorations.Get("new")!.Decorations[0];
        Assert.Equal(0, newM.From);                       // 新 set は新座標そのまま
    }

    // ---- プロバイダ契約 ----

    // 部分文字列の全出現に背景マークを付ける同期プロバイダ (テスト用)
    private sealed class HighlightAllProvider(string needle) : IDecorationProvider
    {
        public string Owner => "highlight";
        public DecorationSet Provide(EditorState state)
        {
            var marks = new List<Decoration>();
            string t = state.Doc.Text;
            int i = 0;
            while ((i = t.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
            {
                marks.Add(new MarkDecoration(i, i + needle.Length, Background: 0xFFFFFF00));
                i += needle.Length;
            }
            return new DecorationSet(marks);
        }
    }

    [Fact]
    public void Provider_SyncCollectAndApply()
    {
        var s0 = EditorState.Create("ab ab ab");
        IReadOnlyList<StateEffect> effects = DecorationProviders.Collect(s0, [new HighlightAllProvider("ab")]);
        EditorState s1 = s0.Update(new TransactionSpec { Effects = effects }).State;
        Assert.Equal(3, s1.Decorations.Get("highlight")!.Count);   // 3 箇所
    }

    [Fact]
    public void Provider_AsyncStaleResult_RemappedThroughEdits()
    {
        // プロバイダが古い状態 v0 に対して装飾を出す → その間に編集 → 発行時からの ChangeSet で写してから set
        var v0 = EditorState.Create("ab ab ab");
        DecorationSet staleSet = new HighlightAllProvider("ab").Provide(v0);   // [0,2)[3,5)[6,8)

        // v0 → v1: 先頭に "ZZ" 挿入 (非同期結果が返るより前に起きた編集)
        Transaction edit = v0.Update(new TransactionSpec { Changes = [new ChangeSpec(0, 0, "ZZ")] });
        EditorState v1 = edit.State;

        // 古い結果を発行時からの変更で写してから適用 (view がやる配線)
        DecorationSet remapped = staleSet.Map(edit.Changes);
        EditorState v1b = v1.WithDecorations("highlight", remapped).State;

        var first = (MarkDecoration)v1b.Decorations.Get("highlight")!.Decorations[0];
        Assert.Equal(2, first.From);                      // 0 → 2 に写っている (ずれない)
        Assert.Equal(3, v1b.Decorations.Get("highlight")!.Count);
    }

    // ---- undo/redo 追従 ----

    [Fact]
    public void History_PreservesDecorations()
    {
        var s0 = EditorState.Create("abcdef")
            .WithDecorations("m", new DecorationSet([new MarkDecoration(4, 6, Background: Red)])).State;
        var h = new History();

        Transaction t = s0.Update(new TransactionSpec { Changes = [new ChangeSpec(0, 0, "XX")] });
        h.Record(t);
        EditorState s1 = t.State;
        Assert.Equal(6, ((MarkDecoration)s1.Decorations.Get("m")!.Decorations[0]).From);  // 4 → 6

        EditorState back = h.Undo(s1);
        Assert.Equal(4, ((MarkDecoration)back.Decorations.Get("m")!.Decorations[0]).From); // 逆写像で 6 → 4
    }
}
