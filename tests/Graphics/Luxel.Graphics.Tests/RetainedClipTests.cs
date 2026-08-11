using Luxel.Graphics.TwoD;
using Xunit;

namespace Luxel.Tests;

/// <summary>IC: クリップスロットの transform 部分更新追従 — 祖先クリップを焼き込んだ子孫の
/// スロットも、サブツリー移動 (ゴースト transform 等) で画面位置が再計算される。</summary>
public class RetainedClipTests
{
    [Fact]
    public void TransformPartialUpdate_RefreshesInheritedClipSlots()
    {
        var canvas = new RetainedCanvas();
        UiNode mover = canvas.AddChild(canvas.Root);                 // 動かす親 (クリップなし)
        UiNode clipper = canvas.AddChild(mover);
        clipper.Clip = new RectClip(0, 0, 50, 50);
        UiNode leaf = canvas.AddChild(clipper);                      // 自分はクリップを持たない子孫
        leaf.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 40, 40);
        leaf.Color = Color2D.Green;

        canvas.Flush(100, 100);                                      // フル再構築 (スロット割当)
        Assert.True(leaf.ClipSlot >= 0);
        GpuClip before = canvas.DebugClipAt(leaf.ClipSlot);
        Assert.Equal((0f, 0f, 50f, 50f), (before.MinX, before.MinY, before.MaxX, before.MaxY));

        mover.Transform = Affine2D.Translate(30, 40);                // ゴースト移動 (部分更新)
        canvas.Flush(100, 100);

        // 子孫の (祖先焼き込み) スロットも新しい画面位置へ
        GpuClip leafClip = canvas.DebugClipAt(leaf.ClipSlot);
        Assert.Equal((30f, 40f, 80f, 90f), (leafClip.MinX, leafClip.MinY, leafClip.MaxX, leafClip.MaxY));
        // クリップを持つノード自身のスロットも同様
        GpuClip clipperClip = canvas.DebugClipAt(clipper.ClipSlot);
        Assert.Equal((30f, 40f, 80f, 90f), (clipperClip.MinX, clipperClip.MinY, clipperClip.MaxX, clipperClip.MaxY));
    }

    [Fact]
    public void TransformPartialUpdate_KeepsAncestorIntersection()
    {
        // 祖先 (外側 60×60) と自分 (内側 50×50) の交差が、移動後も両方の現在位置で交差される
        var canvas = new RetainedCanvas();
        UiNode outer = canvas.AddChild(canvas.Root);
        outer.Clip = new RectClip(0, 0, 60, 60);
        UiNode mover = canvas.AddChild(outer);
        UiNode inner = canvas.AddChild(mover);
        inner.Clip = new RectClip(0, 0, 50, 50);
        inner.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 40, 40);

        canvas.Flush(100, 100);
        mover.Transform = Affine2D.Translate(30, 0);                 // 内側だけ移動 (外側 60 は固定)
        canvas.Flush(100, 100);

        GpuClip c = canvas.DebugClipAt(inner.ClipSlot);
        // 内側 (30..80) ∩ 外側 (0..60) = 30..60
        Assert.Equal((30f, 0f, 60f, 50f), (c.MinX, c.MinY, c.MaxX, c.MaxY));
    }
}
