using Luxel.Typography;
using Xunit;

namespace Luxel.Tests;

public class TextLayoutTests
{
    private static VectorFont F() => VectorFont.LoadSystem();
    private static VectorFont Jp() => VectorFont.LoadSystemJapanese();

    private static string LineText(string text, TextLayout l, int line)
    {
        (int s, int e) = l.LineCharRange(line);
        return text[s..e].TrimEnd();
    }

    [Fact]
    public void Segmenter_SpaceCjkKinsoku()
    {
        var seg = new SimpleSegmenter();
        var b = new LineBreakKind[5];
        seg.GetLineBreaks("ab cd", b);
        Assert.Equal(LineBreakKind.CharAllowed, b[0]);   // a|b — 語中は Char のみ
        Assert.Equal(LineBreakKind.Allowed, b[2]);       // 空白の後
        Assert.Equal(LineBreakKind.Prohibited, b[4]);    // 末尾

        var b2 = new LineBreakKind[3];
        seg.GetLineBreaks("あ。あ", b2);
        Assert.Equal(LineBreakKind.Prohibited, b2[0]);   // 行頭禁則: 。を行頭にしない
        Assert.Equal(LineBreakKind.Allowed, b2[1]);      // 。の後は折れる

        var b3 = new LineBreakKind[2];
        seg.GetLineBreaks("「あ", b3);
        Assert.Equal(LineBreakKind.Prohibited, b3[0]);   // 行末禁則: 「で行を終えない

        var b4 = new LineBreakKind[3];   // 𠮷 = サロゲートペア (2 char) + x
        seg.GetLineBreaks("𠮷x", b4);
        Assert.Equal(LineBreakKind.Prohibited, b4[0]);   // サロゲート内
    }

    [Fact]
    public void Wrap_Word_BreaksAtSpaces()
    {
        using VectorFont f = F();
        const string text = "aaa bbb ccc ddd";
        float maxW = f.Measure("aaa bbb", 16).width + 2;   // 2 語 + α で溢れる
        var l = new TextLayout(f, text, 16, new TextLayoutOptions { MaxWidth = maxW, Wrap = TextWrap.Word });
        Assert.True(l.LineCount >= 2);
        Assert.Equal("aaa bbb", LineText(text, l, 0));
        foreach (int i in Enumerable.Range(0, l.LineCount))
            Assert.True(l.LineWidth(i) <= maxW + 0.5f, $"line {i} width {l.LineWidth(i)} > {maxW}");
    }

    [Fact]
    public void Wrap_Kinsoku_MovesBreakBeforePunctuation()
    {
        using VectorFont f = Jp();
        const string text = "ああああ。あ";
        float maxW = f.Measure("ああああ。", 16).width - 1;   // 。で溢れる幅
        var l = new TextLayout(f, text, 16, new TextLayoutOptions { MaxWidth = maxW, Wrap = TextWrap.Word });
        Assert.True(l.LineCount >= 2);
        // 。を行頭にしない → 「ああああ」の後ではなくその前で折る
        Assert.Equal("あああ", LineText(text, l, 0));
        Assert.StartsWith("あ。", LineText(text, l, 1));
    }

    [Fact]
    public void Paragraphs_MandatoryNewlines()
    {
        using VectorFont f = F();
        var l = new TextLayout(f, "a\nb\nc", 16, new TextLayoutOptions { Wrap = TextWrap.None, LineHeight = 1.2f });
        Assert.Equal(3, l.LineCount);
        Assert.Equal(16 + 2 * 16 * 1.2f, l.Height, precision: 2);
    }

    [Fact]
    public void Justify_DistributesToSpaces_ExceptLastLine()
    {
        using VectorFont f = F();
        const string text = "aa bb cc dd ee ff gg hh";
        float maxW = f.Measure("aa bb cc dd", 16).width + 4;
        var l = new TextLayout(f, text, 16,
            new TextLayoutOptions { MaxWidth = maxW, Wrap = TextWrap.Word, Align = TextAlign.Justify });
        Assert.True(l.LineCount >= 2);
        Assert.True(l.LineJustifyExtra(0) > 0, "先頭行は分配される");
        Assert.Equal(0, l.LineJustifyExtra(l.LineCount - 1));   // 段落末行は分配しない
        Assert.Equal(maxW, l.Width, precision: 2);              // Justify のボックス幅
    }

