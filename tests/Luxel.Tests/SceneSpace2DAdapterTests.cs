using System.Numerics;
using Luxel.Controls;
using Luxel.SceneEdit;

namespace Luxel.Tests;

/// <summary>2D 空間アダプタ (ToDo 27 GE-1 / ADR-0016) の canvas 非依存部の単体テスト —
/// BuildMove の軸拘束/スナップ、HitEntity の最前面優先、EntitiesIn、OffsetDuplicate。
/// 描画 (Attach/Refresh) は story の golden が担保する。</summary>
public class SceneSpace2DAdapterTests
{
    private static SceneEntity E(int id, float x, float y)
        => SceneEntity.Of(id, $"e{id}", SceneSchemas.NewComponent(SceneSchemas.Transform2D)
            .With("pos", SceneValue.Of(new Vector2(x, y))));

    private static Vector2 PosOf(SceneDoc doc, int id) => doc.Entity(id).Component("transform2d")!.Get("pos")!.Value.AsVec2();

    [Fact]
    public void BuildMove_AxisConstraintAndSnap()
    {
        var ad = new SceneSpace2DAdapter();   // zoom=1 なので screenDelta = worldDelta
        SceneDoc doc = SceneDoc.Of(SceneSpace.TwoD, [E(1, 100, 100)]);

        // 自由移動
        SceneDoc moved = new SceneChangeSet(ad.BuildMove(doc, [1], new Vector2(50, 30), SceneHandleKind.Free, snap: false)).Apply(doc);
        Assert.Equal(new Vector2(150, 130), PosOf(moved, 1));
        // X 軸拘束 — Y 成分が落ちる
        moved = new SceneChangeSet(ad.BuildMove(doc, [1], new Vector2(50, 30), SceneHandleKind.AxisX, snap: false)).Apply(doc);
        Assert.Equal(new Vector2(150, 100), PosOf(moved, 1));
        // スナップ (GridStep=32): 150,130 → 160,128
        moved = new SceneChangeSet(ad.BuildMove(doc, [1], new Vector2(50, 30), SceneHandleKind.Free, snap: true)).Apply(doc);
        Assert.Equal(new Vector2(160, 128), PosOf(moved, 1));
        // transform2d の無いエンティティは変更を作らない
        SceneDoc noT = SceneDoc.Of(SceneSpace.TwoD, [SceneEntity.Of(9, "ghost")]);
        Assert.Empty(ad.BuildMove(noT, [9], new Vector2(50, 30), SceneHandleKind.Free, snap: false));
    }

    [Fact]
    public void HitEntity_TopmostWins_AndMissReturnsMinusOne()
    {
        var ad = new SceneSpace2DAdapter();   // BoxSize 96x40、中心基準
        // 同じ場所に 2 個 — リスト末尾 (後に描かれる方) が勝つ
        SceneDoc doc = SceneDoc.Of(SceneSpace.TwoD, [E(1, 100, 100), E(2, 110, 100)]);
        Assert.Equal(2, ad.HitEntity(doc, new Vector2(110, 100)));
        Assert.Equal(1, ad.HitEntity(doc, new Vector2(56, 100)));    // e1 だけの領域 (左端 52)
        Assert.Equal(-1, ad.HitEntity(doc, new Vector2(400, 300)));
    }

    [Fact]
    public void EntitiesIn_IntersectsBoxes()
    {
        var ad = new SceneSpace2DAdapter();
        SceneDoc doc = SceneDoc.Of(SceneSpace.TwoD, [E(1, 100, 100), E(2, 300, 100), E(3, 600, 400)]);
        Assert.Equal([1, 2], ad.EntitiesIn(doc, new Vector2(10, 60), new Vector2(360, 140)));
        Assert.Empty(ad.EntitiesIn(doc, new Vector2(0, 0), new Vector2(10, 10)));
    }

    [Fact]
    public void OffsetDuplicate_ShiftsPos()
    {
        var ad = new SceneSpace2DAdapter();
        SceneEntity moved = ad.OffsetDuplicate(E(1, 100, 100));
        Assert.Equal(new Vector2(124, 124), moved.Component("transform2d")!.Get("pos")!.Value.AsVec2());
        // transform2d 無しはそのまま
        SceneEntity ghost = SceneEntity.Of(9, "ghost");
        Assert.Same(ghost, ad.OffsetDuplicate(ghost));
    }
}
