using Luxel.Strudel;

namespace Luxel.Tests;

/// <summary>Strudel ソーススパン配管 (ToDo 22 S8a) — ミニ記法アトムの位置が Hap に刻まれ、変形を
/// 通り抜けて残ること + 「いま鳴っているトークン」の点クエリ。エディタの再生囲みの土台。</summary>
public class StrudelSpanTests
{
    [Fact]
    public void Parse_StampsAtomSpans()
    {
        // "bd sd hh" — bd[0,2) sd[3,5) hh[6,8)
        var haps = MiniNotation.Parse("bd sd hh").QueryCycles();
        Assert.Equal(3, haps.Count);
        Assert.Equal(new SourceSpan(0, 2), haps[0].Span);
        Assert.Equal(new SourceSpan(3, 2), haps[1].Span);
        Assert.Equal(new SourceSpan(6, 2), haps[2].Span);
        Assert.Equal("bd", haps[0].Value);
    }

    [Fact]
    public void Parse_BaseOffset_RebasesToLineAbsolute()
    {
        // 行内 10 文字目から始まるミニ記法 (例: s("bd sd") の "bd" は行頭から数えて 10 付近)
        var haps = MiniNotation.Parse("bd sd", baseOffset: 10).QueryCycles();
        Assert.Equal(new SourceSpan(10, 2), haps[0].Span);
        Assert.Equal(new SourceSpan(13, 2), haps[1].Span);
    }

    [Fact]
    public void Spans_SurviveTimeTransforms()
    {
        // fast/rev を通してもソース位置は残る
        var haps = MiniNotation.Parse("bd sd").Fast(new Fraction(2)).QueryCycles();
        Assert.All(haps, h => Assert.NotNull(h.Span));
        Assert.Contains(haps, h => h.Span == new SourceSpan(0, 2));   // "bd"
        Assert.Contains(haps, h => h.Span == new SourceSpan(3, 2));   // "sd"
    }

    [Fact]
    public void ActiveAt_ReturnsPlayingToken()
    {
        // "bd sd hh sd" 4 トークン/サイクル → 各 1/4。トークン中央でどれが鳴っているか
        var p = MiniNotation.Parse("bd sd hh sd");   // bd[0,2) sd[3,5) hh[6,8) sd[9,11)
        Assert.Equal(new SourceSpan(0, 2), p.ActiveAt(new Fraction(1, 8))[0]);    // 1/8 → bd
        Assert.Equal(new SourceSpan(3, 2), p.ActiveAt(new Fraction(3, 8))[0]);    // 3/8 → sd
        Assert.Equal(new SourceSpan(6, 2), p.ActiveAt(new Fraction(5, 8))[0]);    // 5/8 → hh
        Assert.Equal(new SourceSpan(9, 2), p.ActiveAt(new Fraction(7, 8))[0]);    // 7/8 → sd(2)
    }

    [Fact]
    public void ActiveAt_NextCycleWraps()
    {
        // サイクル 1 の中央でも同じトークンが鳴る (パターンは純関数・毎サイクル反復)
        var p = MiniNotation.Parse("bd sd");
        Assert.Equal(new SourceSpan(0, 2), p.ActiveAt(new Fraction(1, 4))[0]);        // cyc0 前半 bd
        Assert.Equal(new SourceSpan(0, 2), p.ActiveAt(new Fraction(5, 4))[0]);        // cyc1 前半 bd
    }
}
