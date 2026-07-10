using System.Numerics;
using Luxel.SceneEdit;

namespace Luxel.Tests;

/// <summary>シーンエディタ GE-1 S1 コア (ToDo 27 / ADR-0016) の単体テスト — SceneChange の
/// Apply/Invert 往復、SceneChangeSet の反転、SceneSelection の正規化/Retain、
/// SceneEditState/SceneTransaction (複数変更 = 1 undo)、SceneHistory (undo/redo/coalesce)、
/// SceneCommands (削除/複製)。canvas 不要 (純データ)。</summary>
public class SceneEditChangeTests
{
    // ---- 組み立てヘルパ ----

    private static SceneComponent T2(float x, float y)
        => SceneSchemas.NewComponent(SceneSchemas.Transform2D).With("pos", SceneValue.Of(new Vector2(x, y)));

    private static SceneEntity E(int id, float x = 0, float y = 0) => SceneEntity.Of(id, $"e{id}", T2(x, y));

    private static SceneDoc Doc3() => SceneDoc.Of(SceneSpace.TwoD, [E(1, 10, 10), E(2, 100, 50), E(3, 200, 90)]);

    private static Vector2 PosOf(SceneDoc doc, int id) => doc.Entity(id).Component("transform2d")!.Get("pos")!.Value.AsVec2();

    // 変更列を適用 → 反転適用で元の JSON に戻ることを一括検証
    private static void AssertRoundTrip(SceneDoc start, params SceneChange[] changes)
    {
        var set = new SceneChangeSet(changes);
        SceneDoc after = set.Apply(start);
        SceneDoc back = set.InvertAgainst(start).Apply(after);
        Assert.Equal(SceneJson.Serialize(start), SceneJson.Serialize(back));
    }

    // ---- 個別変更の Apply/Invert ----

    [Fact]
    public void Change_AddRemoveEntity_RoundTrips()
    {
        SceneDoc doc = Doc3();
        AssertRoundTrip(doc, new AddEntity(E(4, 5, 5)));
        AssertRoundTrip(doc, new RemoveEntity(2));   // 逆 = コンポーネントごと復活
        Assert.False(new RemoveEntity(2).Apply(doc).HasEntity(2));
    }

    [Fact]
    public void Change_RenameAndSetComponent_RoundTrip()
    {
        SceneDoc doc = Doc3();
        AssertRoundTrip(doc, new RenameEntity(1, "renamed"));
        // 置換 (既存 transform2d) と追加 (新規型) の両方
        AssertRoundTrip(doc, new SetComponent(1, T2(77, 88)));
        AssertRoundTrip(doc, new SetComponent(1, SceneComponent.Of("tint", ("color", SceneValue.Of(new Vector4(1, 0, 0, 1))))));
        AssertRoundTrip(doc,
            new SetComponent(1, SceneComponent.Of("tint", ("color", SceneValue.Of(new Vector4(1, 0, 0, 1))))),
            new RemoveComponent(1, "tint"));
    }

    [Fact]
    public void Change_SetField_MovesAndRoundTrips()
    {
        SceneDoc doc = Doc3();
        var move = new SetField(2, "transform2d", "pos", SceneValue.Of(new Vector2(150, 60)));
        Assert.Equal(new Vector2(150, 60), PosOf(move.Apply(doc), 2));
        AssertRoundTrip(doc, move);
        // 無いコンポーネント/フィールドは例外 (呼び側の判定ミスを隠さない)
        Assert.Throws<KeyNotFoundException>(() => new SetField(2, "nope", "pos", SceneValue.Of(1)).Apply(doc));
        Assert.Throws<KeyNotFoundException>(() => new SetField(2, "transform2d", "nope", SceneValue.Of(1)).Apply(doc));
    }

    [Fact]
    public void ChangeSet_InvertAgainst_RestoresStart_AcrossDependentChanges()
    {
        // 依存する変更列: 追加 → その entity を移動 → 別 entity を削除
        SceneDoc doc = Doc3();
        AssertRoundTrip(doc,
            new AddEntity(E(9, 1, 1)),
            new SetField(9, "transform2d", "pos", SceneValue.Of(new Vector2(64, 64))),
            new RemoveEntity(1));
    }

    // ---- 選択 ----

