using Luxel.UI;

namespace Luxel.Tests;

/// <summary>PointerEvent のボタン/修飾キー (ADR-0011) の単体テスト — フィールドと
/// Ctrl/Shift/Alt/Meta 便宜プロパティ、非ドラッグ/ドラッグ両 ctor での保持。canvas 不要。</summary>
public class PointerEventTests
{
    [Fact]
    public void Defaults_AreLeftAndNone()
    {
        var e = new PointerEvent(1, 2, 1, 2);
        Assert.Equal(PointerButton.Left, e.Button);
        Assert.Equal(KeyModifiers.None, e.Modifiers);
        Assert.False(e.Ctrl); Assert.False(e.Shift); Assert.False(e.Alt); Assert.False(e.Meta);
    }

    [Fact]
    public void ModifierFlags_MapToConvenienceProps()
    {
        var e = new PointerEvent(0, 0, 0, 0, PointerButton.Middle, KeyModifiers.Ctrl | KeyModifiers.Alt);
        Assert.Equal(PointerButton.Middle, e.Button);
        Assert.True(e.Ctrl);
        Assert.True(e.Alt);
        Assert.False(e.Shift);
        Assert.False(e.Meta);
    }

    [Fact]
    public void DragCtor_KeepsButtonAndModifiers_AndDelta()
    {
        // ドラッグ ctor: 開始 (10,10) から現在 (30,25) へ、右ボタン + Shift
        var e = new PointerEvent(30, 25, 30, 25, 10, 10, 10, 10, PointerButton.Right, KeyModifiers.Shift);
        Assert.Equal(PointerButton.Right, e.Button);
        Assert.True(e.Shift);
        Assert.Equal(20, e.DeltaX);   // 画面絶対の差分は修飾追加で不変
        Assert.Equal(15, e.DeltaY);
    }
}
