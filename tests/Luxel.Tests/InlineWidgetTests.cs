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

    [Fact]
    public void DocString_InlineFormat_EmitsLinkSyntax()
    {
        Widget b = Label("x");
        RichTextEditor doc = Docs($"前 {b:inline} 後");
        // インライン hole はリンク記法 → run (Link = luxel-ui:0) になり、resolver が widget を返す
        Luxel.Document.Block block = doc.Editor.Doc.Blocks[0];
        Luxel.Document.InlineRun run = block.Runs.First(r => r.Style.Link is not null);
        Assert.Equal("luxel-ui:0", run.Style.Link);
        Assert.Same(b, doc.InlineWidgetResolver!(run.Style.Link!));
        Assert.Null(doc.InlineWidgetResolver!("luxel-ui:99"));   // 範囲外は null (通常リンク扱い)
    }

    [Fact]
    public void DocString_BlockHole_Unchanged()
    {
        Widget b = Label("x");
        RichTextEditor doc = Docs($"前\n\n{b}\n\n後");
        Assert.Contains(doc.Editor.Doc.Blocks, bl => bl.Kind == Luxel.Document.BlockKind.Embed);
    }
}