    [Fact]
    public void Selection_NormalizesAndRetains()
    {
        var sel = SceneSelection.Of([3, 1, 3, 2], main: 1);
        Assert.Equal([1, 2, 3], sel.Entities);
        Assert.Equal(1, sel.Main);
        // main が含まれなければ末尾
        Assert.Equal(3, SceneSelection.Of([1, 3], main: 99).Main);
        // Retain: 削除された参照を落とす
        SceneDoc doc = new RemoveEntity(1).Apply(Doc3());
        SceneSelection kept = sel.Retain(doc);
        Assert.Equal([2, 3], kept.Entities);
        Assert.Equal(3, kept.Main);   // main=1 が消えたので末尾へ
    }

    // ---- Transaction + History ----

    [Fact]
    public void Transaction_MultiChange_IsOneUndo_AndRestoresSelection()
    {
        var history = new SceneHistory();
        SceneEditState s0 = SceneEditState.Create(Doc3(), SceneSelection.Of([1, 2]));
        // 複数エンティティの移動 = 1 トランザクション
        SceneTransaction tr = s0.Update(new SceneTransactionSpec
        {
            Changes =
            [
                new SetField(1, "transform2d", "pos", SceneValue.Of(new Vector2(20, 20))),
                new SetField(2, "transform2d", "pos", SceneValue.Of(new Vector2(110, 60))),
            ],
        });
        history.Record(tr);
        SceneEditState s1 = tr.State;
        Assert.Equal(new Vector2(20, 20), PosOf(s1.Doc, 1));
        Assert.Equal(1, history.UndoDepth);

        SceneEditState undone = history.Undo(s1);   // 1 手で両方戻る
        Assert.Equal(new Vector2(10, 10), PosOf(undone.Doc, 1));
        Assert.Equal(new Vector2(100, 50), PosOf(undone.Doc, 2));
        Assert.Equal([1, 2], undone.Selection.Entities);   // Before の選択が戻る

        SceneEditState redone = history.Redo(undone);
        Assert.Equal(new Vector2(20, 20), PosOf(redone.Doc, 1));
    }

    [Fact]
    public void History_Coalesce_FoldsConsecutiveMoves()
    {
        var history = new SceneHistory();
        SceneEditState s = SceneEditState.Create(Doc3());
        for (int i = 1; i <= 3; i++)
        {
            SceneTransaction tr = s.Apply(new SetField(1, "transform2d", "pos", SceneValue.Of(new Vector2(10 + i * 5, 10))));
            history.Record(tr, coalesce: i > 1);
            s = tr.State;
        }
        Assert.Equal(1, history.UndoDepth);   // 3 移動が 1 undo に畳まれた
        Assert.Equal(new Vector2(10, 10), PosOf(history.Undo(s).Doc, 1));
    }

    [Fact]
    public void History_SelectionOnlyTransaction_IsNotRecorded()
    {
        var history = new SceneHistory();
        SceneEditState s = SceneEditState.Create(Doc3());
        history.Record(s.WithSelection(SceneSelection.Single(2)));
        Assert.False(history.CanUndo);
    }

    // ---- コマンド ----

    [Fact]
    public void Command_DeleteSelection_RemovesAllSelected()
    {
        SceneEditState s = SceneEditState.Create(Doc3(), SceneSelection.Of([1, 3]));
        SceneEditState after = SceneCommands.DeleteSelection(s).State;
        Assert.Equal([2], after.Doc.Entities.Select(e => e.Id));
        Assert.True(after.Selection.IsEmpty);
    }

    [Fact]
    public void Command_DuplicateSelection_ClonesWithNewIdsAndOffset()
    {
        SceneEditState s = SceneEditState.Create(Doc3(), SceneSelection.Of([1, 2]));
        SceneEditState after = SceneCommands.DuplicateSelection(s,
            e => e.WithComponent(e.Component("transform2d")!.With("pos",
                SceneValue.Of(e.Component("transform2d")!.Get("pos")!.Value.AsVec2() + new Vector2(24, 24))))).State;
        Assert.Equal(5, after.Doc.Entities.Count);
        Assert.Equal([4, 5], after.Selection.Entities);            // 複製が選択される
        Assert.Equal(new Vector2(34, 34), PosOf(after.Doc, 4));    // e1 (10,10) + 24
        Assert.Equal("e1", after.Doc.Entity(4).Name);
        // 空選択の複製は no-op
        Assert.False(SceneCommands.DuplicateSelection(SceneEditState.Create(Doc3())).DocChanged);
    }
}