    [Fact]
    public void Justify_CjkDistributesBetweenClusters()
    {
        using VectorFont f = Jp();
        const string text = "あいうえおかきくけこさしすせそ";
        float maxW = f.Measure("あいうえお", 16).width + 3;
        var l = new TextLayout(f, text, 16,
            new TextLayoutOptions { MaxWidth = maxW, Wrap = TextWrap.Word, Align = TextAlign.Justify });
        Assert.True(l.LineCount >= 2);
        Assert.True(l.LineJustifyExtra(0) > 0, "空白なし行は字間へ分配");
    }

    [Fact]
    public void Align_CenterRight_Offsets()
    {
        using VectorFont f = F();
        const string text = "ab\nabcd";
        var c = new TextLayout(f, text, 16, new TextLayoutOptions
        { MaxWidth = 200, Wrap = TextWrap.None, Align = TextAlign.Center });
        var r = new TextLayout(f, text, 16, new TextLayoutOptions
        { MaxWidth = 200, Wrap = TextWrap.None, Align = TextAlign.Right });
        Assert.Equal((200 - c.LineWidth(0)) / 2, c.LineX(0), precision: 2);
        Assert.Equal(200 - r.LineWidth(0), r.LineX(0), precision: 2);
        Assert.True(c.LineX(0) > c.LineX(1));   // 短い行ほど大きいオフセット
    }

    // ---- TX-M2: キャレット / ヒットテスト (クラスタ + グラフェム按分) ----

