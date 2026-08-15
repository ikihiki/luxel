using Luxel.Document;
using Luxel.Typography;

namespace Luxel.Tests;

/// <summary>エディタ新スタック S3 (ToDo 22 / ADR-0006) — EditorGeometry の純射影を検証。
/// ソース↔表示写像 (行頭 prefix・行内置換 widget・前景色ラン)、キャレット/ヒットの往復、選択矩形、
/// オーバーレイ矩形、widget 枠、縦移動 (goal-x)。TextLayout を使うが GPU 不要 (VectorFont.LoadSystem)。</summary>
public class EditorGeometryTests
{
    private static VectorFont F() => VectorFont.LoadSystem();
    private static EditorConfig Cfg() => EditorConfig.Mono(F(), size: 14f);

    private const uint Red = 0xFFFF0000, Blue = 0xFF0000FF, Yellow = 0xFFFFFF00;

    // ---- 基本: 座標往復 (装飾なし) ----

    [Fact]
    public void CaretHit_RoundTrip_PlainMultiline()
    {
        var g = new EditorGeometry(Cfg(), EditorState.Create("hello\nworld"));
        // 各オフセットの CaretRect 中心を HitTest に通すと同じオフセットへ戻る
        for (int off = 0; off <= 11; off++)
        {
            if (off == 5) continue;   // 改行位置は行末/次行頭の縮退があるので飛ばす
            TextRect c = g.CaretRect(off);
            int hit = g.HitTest(c.X + 1f, c.Y + c.Height / 2);
            Assert.Equal(off, hit);
        }
    }

    [Fact]
    public void ContentHeight_SumsLines()
    {
        var g = new EditorGeometry(Cfg(), EditorState.Create("a\nb\nc"));
        Assert.Equal(3, g.LineCount);
        Assert.True(g.ContentHeight > g.Line(0).Height * 2.5f);
        Assert.Equal(g.Line(0).Height, g.LineTop(1), 3);
    }

    // ---- 行頭 prefix の写像 ----

    [Fact]
    public void Prefix_ShiftsDisplayNotSource()
    {
        var st = EditorState.Create("abc")
            .WithDecorations("num", new DecorationSet([new LinePrefixDecoration(0, "1. ", Blue)])).State;
        var g = new EditorGeometry(Cfg(), st);
        DisplayLine dl = g.Line(0);
        Assert.Equal(3, dl.PrefixLen);
        // ソース桁 0 は prefix の後ろ (表示 3)、ソース 2 は表示 5
        Assert.Equal(3, dl.SourceToDisplay(0));
        Assert.Equal(5, dl.SourceToDisplay(2));
        // 逆写像: prefix 内 (表示 1) はソース行頭 0、表示 4 はソース 1
        Assert.Equal(0, dl.DisplayToSource(1));
        Assert.Equal(1, dl.DisplayToSource(4));
    }

    [Fact]
    public void Prefix_CaretIsAfterPrefix()
    {
        var plain = new EditorGeometry(Cfg(), EditorState.Create("abc"));
        float x0 = plain.CaretRect(0).X;

        var st = EditorState.Create("abc")
            .WithDecorations("num", new DecorationSet([new LinePrefixDecoration(0, "1. ", Blue)])).State;
        var g = new EditorGeometry(Cfg(), st);
        // prefix があるとソース 0 のキャレットは prefix 幅ぶん右へずれる
        Assert.True(g.CaretRect(0).X > x0 + 5f);
    }

    // ---- 行内 置換 widget の写像 ----

    [Fact]
    public void Widget_ReplacesSourceRange_MapsAround()
    {
        // "x5y" の "5" (ソース [1,2)) を幅 40 の widget に置換
        var st = EditorState.Create("x5y")
            .WithDecorations("w", new DecorationSet([new WidgetDecoration(1, 2, 40, 12, "slider")])).State;
        var g = new EditorGeometry(Cfg(), st);
        DisplayLine dl = g.Line(0);
        // ソース: 0→表示0(x前), 1→widget左, 2→widget右, 3→y後
        Assert.Equal(0, dl.SourceToDisplay(0));
        Assert.Equal(1, dl.SourceToDisplay(1));   // widget 左端 (表示スロット 1)
        Assert.Equal(2, dl.SourceToDisplay(2));   // widget 右端
        Assert.Equal(3, dl.SourceToDisplay(3));
        // widget スロットは幅 40 を占有する
        var slots = g.WidgetSlots();
        Assert.Single(slots);
        Assert.Equal("slider", slots[0].Key);
        Assert.True(slots[0].Rect.Width >= 39f);
    }

