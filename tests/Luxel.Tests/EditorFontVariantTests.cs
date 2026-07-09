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
    public void FontFor_ResolvesVariantSlots()
    {
        VectorFont reg = F();
        var cfg = new EditorConfig { Fonts = new FontCollection(reg), BoldFont = reg };
        Assert.Same(reg, cfg.FontFor(FontVariant.Bold));
        Assert.Null(cfg.FontFor(null));                    // 変種なし = 既定 Fonts
        Assert.Null(cfg.FontFor(FontVariant.Italic));      // 未供給スロット
    }
}
