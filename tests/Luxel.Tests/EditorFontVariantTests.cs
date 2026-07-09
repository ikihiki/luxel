using Luxel.Editor;
using Luxel.Typography;

namespace Luxel.Tests;

/// <summary>font-variant Mark (WS-A / ADR-0012) の単体テスト — 太字/斜体/見出しサイズを
/// MarkDecoration で表し、ジオメトリが行内 mixed-run + 行高に反映し、行キャッシュが変種で無効化されること。
/// TextLayout を使うが GPU 不要 (VectorFont.LoadSystem)。</summary>
public class EditorFontVariantTests
{
    private static VectorFont F() => VectorFont.LoadSystem();
    private static EditorConfig Cfg() => EditorConfig.Mono(F(), size: 14f);

    [Fact]
    public void AffectsLayout_ForForegroundVariantScale_NotForOverlay()
    {
        Assert.True(new MarkDecoration(0, 1, Foreground: 0xFFFF0000).AffectsLayout);
        Assert.True(new MarkDecoration(0, 1, Variant: FontVariant.Bold).AffectsLayout);
        Assert.True(new MarkDecoration(0, 1, FontScale: 1.7f).AffectsLayout);
        Assert.False(new MarkDecoration(0, 1, Background: 0xFF00FF00).AffectsLayout);   // オーバーレイのみ
    }

    [Fact]
    public void FontScale_ScalesRun_KeepsColumns()
    {
        var plainG = new EditorGeometry(Cfg(), EditorState.Create("Heading"));
        float plainW = plainG.Line(0).Layout.Width;
        float plainH = plainG.Line(0).Height;
        var st = EditorState.Create("Heading")
            .WithDecorations("md", new DecorationSet([new MarkDecoration(0, 7, FontScale: 2f)])).State;
        var g = new EditorGeometry(Cfg(), st);
        Assert.True(g.Line(0).Layout.Width > plainW * 1.5f);   // 2x サイズ = 幅も約 2x (size がランに適用された証明)
        Assert.True(g.Line(0).Height > plainH);                 // 行高も伸びる (次行と重ならない)
        Assert.Equal(7, g.Line(0).SourceToDisplay(7));          // サイズ/変種は桁を動かさない (写像 1:1)
    }

    [Fact]
    public void VariantChange_RebuildsLayout()
    {
        // フォント変種はレイアウトに効く → 行 TextLayout が作り直される (キャッシュ鍵に含まれる)
        var g = new EditorGeometry(Cfg(), EditorState.Create("abcdef"));
        TextLayout before = g.Line(0).Layout;
        var st2 = g.State.WithDecorations("md",
            new DecorationSet([new MarkDecoration(0, 6, Variant: FontVariant.Bold, FontScale: 1.7f)])).State;
        g.SetState(st2);
        Assert.NotSame(before, g.Line(0).Layout);
    }

    [Fact]
    public void Hidden_CollapsesToZeroWidth_KeepsMapping()
    {
        // "abcd" の [1,3) を非表示 → 表示は "ad" (幅0 で畳む)、ソース↔表示写像は保つ
        var st = EditorState.Create("abcd")
            .WithDecorations("md", new DecorationSet([new MarkDecoration(1, 3, Hidden: true)])).State;
        var g = new EditorGeometry(Cfg(), st);
        DisplayLine dl = g.Line(0);
        Assert.Equal(1, dl.SourceToDisplay(1));   // 非表示開始は左詰め
        Assert.Equal(2, dl.SourceToDisplay(4));   // 末尾 = 表示 2 桁 (b,c は幅0)
        float plainW = new EditorGeometry(Cfg(), EditorState.Create("abcd")).Line(0).Layout.Width;
        Assert.True(dl.Layout.Width < plainW);     // 幅が縮む
    }

    [Fact]
    public void WrapLineHeight_TightensParagraph_KeepsBlockSpacing()
    {
        EditorConfig Wrap(float? wlh) => new()
        {
            Fonts = new FontCollection(F()), FontSize = 14f, Wrap = TextWrap.Word, MaxWidth = 80f,
            LineHeight = 1.5f, WrapLineHeight = wlh,
        };
        const string para = "aaaa aaaa aaaa aaaa";   // 80px で複数行に折返す
        float tight = new EditorGeometry(Wrap(1.25f), EditorState.Create(para)).Line(0).Height;
        float loose = new EditorGeometry(Wrap(null), EditorState.Create(para)).Line(0).Height;
        Assert.True(tight < loose);                                   // 段落内を詰めると総高が縮む
        Assert.Equal(14f * 1.5f, new EditorGeometry(Wrap(1.25f), EditorState.Create("b")).Line(0).Height, 1);   // 単一行はブロック行送りのまま
    }

    [Fact]
    public void FullyHiddenLine_CollapsesToZeroHeight()
    {
        // "```" 行を全部非表示 = マーカのみの行 → 高さ0 (空行が残らない、フェンス区切りの畳み)
        var st = EditorState.Create("```\nx")
            .WithDecorations("md", new DecorationSet([new MarkDecoration(0, 3, Hidden: true)])).State;
        var g = new EditorGeometry(Cfg(), st);
        Assert.Equal(0f, g.Line(0).Height, 3);
        Assert.True(g.Line(1).Height > 0);
    }

    [Fact]
    public void FontFor_ResolvesVariantSlots()
    {
        VectorFont reg = F();
        var cfg = new EditorConfig { Fonts = new FontCollection(reg), BoldFont = reg };
        Assert.Same(reg, cfg.FontFor(FontVariant.Bold));
        Assert.Null(cfg.FontFor(null));                    // 変種なし = 既定 Fonts
        Assert.Null(cfg.FontFor(FontVariant.Italic));      // 未供給スロット
    }
}
