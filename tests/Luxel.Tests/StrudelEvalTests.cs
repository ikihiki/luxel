using Luxel.Audio.Sequencing;
using Luxel.Strudel;

namespace Luxel.Tests;

/// <summary>Strudel 評価系 (Phase 3): チェーン式 → コントロールパターン。</summary>
public class StrudelEvalTests
{
    private static List<Hap<ControlMap>> Cycle(string code, long c = 0)
        => StrudelEval.Evaluate(code).Pattern!.QueryCycles(c);

    [Fact]
    public void S_ParsesMiniAndSampleIndex()
    {
        var haps = Cycle("""s("bd:3 sd")""");
        Assert.Equal(2, haps.Count);
        Assert.Equal("bd", haps[0].Value.Instrument);
        Assert.Equal(3f, haps[0].Value.N);
        Assert.Equal("sd", haps[1].Value.Instrument);
        Assert.Null(haps[1].Value.N);
    }

    [Fact]
    public void Note_NamesAndNumbers()
    {
        var haps = Cycle("""note("c4 60 a4")""");
        Assert.Equal(60f, haps[0].Value.Note);
        Assert.Equal(60f, haps[1].Value.Note);
        Assert.Equal(69f, haps[2].Value.Note);
    }

    [Fact]
    public void Chain_FastAndGain()
    {
        var haps = Cycle("""s("bd").fast(4).gain(0.8)""");
        Assert.Equal(4, haps.Count);
        Assert.All(haps, h => Assert.Equal(0.8f, h.Value.Gain!.Value, 3));
    }

    [Fact]
    public void Gain_AcceptsMiniPattern()
    {
        var haps = Cycle("""s("bd*4").gain("1 0.5")""");
        Assert.Equal(1f, haps[0].Value.Gain);
        Assert.Equal(0.5f, haps[2].Value.Gain);
    }

    [Fact]
    public void Stack_Overlays()
    {
        var haps = Cycle("""stack(s("bd*2"), s("hh*4"))""");
        Assert.Equal(6, haps.Count);
    }

    [Fact]
    public void Every_WithBareRev()
    {
        var p = StrudelEval.Evaluate("""s("bd sd").every(2, rev)""").Pattern!;
        Assert.Equal("sd", p.QueryCycles(0)[0].Value.Instrument);   // cycle0 は rev
        Assert.Equal("bd", p.QueryCycles(1)[0].Value.Instrument);
    }

    [Fact]
    public void Jux_WithTransformValue()
    {
        var haps = Cycle("""s("bd*2").jux(fast(2))""");
        Assert.Contains(haps, h => h.Value.Pan == -1f);
        Assert.Contains(haps, h => h.Value.Pan == 1f);
        Assert.Equal(2 + 4, haps.Count);
    }

    [Fact]
    public void NoteThenSound_SetsInstrument()
    {
        var haps = Cycle("""note("c3 e3").s("saw")""");
        Assert.All(haps, h => Assert.Equal("saw", h.Value.Instrument));
        Assert.All(haps, h => Assert.NotNull(h.Value.Note));
    }

    [Fact]
    public void Silence_And_Cps()
    {
        Assert.Empty(StrudelEval.Evaluate("silence").Pattern!.QueryCycles());
        EvalResult r = StrudelEval.Evaluate("cps(0.6)");
        Assert.Null(r.Pattern);
        Assert.Equal(0.6, r.Cps!.Value, 9);
    }

    [Fact]
    public void Off_ShiftsAndTransforms()
    {
        var haps = Cycle("""s("bd").off(0.25, late(0.25))""");
        var onsets = haps.Where(h => h.HasOnset).Select(h => h.Part.Begin).OrderBy(x => x.ToDouble()).ToList();
        Assert.Equal(new Fraction(0), onsets[0]);
        Assert.Equal(new Fraction(1, 2), onsets[1]);   // 0.25 + late 0.25
    }

    [Theory]
    [InlineData("""s("bd").nope(1)""", "未知のメソッド")]
    [InlineData("""wat("bd")""", "未知の関数")]
    [InlineData("""s(1)""", "の形で使います")]
    [InlineData("""s("bd""", "文字列が閉じていません")]
    [InlineData("""rev""", "変換だけでは")]
    [InlineData("""s("bd") s""", "余分な入力")]
    public void Errors_AreReported(string code, string contains)
    {
        var ex = Assert.Throws<StrudelEvalError>(() => StrudelEval.Evaluate(code));
        Assert.Contains(contains, ex.Message);
    }

    [Fact]
    public void MiniError_PositionMapsIntoLiteral()
    {
        // s("bd [sd") — ミニ記法エラーの位置は元コード内の文字列リテラル内を指す
        const string code = """s("bd [sd")""";
        var ex = Assert.Throws<StrudelEvalError>(() => StrudelEval.Evaluate(code));
        Assert.InRange(ex.Position, 3, code.Length - 1);
    }
}
