using Luxel.Graphics.TwoD;
using Xunit;

namespace Luxel.Tests;

/// <summary>角丸クリップの親連鎖と transform 部分更新を検証する。</summary>
public class RetainedClipTests
{
    [Fact]
    public void TransformPartialUpdate_RefreshesOwnedClipAndInheritedReference()
    {
        var canvas = new RetainedCanvas();
        UiNode mover = canvas.AddChild(canvas.Root);                 // 動かす親 (クリップなし)
        UiNode clipper = canvas.AddChild(mover);
        clipper.Clip = new RectClip(0, 0, 50, 50, 8);
        UiNode leaf = canvas.AddChild(clipper);                      // 自分はクリップを持たない子孫
        leaf.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 40, 40);
        leaf.Color = Color2D.Green;

        canvas.Flush(100, 100);                                      // フル再構築 (スロット割当)
        Assert.True(leaf.ClipSlot >= 0);
        Assert.Equal(clipper.ClipSlot, leaf.ClipSlot);
        GpuClip before = canvas.DebugClipAt(leaf.ClipSlot);
        Assert.Equal((0f, 0f, 50f, 50f), (before.MinX, before.MinY, before.MaxX, before.MaxY));
        Assert.Equal((8f, 8f, (uint)RectCorners.All, GpuPath.NoClip),
            (before.RadiusX, before.RadiusY, before.Corners, before.ParentSlot));

        mover.Transform = Affine2D.Translate(30, 40);                // ゴースト移動 (部分更新)
        canvas.Flush(100, 100);

        // 子孫は祖先のスロットを直接参照するため、同じ更新結果を見る。
        GpuClip leafClip = canvas.DebugClipAt(leaf.ClipSlot);
        Assert.Equal((30f, 40f, 80f, 90f), (leafClip.MinX, leafClip.MinY, leafClip.MaxX, leafClip.MaxY));
        // クリップを持つノード自身のスロットも同様
        GpuClip clipperClip = canvas.DebugClipAt(clipper.ClipSlot);
        Assert.Equal((30f, 40f, 80f, 90f), (clipperClip.MinX, clipperClip.MinY, clipperClip.MaxX, clipperClip.MaxY));
    }

    [Fact]
    public void NestedClips_PreserveBothShapesAsAParentChain()
    {
        // 祖先 (外側 60×60) と自分 (内側 50×50) の交差が、移動後も両方の現在位置で交差される
        var canvas = new RetainedCanvas();
        UiNode outer = canvas.AddChild(canvas.Root);
        outer.Clip = new RectClip(0, 0, 60, 60, 10);
        UiNode mover = canvas.AddChild(outer);
        UiNode inner = canvas.AddChild(mover);
        inner.Clip = new RectClip(0, 0, 50, 50, 6,
            RectCorners.TopLeft | RectCorners.BottomRight);
        inner.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 40, 40);

        canvas.Flush(100, 100);
        mover.Transform = Affine2D.Translate(30, 0);                 // 内側だけ移動 (外側 60 は固定)
        canvas.Flush(100, 100);

        GpuClip c = canvas.DebugClipAt(inner.ClipSlot);
        Assert.Equal((30f, 0f, 80f, 50f), (c.MinX, c.MinY, c.MaxX, c.MaxY));
        Assert.Equal((uint)outer.ClipSlot, c.ParentSlot);
        Assert.Equal((uint)(RectCorners.TopLeft | RectCorners.BottomRight), c.Corners);
        GpuClip parent = canvas.DebugClipAt((int)c.ParentSlot);
        Assert.Equal((0f, 0f, 60f, 60f), (parent.MinX, parent.MinY, parent.MaxX, parent.MaxY));
        Assert.Equal(GpuPath.NoClip, parent.ParentSlot);
    }
}
