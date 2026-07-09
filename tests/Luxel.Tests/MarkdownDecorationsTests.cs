using Luxel.Controls;
using Luxel.Editor;
using Luxel.UI;

namespace Luxel.Tests;

/// <summary>Markdown → 装飾 (WS-A / ADR-0012) の単体テスト — 見出し (Bold+サイズ)・太字・斜体・
/// インラインコード (Mono+背景) が正しいソース範囲・変種で出ること。純関数・フォント非依存 (GPU 不要)。</summary>
public class MarkdownDecorationsTests
{
    private static readonly Theme T = Theme.Light;
    private static DecorationSet Build(string s) => MarkdownDecorations.Build(s, T);
    private static MarkDecoration At(DecorationSet set, int from, int to)
        => set.OfKind<MarkDecoration>().First(m => m.From == from && m.To == to);

    [Fact]
    public void Heading_IsBoldAndScaled_MarkerMuted()
    {
        var set = Build("# Title");                     // '#'=0 ' '=1 "Title"=[2,7)
        MarkDecoration head = At(set, 2, 7);
        Assert.Equal(FontVariant.Bold, head.Variant);
        Assert.Equal(1.9f, head.FontScale!.Value, 3);   // h1 = 1.9x
        Assert.Equal(T.Text, head.Foreground);
        Assert.Equal(T.TextMuted, At(set, 0, 2).Foreground);   // "# " マーカは淡色
    }

    [Fact]
    public void Heading_Level_ScalesDown()
    {
        Assert.Equal(MarkdownDecorations.HeadingScale(2), At(Build("## Sub"), 3, 6).FontScale!.Value, 3);
    }

    [Fact]
    public void Bold_StylesInner_NotDelimiters()
    {
        var set = Build("a **b** c");                   // '*'=2,3 'b'=4 '*'=5,6
        Assert.Equal(FontVariant.Bold, At(set, 4, 5).Variant);
        Assert.DoesNotContain(set.OfKind<MarkDecoration>(), m => m.Variant == FontVariant.Italic);
    }

    [Fact]
    public void Italic_StylesInner()
    {
        Assert.Equal(FontVariant.Italic, At(Build("a *b* c"), 3, 4).Variant);   // 'a'0 ' '1 '*'2 'b'3 '*'4
    }

    [Fact]
    public void InlineCode_IsMonoWithBackground()
    {
        var set = Build("x `y` z");                     // 'x'0 ' '1 '`'2 'y'3 '`'4
        MarkDecoration code = At(set, 3, 4);
        Assert.Equal(FontVariant.Mono, code.Variant);
        Assert.NotNull(code.Background);
    }

    [Fact]
    public void HeadingLine_DoesNotRescanInline()
    {
        // 見出し行内の "**" は消費済みなので二重に太字マークされない (見出しマークのみ)
        var set = Build("# a **b** c");
        Assert.DoesNotContain(set.OfKind<MarkDecoration>(), m => m.From == 7 && m.Variant == FontVariant.Bold && m.FontScale is null);
    }
}