    [Fact]
    public void Caret_MatchesMeasuredWidth()
    {
        using VectorFont f = F();
        const string text = "Type wave";   // カーニングが入りうる列
        var l = new TextLayout(f, text, 16, new TextLayoutOptions { Wrap = TextWrap.None });
        Assert.Equal(f.Measure(text, 16).width, l.CaretRect(text.Length).X, precision: 2);
        Assert.Equal(0, l.CaretRect(0).X, precision: 2);
        // 中間位置は単調増加 (描画と同じ advance 列を辿る)
        float prev = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            float x = l.CaretRect(i).X;
            Assert.True(x >= prev, $"caret x が逆行: i={i}");
            prev = x;
        }
    }

    [Fact]
    public void Caret_SurrogatePair_NeverSplits()
    {
        using VectorFont f = Jp();
        const string text = "𠮷x";   // 𠮷 = サロゲートペア (char 2 つで 1 グラフェム)
        var l = new TextLayout(f, text, 16, new TextLayoutOptions { Wrap = TextWrap.None });
        float full = l.CaretRect(2).X;
        Assert.True(full > 0);
        Assert.Equal(0, l.CaretRect(1).X, precision: 2);   // ペア内はグラフェム境界へ吸着 (先頭側)
        // HitTest はペアの中央付近でも 0 か 2 に吸着し、1 を返さない
        int hit = l.HitTest(full * 0.6f, 0);
        Assert.True(hit is 0 or 2, $"hit={hit}");
    }

    [Fact]
    public void Caret_CombiningMark_OneGrapheme()
    {
        using VectorFont f = F();
        const string text = "éx";   // é (e + 結合アクセント) + x
        var l = new TextLayout(f, text, 16, new TextLayoutOptions { Wrap = TextWrap.None });
        float afterE = l.CaretRect(2).X;   // é の後
        Assert.True(afterE > 0);
        Assert.Equal(0, l.CaretRect(1).X, precision: 2);   // 結合列の内側は境界なし → 先頭へ
        Assert.True(l.CaretRect(3).X > afterE);
    }

    [Fact]
    public void Caret_SecondLine_HasLineY()
    {
        using VectorFont f = F();
        const string text = "aaa bbb ccc";
        float maxW = f.Measure("aaa bbb", 16).width + 2;
        var l = new TextLayout(f, text, 16, new TextLayoutOptions { MaxWidth = maxW, Wrap = TextWrap.Word });
        Assert.True(l.LineCount >= 2);
        (int s2, _) = l.LineCharRange(1);
        var r = l.CaretRect(s2 + 1);
        Assert.Equal(l.LineAdvance, r.Y, precision: 2);
        Assert.True(r.X > 0);
    }

    [Fact]
    public void SelectionRects_SpansLines()
    {
        using VectorFont f = F();
        const string text = "aaa bbb ccc ddd";
        float maxW = f.Measure("aaa bbb", 16).width + 2;
        var l = new TextLayout(f, text, 16, new TextLayoutOptions { MaxWidth = maxW, Wrap = TextWrap.Word });
        Assert.True(l.LineCount >= 2);
        (int s2, _) = l.LineCharRange(1);
        TextRect[] rects = l.SelectionRects(1, s2 + 2);
        Assert.Equal(2, rects.Length);
        Assert.Equal(0, rects[0].Y, precision: 2);
        Assert.Equal(l.LineAdvance, rects[1].Y, precision: 2);
    }

    [Fact]
    public void HitTest_RoundTripsCaret()
    {
        using VectorFont f = F();
        const string text = "hello world";
        var l = new TextLayout(f, text, 16, new TextLayoutOptions { Wrap = TextWrap.None });
        for (int i = 0; i <= text.Length; i++)
        {
            float x = l.CaretRect(i).X;
            Assert.Equal(i, l.HitTest(x + 0.01f, 0));   // キャレット位置のすぐ右をヒット → 同じ位置
        }
    }

    // ---- TX-M4: ellipsis / VerticalAlign / キャッシュ ----

    [Fact]
    public void MaxLines_TruncatesWithEllipsis()
    {
        using VectorFont f = F();
        const string text = "aaa bbb ccc ddd eee fff ggg hhh";
        float maxW = f.Measure("aaa bbb", 16).width + 2;
        var l = new TextLayout(f, text, 16, new TextLayoutOptions
        { MaxWidth = maxW, Wrap = TextWrap.Word, MaxLines = 2 });
        Assert.True(l.Truncated);
        Assert.Equal(2, l.LineCount);
        Assert.True(l.LineWidth(1) <= maxW + 0.5f);            // 記号込みで収まる
        Assert.True(l.LineWidth(1) > 0);
        var noCut = new TextLayout(f, text, 16, new TextLayoutOptions { MaxWidth = maxW, Wrap = TextWrap.Word });
        Assert.True(noCut.LineCount > 2);
        Assert.False(noCut.Truncated);
    }

    [Fact]
    public void VAlign_CenterAndBottom_OffsetLines()
    {
        using VectorFont f = F();
        var top = new TextLayout(f, "a", 16, new TextLayoutOptions { Wrap = TextWrap.None, MaxHeight = 100 });
        var mid = new TextLayout(f, "a", 16, new TextLayoutOptions
        { Wrap = TextWrap.None, MaxHeight = 100, VAlign = TextVAlign.Center });
        var bot = new TextLayout(f, "a", 16, new TextLayoutOptions
        { Wrap = TextWrap.None, MaxHeight = 100, VAlign = TextVAlign.Bottom });
        Assert.Equal(0, top.CaretRect(0).Y, precision: 2);
        Assert.Equal((100 - 16) / 2f, mid.CaretRect(0).Y, precision: 2);
        Assert.Equal(100 - 16, bot.CaretRect(0).Y, precision: 2);
        Assert.Equal(100, mid.Height, precision: 2);
    }

    [Fact]
    public void Cache_ReturnsSameInstanceForSameKey()
    {
        using VectorFont f = F();
        var o = new TextLayoutOptions { MaxWidth = 200, Wrap = TextWrap.Word };
        TextLayout a = TextLayout.Get(f, "cached text", 16, o);
        TextLayout b = TextLayout.Get(f, "cached text", 16, new TextLayoutOptions { MaxWidth = 200, Wrap = TextWrap.Word });
        Assert.Same(a, b);
        Assert.NotSame(a, TextLayout.Get(f, "other text", 16, o));
        Assert.NotSame(a, TextLayout.Get(f, "cached text", 18, o));
    }

    // ---- TX-M3: リッチテキスト / フォールバック ----

    [Fact]
    public void FontCollection_FallsBackForMissingGlyphs()
    {
        using VectorFont latin = F();
        using VectorFont jp = Jp();
        var fonts = new FontCollection(latin, jp);
        Assert.False(latin.HasGlyph('あ'));
        Assert.Same(latin, fonts.FontFor('a'));
        Assert.Same(jp, fonts.FontFor('あ'));
        // 混在テキストがフォールバック付きでレイアウトできる (豆腐にならない)
        var l = new TextLayout(fonts, [new TextSpan("abあcd")], 16, new TextLayoutOptions { Wrap = TextWrap.None });
        Assert.True(l.Width > 0);
        Assert.Equal(f_width_ab_cd_plus_jp(l), l.CaretRect(6).X, precision: 2);
        static float f_width_ab_cd_plus_jp(TextLayout l) => l.LineWidth(0);   // キャレット終端 = 行幅
    }

    [Fact]
    public void RichText_MixedSizes_LineMetricsUseMax()
    {
        using VectorFont f = F();
        var fonts = new FontCollection(f);
        var l = new TextLayout(fonts,
            [new TextSpan("small ", new SpanStyle { Size = 16 }), new TextSpan("BIG", new SpanStyle { Size = 32 })],
            16, new TextLayoutOptions { Wrap = TextWrap.None });
        Assert.Equal(1, l.LineCount);
        Assert.Equal(32, l.Height, precision: 2);              // 行高 = 行内最大サイズ
        Assert.Equal(32, l.CaretRect(0).Height, precision: 2);
    }

    [Fact]
    public void RichText_ColorsEnumerated_AndWrapAcrossSpans()
    {
        using VectorFont f = F();
        var fonts = new FontCollection(f);
        var spans = new[]
        {
            new TextSpan("red words here ", new SpanStyle { Color = 0xFF0000FFu }),
            new TextSpan("blue words there", new SpanStyle { Color = 0xFFFF0000u }),
        };
        float maxW = f.Measure("red words here blue", 16).width;
        var l = new TextLayout(fonts, spans, 16, new TextLayoutOptions { MaxWidth = maxW, Wrap = TextWrap.Word });
        Assert.Equal(2, l.Colors.Count);
        Assert.True(l.LineCount >= 2);   // スパン跨ぎでも折り返す
        foreach (int i in Enumerable.Range(0, l.LineCount))
            Assert.True(l.LineWidth(i) <= maxW + 0.5f);
    }

    [Fact]
    public void TextEditor_GraphemeMovementAndDelete()
    {
        var ed = new Luxel.UI.TextEditor();
        ed.SetText("a𠮷éb");   // a + 𠮷(2char) + é(2char) + b
        ed.Select(0, 0);
        ed.MoveRight(false); Assert.Equal(1, ed.Caret);   // a の後
        ed.MoveRight(false); Assert.Equal(3, ed.Caret);   // 𠮷 を 1 歩で跨ぐ
        ed.MoveRight(false); Assert.Equal(5, ed.Caret);   // é を 1 歩で跨ぐ
        ed.MoveLeft(false); Assert.Equal(3, ed.Caret);
        ed.Backspace();                                    // 𠮷 全体を削除
        Assert.Equal("aéb", ed.Text);
        Assert.Equal(1, ed.Caret);
        ed.DeleteForward();                                // é 全体を削除
        Assert.Equal("ab", ed.Text);
    }

    [Fact]
    public void CharWrap_BreaksInsideWord()
    {
        using VectorFont f = F();
        const string text = "abcdefghij";
        float maxW = f.Measure("abcd", 16).width + 1;
        var l = new TextLayout(f, text, 16, new TextLayoutOptions { MaxWidth = maxW, Wrap = TextWrap.Char });
        Assert.True(l.LineCount >= 2);
        Assert.True(LineText(text, l, 0).Length is >= 3 and <= 5);
    }
}