    [Fact]
    public void Widget_HitTestOnBox_MapsToEdge()
    {
        var st = EditorState.Create("x5y")
            .WithDecorations("w", new DecorationSet([new WidgetDecoration(1, 2, 40, 12, "s")])).State;
        var g = new EditorGeometry(Cfg(), st);
        WidgetSlot slot = g.WidgetSlots()[0];
        // ボックス左寄りクリック → ソース 1 (widget 開始)、右寄り → ソース 2 (widget 終端)
        int left = g.HitTest(slot.Rect.X + 2f, slot.Rect.Y + slot.Rect.Height / 2);
        int right = g.HitTest(slot.Rect.X + slot.Rect.Width - 2f, slot.Rect.Y + slot.Rect.Height / 2);
        Assert.Equal(1, left);
        Assert.Equal(2, right);
    }

    [Fact]
    public void Widget_Anchor_InsertsDisplaySlot()
    {
        // "xy" の位置 1 (x と y の間) にアンカー widget (ソース 0 文字)
        var st = EditorState.Create("xy")
            .WithDecorations("w", new DecorationSet([new WidgetDecoration(1, 1, 20, 12, "mark")])).State;
        var g = new EditorGeometry(Cfg(), st);
        DisplayLine dl = g.Line(0);
        Assert.Equal(0, dl.SourceToDisplay(0));
        Assert.Equal(2, dl.SourceToDisplay(1));   // アンカーの後ろ
        Assert.Equal(3, dl.SourceToDisplay(2));
        Assert.Single(g.WidgetSlots());
        Assert.Equal("mark", g.WidgetSlots()[0].Key);
    }

    // ---- 前景色ラン (色分け) ----

    [Fact]
    public void Foreground_SplitsColorRuns_KeepsCharCount()
    {
        // "abcd" の [1,3) を赤に → 色は変わるが表示文字数は不変 (写像は 1:1)
        var st = EditorState.Create("abcd")
            .WithDecorations("syn", new DecorationSet([new MarkDecoration(1, 3, Foreground: Red)])).State;
        var g = new EditorGeometry(Cfg(), st);
        DisplayLine dl = g.Line(0);
        Assert.Equal(0, dl.PrefixLen);
        Assert.Equal(2, dl.SourceToDisplay(2));      // 色分けは桁を動かさない
        // Layout の色集合に赤と既定色が含まれる
        Assert.Contains(Red, dl.Layout.Colors);
        Assert.Contains(Cfg().DefaultColor, dl.Layout.Colors);
    }

    // ---- オーバーレイ矩形 (レイアウト非依存) ----

    [Fact]
    public void OverlayRects_BackgroundUnderlineBox()
    {
        var st = EditorState.Create("abcdef").WithDecorations("ov", new DecorationSet(
        [
            new MarkDecoration(0, 2, Background: Yellow),
            new MarkDecoration(2, 4, Underline: new UnderlineStyle(Red, Wavy: true)),
            new MarkDecoration(4, 6, Box: new BoxStyle(Blue)),
        ])).State;
        var g = new EditorGeometry(Cfg(), st);
        var ov = g.OverlayRects();
        Assert.Contains(ov, r => r.Kind == OverlayKind.Background && r.Color == Yellow);
        Assert.Contains(ov, r => r.Kind == OverlayKind.WavyUnderline && r.Color == Red);
        Assert.Contains(ov, r => r.Kind == OverlayKind.Box && r.Color == Blue);
    }

    [Fact]
    public void LineBackground_ExtendsToConfiguredRightEdge()
    {
        VectorFont font = F();
        var config = new EditorConfig
        {
            Fonts = new FontCollection(font),
            FontSize = 14,
            Wrap = TextWrap.Word,
            MaxWidth = 320,
        };
        EditorState state = EditorState.Create("short")
            .WithDecorations("line", new DecorationSet([new LineDecoration(0, Yellow)])).State;
        var geometry = new EditorGeometry(config, state);

        OverlayRect background = geometry.OverlayRects().Single(x => x.Kind == OverlayKind.LineBackground);
        Assert.Equal(320f, background.Rect.Width);
    }

