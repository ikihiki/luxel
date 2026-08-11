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

    [Fact]
    public void Scale_MapsDegreesToNotes()
    {
        // C:major, 度数 0 2 4 → C E G (60 64 67)
        var haps = Cycle("""n("0 2 4").scale("C:major")""");
        Assert.Equal([60f, 64f, 67f], haps.Select(h => h.Value.Note!.Value));
        Assert.All(haps, h => Assert.Null(h.Value.N));   // 度数 N は消える
    }

    [Fact]
    public void Scale_WrapsOctavesAndNegatives()
    {
        // C:minor (0 2 3 5 7 8 10)。度数 7 = 1 オクターブ上のルート (72)、-1 = 直下の第 7 度 (60-2=58)
        var haps = Cycle("""n("7 -1").scale("C:minor")""");
        Assert.Equal(72f, haps[0].Value.Note);
        Assert.Equal(58f, haps[1].Value.Note);
    }

    [Fact]
    public void Scale_DefaultRootAndMode()
    {
        // ルート省略 = c4(60)。minorpentatonic (0 3 5 7 10)
        var haps = Cycle("""n("0 1 2").scale("minorpentatonic")""");
        Assert.Equal([60f, 63f, 65f], haps.Select(h => h.Value.Note!.Value));
    }

    [Fact]
    public void Chord_ExpandsToSimultaneousNotes()
    {
        // C メジャートライアド = 60 64 67 が同時刻に 3 つ
        var haps = Cycle("""chord("C")""");
        Assert.Equal(3, haps.Count);
        Assert.All(haps, h => Assert.Equal(Fraction.Zero, h.Part.Begin));
        Assert.Equal([60f, 64f, 67f], haps.Select(h => h.Value.Note!.Value).OrderBy(x => x));
    }

    [Fact]
    public void Chord_QualityAndSequence()
    {
        // ルートは各ピッチクラスをオクターブ 4 に配置 (C=60, A=69)。
        // "C Am" → 前半 C(60 64 67)、後半 Am(69 72 76)
        var haps = Cycle("""chord("C Am")""");
        var first = haps.Where(h => h.Part.Begin < new Fraction(1, 2)).Select(h => h.Value.Note!.Value).OrderBy(x => x).ToList();
        var second = haps.Where(h => h.Part.Begin >= new Fraction(1, 2)).Select(h => h.Value.Note!.Value).OrderBy(x => x).ToList();
        Assert.Equal([60f, 64f, 67f], first);
        Assert.Equal([69f, 72f, 76f], second);
    }

    [Fact]
    public void Chord_CanReceiveSound()
    {
        var haps = Cycle("""chord("Cmaj7").s("saw")""");
        Assert.Equal(4, haps.Count);
        Assert.All(haps, h => Assert.Equal("saw", h.Value.Instrument));
    }

    [Theory]
    [InlineData("""n("0").scale("C:bogus")""", "未知のスケール")]
    [InlineData("""s("bd").scale(1)""", "の形で使います")]
    public void ScaleErrors_AreReported(string code, string contains)
    {
        var ex = Assert.Throws<StrudelEvalError>(() => StrudelEval.Evaluate(code));
        Assert.Contains(contains, ex.Message);
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
