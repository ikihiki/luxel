using Luxel.UI;

namespace Luxel.Tests;

/// <summary>浮遊 UI 配置ソルバ (ADR-0007 / ToDo 23) の単体テスト — フリップ/シフト/クランプ/揃え。
/// canvas 不要 (純関数)。viewport は 100x100 を基準に。</summary>
public class PopupPlacerTests
{
    private static readonly Rect VP = new(0, 0, 100, 100);

    private static PopupSolve Solve(Rect anchor, Size content, PopupSide side,
        PopupAlign align = PopupAlign.Start, float margin = 0, bool flip = true, bool shift = true, float maxH = 0)
        => PopupPlacer.Solve(anchor, content, VP, new AnchoredPlacement
        { Side = side, Align = align, Margin = margin, Flip = flip, Shift = shift, MaxHeight = maxH, Gap = 6 });

    [Fact]
    public void Below_Fits_PlacesBelow()
    {
        var s = Solve(new Rect(10, 10, 20, 10), new Size(40, 20), PopupSide.Below);
        Assert.Equal(PopupSide.Below, s.Side);
        Assert.Equal(26, s.Rect.Y, 1);      // 10 + 10 + gap(6)
        Assert.Equal(10, s.Rect.X, 1);      // Align.Start = anchor.X
    }

    [Fact]
    public void Below_NoRoom_FlipsAbove()
    {
        var s = Solve(new Rect(10, 85, 20, 10), new Size(40, 20), PopupSide.Below);
        Assert.Equal(PopupSide.Above, s.Side);
        Assert.Equal(59, s.Rect.Y, 1);      // 85 - gap(6) - 20
    }

    [Fact]
    public void Right_NoRoom_FlipsLeft()
    {
        var s = Solve(new Rect(85, 10, 10, 10), new Size(30, 20), PopupSide.Right);
        Assert.Equal(PopupSide.Left, s.Side);
        Assert.Equal(49, s.Rect.X, 1);      // 85 - gap(6) - 30
    }

    [Fact]
    public void Cross_ShiftsIntoView_AtRightEdge()
    {
        // アンカーが右端 → 下配置の x は画面内へシフト (clamp to viewport-width)
        var s = Solve(new Rect(90, 10, 10, 10), new Size(40, 20), PopupSide.Below);
        Assert.Equal(60, s.Rect.X, 1);      // clamp [0, 100-40]
    }

    [Fact]
    public void Cross_AlignCenterAndEnd()
    {
        var c = Solve(new Rect(40, 10, 20, 10), new Size(40, 20), PopupSide.Below, PopupAlign.Center);
        Assert.Equal(30, c.Rect.X, 1);      // 40 + (20-40)/2
        var e = Solve(new Rect(40, 10, 20, 10), new Size(40, 20), PopupSide.Below, PopupAlign.End);
        Assert.Equal(20, e.Rect.X, 1);      // 40 + 20 - 40
    }

    [Fact]
    public void SizeClamp_WhenTallerThanViewport()
    {
        // 中身が高すぎ・どちらも入らない → 広い側 (下) に寄せて高さを詰める
        var s = Solve(new Rect(10, 10, 20, 10), new Size(40, 200), PopupSide.Below);
        Assert.Equal(PopupSide.Below, s.Side);
        Assert.Equal(74, s.Rect.Height, 1);   // 100 - (20 + gap6) = 74
        Assert.Equal(26, s.Rect.Y, 1);
    }

    [Fact]
    public void MaxHeight_CapsExtent()
    {
        var s = Solve(new Rect(10, 10, 20, 10), new Size(40, 200), PopupSide.Below, maxH: 50);
        Assert.Equal(50, s.Rect.Height, 1);   // MaxHeight で頭打ち (下に 74 の余地あるが 50)
    }

    [Fact]
    public void Margin_KeepsOffEdges()
    {
        var s = Solve(new Rect(90, 10, 10, 10), new Size(40, 20), PopupSide.Below, margin: 8);
        Assert.True(s.Rect.X + s.Rect.Width <= 92.01f);   // 右端から margin 8
        Assert.True(s.Rect.X >= 8f);
    }

    [Fact]
    public void NoFlip_StaysDespiteOverflow()
    {
        // Flip=false なら入らなくても希望側のまま (サイズは詰まる)
        var s = Solve(new Rect(10, 85, 20, 10), new Size(40, 20), PopupSide.Below, flip: false);
        Assert.Equal(PopupSide.Below, s.Side);
    }
}
