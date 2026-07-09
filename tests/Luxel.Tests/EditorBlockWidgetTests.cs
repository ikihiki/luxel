using Luxel.Editor;
using Luxel.Typography;

namespace Luxel.Tests;

/// <summary>ブロック widget (WS-A S(A2b) / ADR-0012) の単体テスト — 複数ソース行を占有し、
/// 先頭行を宣言高さの全幅スロットに、残りを高さ 0 に畳む。行 ↔ ソースの 1:1 は保つ。GPU 不要。</summary>
public class EditorBlockWidgetTests
{
    private static EditorConfig Cfg() => EditorConfig.Mono(VectorFont.LoadSystem(), size: 14f);

    // "a\nb\nc\nd": 行1(b)=[2,3) 行2(c)=[4,5)。[2,5) は行1..2 を占有 (先頭=行1、畳み=行2)。
    private static EditorState WithBlock() => EditorState.Create("a\nb\nc\nd")
        .WithDecorations("md", new DecorationSet([new BlockWidgetDecoration(2, 5, "k", 50f)])).State;

    [Fact]
    public void AnchorLineGetsHeight_ContinuationCollapses()
    {
        var g = new EditorGeometry(Cfg(), WithBlock());
        Assert.Equal(4, g.LineCount);                       // 行 ↔ ソースの 1:1 を保つ
        Assert.Equal(50f, g.Line(1).Height, 3);             // 先頭行 = 宣言高さ
        Assert.Equal(0f, g.Line(2).Height, 3);              // 被覆行 = 0 に畳む
        Assert.Equal(g.LineTop(1) + 50f, g.LineTop(3), 3);  // 次の通常行はブロック直後
    }

    [Fact]
    public void EmitsFullWidthSlot_AtAnchorTop()
    {
        var g = new EditorGeometry(Cfg(), WithBlock());
        WidgetSlot slot = g.WidgetSlots().First(s => Equals(s.Key, "k"));
        Assert.Equal(g.LineTop(1), slot.Rect.Y, 3);
        Assert.Equal(50f, slot.Rect.Height, 3);
        Assert.True(slot.Rect.Width > 0);
    }

    [Fact]
    public void HeightChange_RebuildsViaKey()
    {
        var g = new EditorGeometry(Cfg(), WithBlock());
        float before = g.ContentHeight;
        var st2 = EditorState.Create("a\nb\nc\nd")
            .WithDecorations("md", new DecorationSet([new BlockWidgetDecoration(2, 5, "k", 90f)])).State;
        g.SetState(st2);
        Assert.True(g.ContentHeight > before);              // 高さ変更が ContentHeight に効く (LineKey に含まれる)
    }
}
