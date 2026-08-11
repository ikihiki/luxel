using Luxel.Controls;
using Luxel.Gallery.Stories;
using Xunit;

namespace Luxel.Gallery.E2eTests;

/// <summary>Strudel の ICodeLanguage (02): パースエラー位置が診断に写るか + 文脈補完 (GPU 不要)。
/// <see cref="StrudelCodeLanguage"/> は Gallery 層にあるため E2e.Tests (Gallery 参照) に置く。</summary>
public class StrudelCodeLanguageTests
{
    private static readonly StrudelCodeLanguage Lang = StrudelCodeLanguage.Instance;

    [Fact]
    public void Diagnose_ValidCode_NoDiagnostics()
        => Assert.Empty(Lang.Diagnose("s(\"bd sd\").fast(2)"));

    [Fact]
    public void Diagnose_EmptyOrWhitespace_NoDiagnostics()
    {
        Assert.Empty(Lang.Diagnose(""));
        Assert.Empty(Lang.Diagnose("   "));
    }

    [Fact]
    public void Diagnose_UnknownMethod_ReportsError()
    {
        IReadOnlyList<CodeDiagnostic> d = Lang.Diagnose("s(\"bd\").nope()");
        Assert.Single(d);
        Assert.True(d[0].IsError);
        Assert.Equal(1, d[0].Line);
        Assert.True(d[0].Column > 1, "エラー桁が式の途中を指す");
    }

    [Fact]
    public void Diagnose_MiniNotationError_MapsPositionInsideString()
    {
        // 文字列内 '[' が閉じない → MiniNotationError が元コードの位置へ換算される
        IReadOnlyList<CodeDiagnostic> d = Lang.Diagnose("s(\"bd [sd\")");
        Assert.Single(d);
        Assert.Contains("閉じ", d[0].Message);
        Assert.Equal(1, d[0].Line);
        Assert.True(d[0].Column >= 4, "波線が文字列内 (s(\" の後) を指す");
    }

    [Fact]
    public void Diagnose_MultiLine_MapsToCorrectLine()
    {
        // 改行は空白扱い — 2 行目の未知メソッドは Line 2 に写る
        IReadOnlyList<CodeDiagnostic> d = Lang.Diagnose("s(\"bd\")\n.nope()");
        Assert.Single(d);
        Assert.Equal(2, d[0].Line);
    }

    [Fact]
    public void Complete_InsideString_OffersSounds()
    {
        IReadOnlyList<CodeCompletion> c = Lang.Complete("s(\"", 3);
        Assert.Contains(c, x => x.Label == "bd");
        Assert.All(c, x => Assert.Equal("sound", x.Kind));
    }

    [Fact]
    public void Complete_AfterDot_OffersMethods()
    {
        IReadOnlyList<CodeCompletion> c = Lang.Complete("s(\"bd\").", 8);
        Assert.Contains(c, x => x.Label == "fast");
        Assert.Contains(c, x => x.Label == "gain");
    }

    [Fact]
    public void Complete_TopLevel_OffersFunctions()
    {
        IReadOnlyList<CodeCompletion> c = Lang.Complete("re", 2);
        Assert.Contains(c, x => x.Label == "rev");
        Assert.Contains(c, x => x.Label == "note");
    }
}
