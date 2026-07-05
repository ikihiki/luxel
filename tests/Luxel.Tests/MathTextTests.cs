using Luxel.Document;
using Luxel.MathText;
using Xunit;

namespace Luxel.Tests;

/// <summary>MA: 数式 — インライン Unicode 正規化 / $$ 写像と round-trip / TeX パーサ / ボックス組版。</summary>
public class MathTextTests
{
    [Fact]
    public void TexText_GreekOperatorsScripts()
    {
        Assert.Equal("α + β ≤ π", TexText.ToUnicode(@"\alpha + \beta \le \pi"));
        Assert.Equal("x²", TexText.ToUnicode("x^2"));
        Assert.Equal("aᵢⱼ", TexText.ToUnicode("a_{ij}"));
        Assert.Equal(@"x^\frac", TexText.ToUnicode(@"x^\frac"));   // 変換不能な ^ は原文のまま
        Assert.Equal(@"\unknown", TexText.ToUnicode(@"\unknown"));
    }

    [Fact]
    public void InlineMath_MappedToMathRun_AndRoundTrips()
    {
        RichDocument doc = Markdown.Parse(@"エネルギーは $E = mc^2$ です");
        InlineRun run = doc.Blocks[0].Lines[0].Runs.First(r => r.Style.Math);
        Assert.Equal("E = mc²", run.Text);   // Unicode 正規化済み

        string md = Markdown.Serialize(doc);
        Assert.Contains("$E = mc²$", md);    // 正規形で書き戻し (1 回で収束)
        Assert.Equal(md, Markdown.Serialize(Markdown.Parse(md)));
    }

    [Fact]
    public void BlockMath_MappedToPayload_AndRoundTrips()
    {
        RichDocument doc = Markdown.Parse("$$\n\\frac{a}{b}\n$$");
        Block embed = doc.Blocks.First(b => b.Kind == BlockKind.Embed);
        var payload = Assert.IsType<MathPayload>(embed.Payload);
        Assert.Equal(@"\frac{a}{b}", payload.Source);

        string md = Markdown.Serialize(doc);
        Assert.Contains("$$", md);
        RichDocument again = Markdown.Parse(md);
        Assert.Equal(@"\frac{a}{b}",
            ((MathPayload)again.Blocks.First(b => b.Kind == BlockKind.Embed).Payload!).Source);
    }

    [Fact]
    public void TexParser_FracScriptMatrix()
    {
        var frac = Assert.IsType<MathFrac>(TexParser.Parse(@"\frac{a}{b}"));
        Assert.IsType<MathSymbol>(frac.Num);

        var script = Assert.IsType<MathScript>(TexParser.Parse("x^2"));
        Assert.NotNull(script.Sup);
        Assert.Null(script.Sub);

        var m = Assert.IsType<MathMatrix>(TexParser.Parse(@"\begin{bmatrix} a & b \\ c & d \end{bmatrix}"));
        Assert.Equal('[', m.Bracket);
        Assert.Equal(2, m.Rows.Count);
        Assert.Equal(2, m.Rows[0].Count);
    }

    [Fact]
    public void Layout_FracTallerThanSymbol_MatrixWiderThanCell()
    {
        var engine = new MathLayoutEngine((t, px) => (t.Length * px * 0.6f, px * 1.2f), px => px);
        MathBox sym = engine.Measure(TexParser.Parse("x"), 20);
        MathBox frac = engine.Measure(TexParser.Parse(@"\frac{a}{b}"), 20);
        Assert.True(frac.H > sym.H * 1.5f);
        Assert.True(frac.Base > sym.Base);   // 分子ぶん上に伸びる

        MathBox mat = engine.Measure(TexParser.Parse(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}"), 20);
        MathBox cell = engine.Measure(TexParser.Parse("a"), 20);
        Assert.True(mat.W > cell.W * 2);
        Assert.True(mat.H > cell.H * 2);
    }
}
