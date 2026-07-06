using Luxel.Gallery;
using Luxel.Typography;
using Xunit;

namespace Luxel.Gallery.E2eTests;

/// <summary>同梱フォント (<see cref="GalleryFonts"/>) が実際に読めて日本語・ラテン両方の
/// グリフを収載していることの回帰テスト (GPU 不要)。golden 全再生成の前提を担保する —
/// フォントが glyf アウトライン (VectorFont は CFF/OTF 非対応) で読めることをここで確認する。</summary>
public sealed class BundledFontTests
{
    public BundledFontTests() => RepoRoot.Ensure();

    [Theory]
    [InlineData(GalleryFonts.Regular)]
    [InlineData(GalleryFonts.Bold)]
    [InlineData(GalleryFonts.Mono)]
    public void Bundled_Loads_And_CoversJapaneseAndLatin(string file)
    {
        using VectorFont f = GalleryFonts.Load(file);
        Assert.True(f.HasGlyph('あ'), "ひらがな 'あ' を収載していない");
        Assert.True(f.HasGlyph('漢'), "漢字 '漢' を収載していない");
        Assert.True(f.HasGlyph('A'), "ラテン 'A' を収載していない");
        (float w, float h) = f.Measure("日本語 test 123", 16);
        Assert.True(w > 0 && h > 0, $"テキストの Measure が非正: {w}x{h}");
    }

    [Fact]
    public void MonoFont_CoversJapaneseForCodeEditor()
    {
        // CodeEditor は単一フォントで描く (日本語フォールバック無し) ので、等幅フォント自身が
        // 日本語を収載している必要がある — 収載していないとコメントが豆腐になる。
        using VectorFont mono = GalleryFonts.Load(GalleryFonts.Mono);
        Assert.True(mono.HasGlyph('あ'));
        Assert.True(mono.HasGlyph('コ'));
    }
}
