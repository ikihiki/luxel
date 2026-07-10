using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

/// <summary>IN: インラインボックス (TextLayout) と {widget:inline} hole (DocString)。</summary>
public class InlineWidgetTests
{
    [Fact]
    public void TextLayout_InlineBox_ReservesWidthAndHeight()
    {
        using VectorFont font = VectorFont.LoadSystem();
        var fonts = new FontCollection(font);
        var spans = new TextSpan[]
        {
            new("ab", new SpanStyle()),
            new("￼", new SpanStyle { BoxW = 40, BoxH = 30, Color = 0 }),
            new("cd", new SpanStyle()),
        };
        var layout = new TextLayout(fonts, spans, 16);

        IReadOnlyList<TextRect> box = layout.SelectionRects(2, 3);   // 占位 1 文字
        Assert.Single(box);
        Assert.Equal(40, box[0].Width, 1);
        Assert.True(layout.LineAscentAt(2) >= 30);      // ボックスが行のベースラインを押し上げる
        Assert.True(layout.Width >= 40);

        // 前後のテキスト矩形はボックスの幅ぶんずれる
        IReadOnlyList<TextRect> after = layout.SelectionRects(3, 5);
        Assert.True(after[0].X >= box[0].X + 40 - 0.5f);
    }

    // ---- DocString の markdown 符号化 (新スタック MarkdownDoc.FromDoc/DocNew が使う .Md) ----

    [Fact]
    public void DocString_InlineHole_EmitsLinkSyntax()
    {
        Widget b = Label("x");
        DocString content = $"前 {b:inline} 後";
        Assert.Contains("[￼](luxel-ui:0)", content.Md);   // インライン hole = リンク記法 (resolver が解決)
        Assert.Same(b, content.HoleWidgets[0]);
    }

    [Fact]
    public void DocString_BlockHole_EmitsLuxelUiFence()
    {
        Widget b = Label("x");
        DocString content = $"前\n\n{b}\n\n後";
        Assert.Contains("```luxel-ui", content.Md);        // ブロック hole = luxel-ui フェンス
        Assert.Same(b, content.HoleWidgets[0]);
    }

    [Fact]
    public void DocString_SignalHole_BakesValue_WidgetHolesKeepOrder()
    {
        var sig = new Signal<int>(3);
        Widget a = Label("A"), c = Label("C");
        DocString content = $"{a} count={sig} {c}";
        Assert.Contains("count=3", content.Md);            // Signal hole は構築時の値が焼き込まれる (非リアクティブ)
        Assert.Same(a, content.HoleWidgets[0]);            // widget hole の順序保持
        Assert.Same(c, content.HoleWidgets[1]);
    }
}
