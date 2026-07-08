using Luxel.Strudel;

namespace Luxel.Tests;

/// <summary>Strudel ミニ記法 (Phase 2): 記法 → パターン構造のゴールデン。</summary>
public class StrudelMiniNotationTests
{
    private static string Dump(string src, long cycle = 0)
    {
        var haps = MiniNotation.Parse(src).QueryCycles(cycle);
        return string.Join(" ", haps.Select(h => $"{h.Part.Begin}-{h.Part.End}:{h.Value}"));
    }

    [Fact]
    public void Sequence_DividesEvenly()
        => Assert.Equal("0-1/4:bd 1/4-1/2:sd 1/2-3/4:hh 3/4-1:sd", Dump("bd sd hh sd"));

    [Fact]
    public void Rest_LeavesGap()
        => Assert.Equal("0-1/4:bd 1/2-3/4:sd", Dump("bd ~ sd ~"));

    [Fact]
    public void Group_CompressesIntoStep()
        => Assert.Equal("0-1/2:bd 1/2-3/4:hh 3/4-1:hh", Dump("bd [hh hh]"));

    [Fact]
    public void NestedGroups()
        => Assert.Equal("0-1/2:a 1/2-3/4:b 3/4-7/8:c 7/8-1:d", Dump("a [b [c d]]"));

    [Fact]
    public void GroupComma_Stacks()
    {
        var haps = MiniNotation.Parse("[bd, hh hh]").QueryCycles();
        Assert.Equal(3, haps.Count);
        Assert.Contains(haps, h => h.Value == "bd" && h.Part.Length == Fraction.One);
    }

    [Fact]
    public void TopLevelComma_Stacks()
    {
        var haps = MiniNotation.Parse("bd sd, hh*4").QueryCycles();
        Assert.Equal(6, haps.Count);
    }

    [Fact]
    public void Star_SpeedsStep()
        => Assert.Equal("0-1/4:hh 1/4-1/2:hh 1/2-1:sd", Dump("hh*2 sd"));

    [Fact]
    public void Slash_SlowsStep()
    {
        // a/2 は 2 サイクルで 1 回 — cycle0 は前半断片 (発音あり)、cycle1 は後半断片 (発音なし)
        var p = MiniNotation.Parse("a/2");
        Assert.True(p.QueryCycles(0).Single().HasOnset);
        Assert.False(p.QueryCycles(1).Single().HasOnset);
    }

    [Fact]
    public void Alternation_OnePerCycle()
    {
        var p = MiniNotation.Parse("bd <sd cp>");
        Assert.Equal("0-1/2:bd 1/2-1:sd", Dump("bd <sd cp>", 0));
        Assert.Contains("cp", string.Join(" ", p.QueryCycles(1).Select(h => h.Value)));
    }

    [Fact]
    public void Weight_ChangesProportion()
        => Assert.Equal("0-3/4:a 3/4-1:b", Dump("a@3 b"));

    [Fact]
    public void SampleIndex_StaysInToken()
        => Assert.Equal("0-1:bd:3", Dump("bd:3"));

    [Fact]
    public void Degrade_Deterministic_And_Parses()
    {
        var p = MiniNotation.Parse("hh*16?");
        Assert.Equal(
            string.Join("|", p.QueryCycles().Select(h => h.Part.Begin)),
            string.Join("|", p.QueryCycles().Select(h => h.Part.Begin)));
        var full = MiniNotation.Parse("hh*16?0.99").QueryCycles();
        Assert.True(full.Count <= 3);   // ほぼ全部間引かれる
    }

    [Fact]
    public void Notes_And_Numbers_AreWords()
        => Assert.Equal("0-1/3:c#3 1/3-2/3:e3 2/3-1:-12", Dump("c#3 e3 -12"));

    [Fact]
    public void Bang_ReplicatesStep()
        => Assert.Equal("0-1/3:bd 1/3-2/3:bd 2/3-1:bd", Dump("bd!3"));

    [Fact]
    public void Bang_EqualsSpelledOut()
        => Assert.Equal(Dump("bd bd bd"), Dump("bd!3"));

    [Fact]
    public void Bang_DefaultsToTwo()
        => Assert.Equal(Dump("bd bd sd"), Dump("bd! sd"));

    [Fact]
    public void Bang_Standalone_ReplicatesPrevious()
        => Assert.Equal(Dump("bd bd bd"), Dump("bd ! !"));

    [Fact]
    public void DotGroups_DivideEqually()
        => Assert.Equal("0-1/3:bd 1/3-1/2:sd 1/2-2/3:sd 2/3-1:hh", Dump("bd . sd sd . hh"));

    [Fact]
    public void DotGroup_EqualsBrackets()
        => Assert.Equal(Dump("[a] [b c]"), Dump("a . b c"));

    [Fact]
    public void DecimalToken_NotAGroupSeparator()
        // 先頭にドットが来ない語 (0.5) はトークンのまま — グループ区切りと誤認しない
        => Assert.Equal("0-1:0.5", Dump("0.5"));

    [Theory]
    [InlineData("bd [sd", "閉じていません")]
    [InlineData("bd <sd", "閉じていません")]
    [InlineData("bd*", "数値が必要")]
    [InlineData("bd ]", "予期しない")]
    public void Errors_HavePositionAndMessage(string src, string contains)
    {
        var ex = Assert.Throws<MiniNotationError>(() => MiniNotation.Parse(src));
        Assert.Contains(contains, ex.Message);
        Assert.InRange(ex.Position, 0, src.Length);
    }

    [Fact]
    public void FullExample_QueriesConsistently()
    {
        // 代表例が例外なく 4 サイクル回ることだけ確認 (数値は個別テストで担保)
        var p = MiniNotation.Parse("bd*2 [~ sd] <hh oh>, cp?0.2");
        for (long c = 0; c < 4; c++) _ = p.QueryCycles(c);
    }
}
