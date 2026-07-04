using Luxel.Typography;
using Xunit;

namespace Luxel.Tests;

/// <summary>
/// icu-dotnet アダプタ (完全 UAX#14/#29) の差分テスト。
/// ネイティブ ICU が解決できない環境では検証をスキップする (コアは SimpleSegmenter で自立)。
/// </summary>
public class IcuSegmenterTests
{
    [Fact]
    public void Availability_Reported()
    {
        // 解決可否そのものは環境依存 — プロパティが例外なく評価できることだけ確認
        _ = IcuSegmenter.IsAvailable;
    }

    [Fact]
    public void LineBreaks_MatchUax14()
    {
        if (!IcuSegmenter.IsAvailable) return;   // ICU 不在環境はスキップ
        var icu = new IcuSegmenter();

        // 空白後は語境界
        var b = new LineBreakKind[7];
        icu.GetLineBreaks("foo bar", b);
        Assert.Equal(LineBreakKind.Allowed, b[3]);   // ' ' の後

        // NBSP (U+00A0) では折らない — SimpleSegmenter との差分
        var nb = new LineBreakKind[3];
        icu.GetLineBreaks("a b", nb);
        Assert.NotEqual(LineBreakKind.Allowed, nb[1]);

        // 日本語禁則: 。を行頭にしない
        var jp = new LineBreakKind[3];
        icu.GetLineBreaks("あ。あ", jp);
        Assert.True(jp[0] < LineBreakKind.Allowed);   // あ|。 では折れない
        Assert.Equal(LineBreakKind.Allowed, jp[1]);   // 。の後は折れる
    }

    [Fact]
    public void Graphemes_And_Words()
    {
        if (!IcuSegmenter.IsAvailable) return;
        var icu = new IcuSegmenter();

        int[] g = icu.GetGraphemeBoundaries("a𠮷é");   // a(1) + 𠮷(2) + e+結合アクセント(2)
        Assert.Equal(new[] { 0, 1, 3, 5 }, g);

        (int s, int e) = icu.GetWordAt("hello world", 2);
        Assert.Equal((0, 5), (s, e));
    }

    [Fact]
    public void PluggableIntoTextLayout()
    {
        if (!IcuSegmenter.IsAvailable) return;
        using var f = VectorFont.LoadSystem();
        // NBSP を含む語は ICU では折れない → 1 行に残る (幅は十分に取る)
        var l = new TextLayout(f, "aaa bbb ccc", 16, new TextLayoutOptions
        {
            MaxWidth = f.Measure("aaa bbb", 16).width + 2,
            Wrap = TextWrap.Word,
            Segmenter = new IcuSegmenter(),
        });
        Assert.True(l.LineCount >= 2);
        (int s0, int e0) = l.LineCharRange(0);
        Assert.Contains(" ", "aaa bbb ccc"[s0..e0]);   // NBSP の左右で分断されていない
    }
}
