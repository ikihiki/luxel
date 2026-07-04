using Luxel.Animation;
using Xunit;

namespace Luxel.Tests;

/// <summary>AS-M1/M3: UI アニメの計算部 (Luxel.Animation)。AnimatedValue は
/// PropertyStateMachine への統一移行で削除 — 意味論のテストは PropertyStateMachineTests に。</summary>
public class MotionTests
{
    [Fact]
    public void PolynomialCurves_Endpoints_And_Shape()
    {
        // 旧 Luxel.UI Easing から移設した多項式カーブ (AS-M1) — 端点は正確、OutCubic は前半が速い
        Assert.Equal(0f, OutCubicCurve.Instance.Eval(0));
        Assert.Equal(1f, OutCubicCurve.Instance.Eval(1));
        Assert.True(OutCubicCurve.Instance.Eval(0.5f) > 0.5f);
        Assert.Equal(0f, InOutCubicCurve.Instance.Eval(0));
        Assert.Equal(1f, InOutCubicCurve.Instance.Eval(1));
        Assert.Equal(0.5f, InOutCubicCurve.Instance.Eval(0.5f), 3);
    }

    [Fact]
    public void RgbaTween_Endpoints_And_Middle()
    {
        uint a = 0xFF000000, b = 0xFFFFFFFF;   // 黒 → 白 (RGBA LE packed)
        Assert.Equal(a, new RgbaTween(a, b).Lerp(0));
        Assert.Equal(b, new RgbaTween(a, b).Lerp(1));
        Assert.Equal(0xFF808080u, new RgbaTween(a, b).Lerp(0.5f));
    }
}
