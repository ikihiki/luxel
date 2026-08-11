using Luxel.Strudel;

namespace Luxel.Tests;

/// <summary>Strudel パターンコア (Phase 1): クエリモデルのゴールデン。
/// Pattern は純関数なので「区間 → イベント列」を直接検証する。</summary>
public class StrudelPatternTests
{
    private static Fraction F(long n, long d = 1) => new(n, d);

    private static string Dump<T>(Pattern<T> p, long from = 0, long count = 1)
        => string.Join(" ", p.QueryCycles(from, count)
            .Select(h => $"{h.Part.Begin}-{h.Part.End}:{h.Value}{(h.HasOnset ? "" : "~")}"));

    // ---- Fraction ----

    [Fact]
    public void Fraction_Normalizes_And_Compares()
    {
        Assert.Equal(new Fraction(1, 2), new Fraction(2, 4));
        Assert.Equal(new Fraction(-1, 2), new Fraction(1, -2));
        Assert.True(new Fraction(1, 3) < new Fraction(1, 2));
        Assert.Equal(new Fraction(5, 6), new Fraction(1, 2) + new Fraction(1, 3));
        Assert.Equal(new Fraction(1, 6), new Fraction(1, 2) * new Fraction(1, 3));
        Assert.Equal(new Fraction(3, 2), new Fraction(1, 2) / new Fraction(1, 3));
    }

    [Fact]
    public void Fraction_Floor_Is_Mathematical()
    {
        Assert.Equal(0, new Fraction(1, 2).Floor);
        Assert.Equal(-1, new Fraction(-1, 2).Floor);
        Assert.Equal(2, new Fraction(2).Floor);
        Assert.Equal(new Fraction(1, 2), new Fraction(3, 2).CyclePos);
        Assert.Equal(new Fraction(1, 2), new Fraction(-1, 2).CyclePos);   // -1/2 は サイクル -1 の中の 1/2
    }

    // ---- Pure / FastCat / Stack ----

    [Fact]
    public void Pure_OneEventPerCycle()
    {
        Assert.Equal("0-1:bd", Dump(Pat.Pure("bd")));
        Assert.Equal("5-6:bd", Dump(Pat.Pure("bd"), from: 5));
    }

    [Fact]
    public void Pure_PartialQuery_IsFragment_WithoutOnset()
    {
        var haps = Pat.Pure("bd").Query(new TimeArc(F(1, 2), F(1))).ToList();
        Hap<string> h = Assert.Single(haps);
        Assert.False(h.HasOnset);                       // 断片 — 発音しない
        Assert.Equal(F(0), h.Whole!.Value.Begin);       // 本来の全区間は [0,1)
        Assert.Equal(F(1, 2), h.Part.Begin);
    }

    [Fact]
    public void FastCat_DividesCycle()
    {
        var p = Pat.FastCat(Pat.Pure("bd"), Pat.Pure("sd"));
        Assert.Equal("0-1/2:bd 1/2-1:sd", Dump(p));
        Assert.Equal("3-7/2:bd 7/2-4:sd", Dump(p, from: 3));
    }

    [Fact]
    public void Stack_Overlays()
    {
        var p = Pat.Stack(Pat.Pure("bd"), Pat.Pure("hh"));
        Assert.Equal(2, p.QueryCycles().Count);
    }

    [Fact]
    public void Cat_AlternatesPerCycle_AndAdvancesInnerCycle()
    {
        var p = Pat.Cat(Pat.FastCat(Pat.Pure("a"), Pat.Pure("b")), Pat.Pure("c"));
        Assert.Equal("0-1/2:a 1/2-1:b", Dump(p, 0));
        Assert.Equal("1-2:c", Dump(p, 1));
        Assert.Equal("2-5/2:a 5/2-3:b", Dump(p, 2));
    }

    // ---- 時間変形 ----

    [Fact]
    public void Fast_SqueezesCycles()
    {
        var p = Pat.Pure("bd").Fast(2);
        Assert.Equal("0-1/2:bd 1/2-1:bd", Dump(p));
    }

    [Fact]
    public void Slow_StretchesAcrossCycles()
    {
        var p = Pat.FastCat(Pat.Pure("a"), Pat.Pure("b")).Slow(2);
        Assert.Equal("0-1:a", Dump(p, 0));    // a が 1 サイクル全体に伸びる (whole = part = [0,1))
        var haps = p.QueryCycles(0, 2);
        Assert.Equal(2, haps.Count);
        Assert.True(haps[0].HasOnset);          // a は t=0 で発音
        Assert.Equal(F(1), haps[1].Part.Begin); // b は t=1 で発音
        Assert.True(haps[1].HasOnset);
    }

