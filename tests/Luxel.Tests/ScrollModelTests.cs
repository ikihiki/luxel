using Luxel.Controls;
using Luxel.UI;
using Xunit;

namespace Luxel.Tests;

/// <summary>ScrollModel — スクロール計算の共通機構 (クランプ/寸法変更追従/サム幾何/ドラッグ写像)。</summary>
public class ScrollModelTests
{
    [Fact]
    public void ScrollTo_ClampsToRange()
    {
        var m = new ScrollModel();
        m.SetLengths(content: 1000, viewport: 200);
        m.ScrollTo(500);
        Assert.Equal(500, m.ClampedPeek);
        m.ScrollTo(-50);
        Assert.Equal(0, m.ClampedPeek);
        m.ScrollTo(9999);
        Assert.Equal(800, m.ClampedPeek);   // MaxScroll = 1000 - 200
    }

    [Fact]
    public void ScrollBy_AccumulatesAndClamps()
    {
        var m = new ScrollModel();
        m.SetLengths(1000, 200);
        m.ScrollBy(300);
        m.ScrollBy(300);
        Assert.Equal(600, m.ClampedPeek);
        m.ScrollBy(9999);
        Assert.Equal(800, m.ClampedPeek);
    }

    /// <summary>幅変更の核心 — 内容長が変わっても位置は保たれ、縮んだ分だけクランプされる。</summary>
    [Fact]
    public void SetLengths_PreservesOffsetAndClamps()
    {
        var m = new ScrollModel();
        m.SetLengths(2000, 500);
        m.ScrollTo(1200);

        m.SetLengths(2100, 500);            // 内容が伸びた (折返し変化) — 位置そのまま
        Assert.Equal(1200, m.ClampedPeek);

        m.SetLengths(1400, 500);            // 内容が縮んだ — 新しい末尾へクランプ
        Assert.Equal(900, m.ClampedPeek);

        m.SetLengths(400, 500);             // 収まった — 先頭へ
        Assert.Equal(0, m.ClampedPeek);
        Assert.Equal(0, m.MaxScroll);
    }

    /// <summary>同じ寸法での SetLengths は signal を揺らさない (レイアウト毎の呼び出しを許容)。</summary>
    [Fact]
    public void SetLengths_DedupsUnchangedValues()
    {
        var m = new ScrollModel();
        m.SetLengths(1000, 200);
        m.ScrollTo(100);

        int fired = 0;
        using IDisposable e = Reactive.Effect(() => { _ = m.Clamped; fired++; });
        Assert.Equal(1, fired);   // 登録時の初回実行
        m.SetLengths(1000, 200);
        m.SetLengths(1000, 200);
        Assert.Equal(1, fired);   // 同値 → 再実行なし
    }

    /// <summary>回帰: 幅変更 (内容高の変化) でも content transform 相当の effect が
    /// 再実行され、保持していた位置まで自動でスクロールし直される。
    /// (旧実装はオフセットへの同値クランプ書き込みだけで、effect が実体化時に掴んだ 0 のまま
    /// 取り残されて先頭に戻っていた。)</summary>
    [Fact]
    public void Clamped_EffectRefiresWhenContentLengthChanges()
    {
        var m = new ScrollModel();
        m.SetLengths(2000, 500);
        m.ScrollTo(1200);

        float applied = -1;   // = content.Transform に流れる値
        using IDisposable e = Reactive.Effect(() => applied = m.Clamped);
        Assert.Equal(1200, applied);

        m.SetLengths(2400, 500);   // 折返し変化で内容が伸びた — 位置は保たれたまま再適用
        Assert.Equal(1200, applied);

        m.SetLengths(1000, 500);   // 縮んだ — 新しい末尾へクランプして再適用
        Assert.Equal(500, applied);
    }

    [Fact]
    public void EnsureVisible_MovesMinimally()
    {
        var m = new ScrollModel();
        m.SetLengths(1000, 200);

        m.EnsureVisible(500, 520, pad: 10);   // 下方向: bottom + pad が収まるまで
        Assert.Equal(330, m.ClampedPeek);     // 520 - 200 + 10

        m.EnsureVisible(400, 420, pad: 10);   // 既に見えている → 動かない
        Assert.Equal(330, m.ClampedPeek);

        m.EnsureVisible(100, 120, pad: 10);   // 上方向: top - pad へ
        Assert.Equal(90, m.ClampedPeek);
    }

    [Fact]
    public void ThumbGeometry_ProportionalWithMinimum()
    {
        var m = new ScrollModel();
        m.SetLengths(1000, 200);
        Assert.Equal(40, m.ThumbLength(28));          // 200 * 200 / 1000

        m.SetLengths(100000, 200);
        Assert.Equal(28, m.ThumbLength(28));          // 最小長で下支え

        m.SetLengths(100, 200);                       // 収まる → トラック全長
        Assert.Equal(200, m.ThumbLength(28));
    }

    [Fact]
    public void ThumbPos_And_OffsetForThumbTop_RoundTrip()
    {
        var m = new ScrollModel();
        m.SetLengths(1000, 200);
        m.ScrollTo(400);                               // MaxScroll = 800 の中央
        float th = m.ThumbLength(28);                  // 40
        float pos = m.ThumbPos(m.ClampedPeek, 28);
        Assert.Equal((200 - th) / 2, pos, 3);          // トラック中央

        // 逆写像: サム上端位置 → 元のオフセット
        Assert.Equal(400, m.OffsetForThumbTop(pos, 28), 3);
    }

    [Fact]
    public void BeginThumbDrag_GrabOnThumbVsTrackJump()
    {
        var m = new ScrollModel();
        m.SetLengths(1000, 200);
        m.ScrollTo(0);                                 // thumb は先頭 (top=0, 長さ 40)

        Assert.Equal(10, m.BeginThumbDrag(10, 28));    // サム上 → 食い込み位置
        Assert.Equal(20, m.BeginThumbDrag(150, 28));   // トラック上 → サム中央掴み (40/2)
    }

    [Fact]
    public void NoScrollableRange_IsInert()
    {
        var m = new ScrollModel();
        m.SetLengths(100, 200);
        m.ScrollBy(50);
        Assert.Equal(0, m.ClampedPeek);
        Assert.Equal(0, m.ThumbPos(0, 28));
        Assert.Equal(0, m.OffsetForThumbTop(50, 28));
    }
}