    [Fact]
    public void OverlayRects_DoNotRebuildLayout()
    {
        // オーバーレイ (背景/囲み) だけ変えても行 TextLayout は同一インスタンスのまま (キャッシュ再利用)
        var g = new EditorGeometry(Cfg(), EditorState.Create("abcdef"));
        TextLayout before = g.Line(0).Layout;
        var st2 = g.State.WithDecorations("play", new DecorationSet([new MarkDecoration(1, 3, Box: new BoxStyle(Blue))])).State;
        g.SetState(st2);
        Assert.Same(before, g.Line(0).Layout);       // レイアウト非依存装飾は再構築を起こさない
        Assert.NotEmpty(g.OverlayRects());
    }

    [Fact]
    public void ForegroundChange_DoesRebuildLayout()
    {
        // 前景色 (レイアウトに効く) を変えると Layout は作り直される
        var g = new EditorGeometry(Cfg(), EditorState.Create("abcdef"));
        TextLayout before = g.Line(0).Layout;
        var st2 = g.State.WithDecorations("syn", new DecorationSet([new MarkDecoration(1, 3, Foreground: Red)])).State;
        g.SetState(st2);
        Assert.NotSame(before, g.Line(0).Layout);
    }

    // ---- ブロックインデント (縦バーの場所を確保) ----

    [Fact]
    public void BlockIndent_ShiftsContentRight()
    {
        var st = EditorState.Create("abc\ndef")
            .WithDecorations("q", new DecorationSet([new BlockDecoration(0, 7, BarColor: Blue, Indent: 20f)])).State;
        var g = new EditorGeometry(Cfg(), st);
        Assert.Equal(20f, g.LineIndent(0), 1);
        Assert.Equal(20f, g.LineIndent(1), 1);

        var plain = new EditorGeometry(Cfg(), EditorState.Create("abc\ndef"));
        Assert.True(g.CaretRect(0).X >= plain.CaretRect(0).X + 19f);   // 行頭キャレットがインデントぶん右へ

        // 縦バー領域 (x < indent) のクリックは行頭にマップ (テキストに食い込まない)
        Assert.Equal(0, g.HitTest(3f, g.CaretRect(0).Y + 2f));
    }

    // ---- 選択矩形 ----

    [Fact]
    public void SelectionRects_MultiLine()
    {
        var g = new EditorGeometry(Cfg(), EditorState.Create("hello\nworld"));
        var rects = g.SelectionRects(2, 8);          // "llo\nwo"
        Assert.True(rects.Count >= 2);               // 2 行に跨る
        Assert.True(rects[1].Y > rects[0].Y);        // 2 行目は下
    }

    // ---- 縦移動 (goal-x) ----

    [Fact]
    public void MoveVertical_KeepsGoalX()
    {
        var g = new EditorGeometry(Cfg(), EditorState.Create("longer line\nab\nlonger line"));
        float? goal = null;
        int start = 8;                               // 1 行目の桁 8
        int down = g.MoveVertical(start, +1, ref goal);   // 2 行目 "ab" は短いので行末へ
        (int l1, _) = g.State.Doc.CoordAt(down);
        Assert.Equal(1, l1);
        int down2 = g.MoveVertical(down, +1, ref goal);   // goal-x を保って 3 行目の元の桁付近へ
        (int l2, int c2) = g.State.Doc.CoordAt(down2);
        Assert.Equal(2, l2);
        Assert.True(c2 >= 6);                        // goal-x により桁 8 付近に戻る
    }

    [Fact]
    public void MoveVertical_StopsAtEdges()
    {
        var g = new EditorGeometry(Cfg(), EditorState.Create("a\nb"));
        float? goal = null;
        Assert.Equal(0, g.MoveVertical(0, -1, ref goal));   // 先頭行より上には行かない
        goal = null;
        int last = g.State.Doc.Length;
        Assert.Equal(last, g.MoveVertical(last, +1, ref goal));  // 最終行より下へは行かない
    }

    // ---- 折返し ----

    [Fact]
    public void Wrap_ProducesMultipleRows()
    {
        var cfg = new EditorConfig { Fonts = new FontCollection(F()), FontSize = 14f, Wrap = TextWrap.Word, MaxWidth = 40f };
        var g = new EditorGeometry(cfg, EditorState.Create("one two three four five"));
        Assert.True(g.Line(0).Layout.LineCount > 1);     // 折り返されて複数表示行
        Assert.True(g.Line(0).Height > g.Line(0).Layout.LineAdvance);
    }
}