    [Fact]
    public void EarlyLate_Shift()
    {
        Assert.Equal("0-3/4:bd~ 3/4-1:bd", Dump(Pat.Pure("bd").Late(F(3, 4))));
        var haps = Pat.Pure("bd").Late(F(1, 4)).QueryCycles();
        Assert.Equal(F(1, 4), haps.First(h => h.HasOnset).Part.Begin);
    }

    [Fact]
    public void Rev_MirrorsWithinCycle()
    {
        var p = Pat.FastCat(Pat.Pure("a"), Pat.Pure("b"), Pat.Pure("c")).Rev();
        Assert.Equal("0-1/3:c 1/3-2/3:b 2/3-1:a", Dump(p));
        Assert.Equal("2-7/3:c 7/3-8/3:b 8/3-3:a", Dump(p, 2));   // どのサイクルでも同じ鏡映
    }

    [Fact]
    public void Iter_RotatesPerCycle()
    {
        var p = Pat.FastCat(Pat.Pure("a"), Pat.Pure("b"), Pat.Pure("c"), Pat.Pure("d")).Iter(4);
        Assert.Equal("0-1/4:a 1/4-1/2:b 1/2-3/4:c 3/4-1:d", Dump(p, 0));
        string cyc1 = Dump(p, 1);
        Assert.StartsWith("1-5/4:b", cyc1);   // サイクル 1 は 1/4 回転
    }

    [Fact]
    public void Every_AppliesOnMultiples()
    {
        var p = Pat.FastCat(Pat.Pure("a"), Pat.Pure("b")).Every(2, x => x.Rev());
        Assert.Equal("0-1/2:b 1/2-1:a", Dump(p, 0));   // cycle 0 は適用
        Assert.Equal("1-3/2:a 3/2-2:b", Dump(p, 1));   // cycle 1 は素通し
    }

    [Fact]
    public void Off_StacksShiftedCopy()
    {
        var p = Pat.Pure("a").Off(F(1, 4), x => x.Select(_ => "A"));
        var haps = p.QueryCycles().Where(h => h.HasOnset).ToList();
        Assert.Equal(2, haps.Count);
        Assert.Contains(haps, h => h.Value == "a" && h.Part.Begin == F(0));
        Assert.Contains(haps, h => h.Value == "A" && h.Part.Begin == F(1, 4));
    }

    [Fact]
    public void TimeCat_WeightedSlots()
    {
        var p = Pat.TimeCat([(F(3), Pat.Pure("a")), (F(1), Pat.Pure("b"))]);
        Assert.Equal("0-3/4:a 3/4-1:b", Dump(p));
    }

    [Fact]
    public void CompressSpan_SqueezesIntoSlot_EveryCycle()
    {
        var p = Pat.FastCat(Pat.Pure("a"), Pat.Pure("b")).CompressSpan(F(1, 2), F(1));
        Assert.Equal("1/2-3/4:a 3/4-1:b", Dump(p));
        Assert.Equal("7/2-15/4:a 15/4-4:b", Dump(p, 3));
    }

    // ---- 確率 / 合成 ----

    [Fact]
    public void Degrade_IsDeterministic()
    {
        var p = Pat.Pure("x").Fast(16).Degrade();
        string a = Dump(p);
        string b = Dump(p);
        Assert.Equal(a, b);                                    // 同じ時刻 → 同じ判定
        int kept = p.QueryCycles().Count;
        Assert.InRange(kept, 1, 15);                           // 全滅も全生存もしない (16 発で確率 1/2)
    }

    [Fact]
    public void OpLeft_TakesStructureFromLeft()
    {
        var gain = Pat.FastCat(Pat.Pure(1.0), Pat.Pure(0.5));
        var p = Pat.Pure("bd").Fast(4).Select(s => (S: s, G: 0.0))
            .OpLeft(gain, (v, g) => v with { G = g });
        var haps = p.QueryCycles();
        Assert.Equal(4, haps.Count);                           // 構造は左 (4 発) のまま
        Assert.Equal(1.0, haps[0].Value.G);
        Assert.Equal(0.5, haps[2].Value.G);
    }

    [Fact]
    public void WindowSplit_DoesNotDoubleFire()
    {
        // 窓を細切れにクエリしても onset は 1 回だけ — スケジューラの先読み窓の前提
        var p = Pat.Pure("bd").Fast(3);
        var onsets = new List<Fraction>();
        for (int i = 0; i < 10; i++)
        {
            var w = new TimeArc(new Fraction(i, 10), new Fraction(i + 1, 10));
            onsets.AddRange(p.Query(w).Where(h => h.HasOnset).Select(h => h.Part.Begin));
        }
        Assert.Equal([F(0), F(1, 3), F(2, 3)], onsets);
    }
}
