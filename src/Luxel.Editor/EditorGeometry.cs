using Luxel.Typography;

namespace Luxel.Editor;

/// <summary>ジオメトリの設定 — フォント・基準サイズ・折返し。view が注入する。</summary>
public sealed class EditorConfig
{
    /// <summary>フォント (フォールバック対応)。</summary>
    public required FontCollection Fonts { get; init; }
    /// <summary>基準文字サイズ px。</summary>
    public float FontSize { get; init; } = 14f;
    /// <summary>折返し (コードは既定 None)。</summary>
    public TextWrap Wrap { get; init; } = TextWrap.None;
    /// <summary>折返し幅 px (Wrap!=None のとき有効)。</summary>
    public float MaxWidth { get; init; } = float.PositiveInfinity;
    /// <summary>行高倍率。</summary>
    public float LineHeight { get; init; } = 1.4f;
    /// <summary>既定の文字色 (前景マークが無い箇所)。</summary>
    public uint DefaultColor { get; init; } = 0xFF000000;

    /// <summary>単一フォントから作る便宜コンストラクタ相当。</summary>
    public static EditorConfig Mono(VectorFont font, float size = 14f, uint color = 0xFF000000)
        => new() { Fonts = new FontCollection(font), FontSize = size, DefaultColor = color };
}

/// <summary>widget の配置枠 — view が <see cref="Key"/> から実 Widget を解決してこの矩形に重ねる。</summary>
public readonly record struct WidgetSlot(object Key, TextRect Rect);

/// <summary>オーバーレイ装飾の描画分類 (背景/下線/波線/囲み/行背景/ブロック)。</summary>
public enum OverlayKind { Background, Underline, WavyUnderline, Box, LineBackground, BlockBackground, BlockBar }

/// <summary>オーバーレイ装飾 1 つの描画情報 (矩形 + 種別 + 色)。view が種別に応じて塗る。</summary>
public readonly record struct OverlayRect(TextRect Rect, OverlayKind Kind, uint Color, uint? Fill = null, float Radius = 0f);

/// <summary>
/// 1 ソース行の表示状態 — <see cref="TextLayout"/> と、ソース桁 ↔ 表示桁のランレングス写像 (<see cref="Seg"/>)。
/// 表示には行頭 prefix (ソース 0 文字) と行内 widget (置換=ソース n 文字→表示 1 ボックス) が混ざるため、
/// キャレット/ヒットはこの写像を通す。前景色マークは表示文字数を変えないので色ランとして Layout に畳む。
/// </summary>
public sealed class DisplayLine
{
    internal readonly record struct Seg(int Src, int Disp, bool IsWidget, object? Key);

    internal DisplayLine(TextLayout layout, int prefixLen, Seg[] segs, float height)
    {
        Layout = layout;
        PrefixLen = prefixLen;
        Segs = segs;
        Height = height;
    }

    /// <summary>この行のテキストレイアウト (view が Draw する)。</summary>
    public TextLayout Layout { get; }
    /// <summary>行頭の表示専用文字数 (番号など)。ソース桁 0 はこの後ろに来る。</summary>
    public int PrefixLen { get; }
    /// <summary>行の表示高さ。</summary>
    public float Height { get; }

    internal Seg[] Segs { get; }

    /// <summary>ソース桁 (行内 0 始まり) → 表示桁。</summary>
    public int SourceToDisplay(int col)
    {
        int s = 0, d = PrefixLen;
        foreach (Seg seg in Segs)
        {
            int end = s + seg.Src;
            if (col < end) return seg.IsWidget ? d : d + (col - s);
            s = end; d += seg.Disp;
        }
        return d;
    }

    /// <summary>表示桁 → ソース桁 (行内)。prefix 内は行頭 (0)、widget は左端=開始/右端=終端。</summary>
    public int DisplayToSource(int disp)
    {
        if (disp <= PrefixLen) return 0;
        int s = 0, d = PrefixLen;
        foreach (Seg seg in Segs)
        {
            int end = d + seg.Disp;
            if (disp < end) return seg.IsWidget ? s : s + (disp - d);
            s += seg.Src; d = end;
        }
        return s;
    }

    /// <summary>widget セグメントを (キー, 表示開始桁) で列挙する。</summary>
    internal IEnumerable<(object Key, int DispStart)> Widgets()
    {
        int d = PrefixLen;
        foreach (Seg seg in Segs)
        {
            if (seg.IsWidget && seg.Key is not null) yield return (seg.Key, d);
            d += seg.Disp;
        }
    }
}

/// <summary>
/// エディタの**純射影** — <see cref="EditorState"/> と <see cref="EditorConfig"/> から、ソース位置 ↔ ピクセルの
/// 変換・各種矩形・行ジオメトリを計算する。<see cref="TextLayout"/> が canvas 非依存なので、この層まで GPU 不要で
/// 単体テストできる。**選択状態を持たない** (state を受け取って投影するだけ)。行は内容 + レイアウト依存装飾を鍵に
/// キャッシュし、変わらない行の TextLayout は再構築しない。
/// </summary>
public sealed class EditorGeometry
{
    private EditorConfig _cfg;
    private EditorState _state;
    private DisplayLine[] _lines = [];
    private float[] _tops = [];      // 各行の絶対 Top
    private float[] _indents = [];   // 各行の左インデント (ブロックの縦バー分の余白)
    private float _contentHeight;
    private Dictionary<string, DisplayLine> _cache = new();

    /// <summary>設定と初期状態から作る。</summary>
    public EditorGeometry(EditorConfig config, EditorState? state = null)
    {
        _cfg = config;
        _state = state ?? EditorState.Create();
        Rebuild();
    }

    /// <summary>現在の状態。</summary>
    public EditorState State => _state;
    /// <summary>ソース行数。</summary>
    public int LineCount => _lines.Length;
    /// <summary>全行の合計高さ。</summary>
    public float ContentHeight => _contentHeight;

    /// <summary>設定を差し替える (全行のキャッシュを捨てて作り直す)。</summary>
    public void Configure(EditorConfig config)
    {
        _cfg = config;
        _cache = new Dictionary<string, DisplayLine>();
        Rebuild();
    }

    /// <summary>状態を差し替える (内容/レイアウト装飾が変わらない行は TextLayout を再利用)。</summary>
    public void SetState(EditorState state)
    {
        _state = state;
        Rebuild();
    }

    /// <summary>ソース行 (0 始まり) の表示行。</summary>
    public DisplayLine Line(int index) => _lines[Math.Clamp(index, 0, _lines.Length - 1)];
    /// <summary>ソース行の絶対 Top。</summary>
    public float LineTop(int index) => _tops[Math.Clamp(index, 0, _tops.Length - 1)];
    /// <summary>ソース行の左インデント (ブロックの縦バー分の余白)。view はテキストをこの x から描く。</summary>
    public float LineIndent(int index) => _indents[Math.Clamp(index, 0, _indents.Length - 1)];

    private void Rebuild()
    {
        TextDoc doc = _state.Doc;
        int n = doc.LineCount;
        var lines = new DisplayLine[n];
        var tops = new float[n];
        var next = new Dictionary<string, DisplayLine>(n);

        float top = 0;
        for (int i = 0; i < n; i++)
        {
            string key = LineKey(i);
            if (!next.TryGetValue(key, out DisplayLine? dl))
            {
                if (!_cache.TryGetValue(key, out dl)) dl = BuildLine(i);
                next[key] = dl;
            }
            lines[i] = dl;
            tops[i] = top;
            top += dl.Height;
        }

        // ブロックのインデント (縦バー/マーカー分の左余白) を行ごとに集計
        var indents = new float[n];
        foreach (BlockDecoration bd in _state.Decorations.All().OfType<BlockDecoration>())
        {
            if (bd.Indent <= 0) continue;
            int l0 = doc.LineOf(bd.From), l1 = doc.LineOf(bd.To);
            for (int i = l0; i <= l1 && i < n; i++) indents[i] += bd.Indent;
        }

        _lines = lines;
        _tops = tops;
        _indents = indents;
        _contentHeight = top;
        _cache = next;
    }

    // 行キャッシュ鍵: ソース行テキスト + レイアウトに効く装飾 (前景色/widget/prefix) の署名。
    // 背景/下線/囲み等のオーバーレイは Layout に影響しないので鍵に含めない (再生囲みが 60fps でも再構築しない)。
    private string LineKey(int line)
    {
        TextDoc doc = _state.Doc;
        int start = doc.LineStart(line), end = doc.LineEnd(line);
        var sb = new System.Text.StringBuilder();
        sb.Append(line).Append('\u0001').Append(doc.LineText(line)).Append('\u0002');
        foreach (Decoration d in _state.Decorations.All())
        {
            if (!d.AffectsLayout) continue;
            if (d.SortTo < start || d.SortFrom > end) continue;
            switch (d)
            {
                case MarkDecoration m when m.Foreground is { } fg:
                    sb.Append("f").Append(m.From).Append(',').Append(m.To).Append(',').Append(fg).Append(';');
                    break;
                case WidgetDecoration w:
                    sb.Append("w").Append(w.From).Append(',').Append(w.To).Append(',').Append(w.Width).Append(',').Append(w.Height).Append(';');
                    break;
                case LinePrefixDecoration p:
                    sb.Append("p").Append(p.Text).Append(';');
                    break;
            }
        }
        return sb.ToString();
    }

    private DisplayLine BuildLine(int line)
    {
        TextDoc doc = _state.Doc;
        int start = doc.LineStart(line), end = doc.LineEnd(line);
        string text = doc.LineText(line);
        int len = text.Length;

        // 行頭 prefix (複数あれば連結)
        string prefixText = "";
        uint prefixColor = _cfg.DefaultColor;
        foreach (LinePrefixDecoration p in _state.Decorations.All().OfType<LinePrefixDecoration>())
            if (p.At >= start && p.At <= end) { prefixText += p.Text; prefixColor = p.Color; }

        // 行内 widget (置換=ソース範囲 n>0、アンカー=点 n==0)。開始桁昇順、非重複前提。
        var widgets = new List<(int a, int b, object key, float w, float h)>();
        foreach (WidgetDecoration wd in _state.Decorations.All().OfType<WidgetDecoration>())
        {
            if (wd.From >= start && wd.To <= end && wd.To >= wd.From)
                widgets.Add((wd.From - start, wd.To - start, wd.Key, wd.Width, wd.Height));
        }
        widgets.Sort((x, y) => x.a.CompareTo(y.a));

        // 前景色 (既定 → マークで上書き)
        var color = new uint[len];
        for (int i = 0; i < len; i++) color[i] = _cfg.DefaultColor;
        foreach (MarkDecoration m in _state.Decorations.All().OfType<MarkDecoration>())
        {
            if (m.Foreground is not { } fg) continue;
            int a = Math.Max(start, m.From) - start, b = Math.Min(end, m.To) - start;
            for (int i = a; i < b; i++) if (i >= 0 && i < len) color[i] = fg;
        }

        // セグメント + スパンを組む
        var spans = new List<TextSpan>();
        var segs = new List<DisplayLine.Seg>();
        int prefixLen = 0;
        if (prefixText.Length > 0)
        {
            spans.Add(new TextSpan(prefixText, new SpanStyle { Color = prefixColor }));
            prefixLen = prefixText.Length;
        }

        int pos = 0, wi = 0;
        while (pos < len)
        {
            if (wi < widgets.Count && widgets[wi].a == pos)
            {
                (int a, int b, object key, float w, float h) = widgets[wi];
                spans.Add(new TextSpan("￼", new SpanStyle { BoxW = w, BoxH = h, Color = _cfg.DefaultColor }));
                segs.Add(new DisplayLine.Seg(b - a, 1, IsWidget: true, key));
                pos = b; wi++;
                continue;
            }
            int runEnd = pos + 1;
            int nextWidget = wi < widgets.Count ? widgets[wi].a : len;
            while (runEnd < len && runEnd < nextWidget && color[runEnd] == color[pos]) runEnd++;
            runEnd = Math.Min(runEnd, nextWidget);
            spans.Add(new TextSpan(text[pos..runEnd], new SpanStyle { Color = color[pos] }));
            segs.Add(new DisplayLine.Seg(runEnd - pos, runEnd - pos, IsWidget: false, null));
            pos = runEnd;
        }
        // 行末のアンカー widget (ソース len 位置)
        while (wi < widgets.Count && widgets[wi].a == len && widgets[wi].b == len)
        {
            spans.Add(new TextSpan("￼", new SpanStyle { BoxW = widgets[wi].w, BoxH = widgets[wi].h, Color = _cfg.DefaultColor }));
            segs.Add(new DisplayLine.Seg(0, 1, IsWidget: true, widgets[wi].key));
            wi++;
        }
        if (spans.Count == 0) spans.Add(new TextSpan("", new SpanStyle { Color = _cfg.DefaultColor }));

        var opt = new TextLayoutOptions { MaxWidth = _cfg.MaxWidth, Wrap = _cfg.Wrap, LineHeight = _cfg.LineHeight };
        var layout = new TextLayout(_cfg.Fonts, spans, _cfg.FontSize, opt);
        float height = MathF.Max(layout.Height, layout.LineAdvance);
        return new DisplayLine(layout, prefixLen, segs.ToArray(), height);
    }

    // ---- ソース位置 ↔ ピクセル ----

    /// <summary>ソースオフセットのキャレット矩形 (絶対座標)。</summary>
    public TextRect CaretRect(int sourceOffset)
    {
        (int line, int col) = _state.Doc.CoordAt(sourceOffset);
        DisplayLine dl = _lines[line];
        TextRect r = dl.Layout.CaretRect(dl.SourceToDisplay(col));
        return r with { X = r.X + _indents[line], Y = r.Y + _tops[line] };
    }

    /// <summary>ピクセル座標 → ソースオフセット (ヒットテスト)。</summary>
    public int HitTest(float x, float y)
    {
        int line = LineAtY(y);
        DisplayLine dl = _lines[line];
        int disp = dl.Layout.HitTest(x - _indents[line], y - _tops[line]);
        int col = dl.DisplayToSource(disp);
        return _state.Doc.LineStart(line) + Math.Min(col, _state.Doc.LineText(line).Length);
    }

    private int LineAtY(float y)
    {
        if (y <= 0 || _lines.Length == 1) return 0;
        for (int i = 0; i < _lines.Length; i++)
            if (y < _tops[i] + _lines[i].Height) return i;
        return _lines.Length - 1;
    }

    /// <summary>ソース範囲 [from, to) の選択矩形群 (絶対座標、行跨ぎ対応)。</summary>
    public IReadOnlyList<TextRect> SelectionRects(int from, int to)
    {
        if (to < from) (from, to) = (to, from);
        var rects = new List<TextRect>();
        AppendRangeRects(from, to, rects);
        return rects;
    }

    private void AppendRangeRects(int from, int to, List<TextRect> outp)
    {
        TextDoc doc = _state.Doc;
        int la = doc.LineOf(from), lb = doc.LineOf(to);
        for (int i = la; i <= lb; i++)
        {
            DisplayLine dl = _lines[i];
            int ls = doc.LineStart(i), le = doc.LineEnd(i);
            int a = (i == la ? from : ls) - ls;
            int b = (i == lb ? to : le) - ls;
            int da = dl.SourceToDisplay(a), db = dl.SourceToDisplay(b);
            foreach (TextRect r in dl.Layout.SelectionRects(da, db))
                outp.Add(r with { X = r.X + _indents[i], Y = r.Y + _tops[i] });
        }
    }

    /// <summary>オーバーレイ装飾 (背景/下線/波線/囲み/行背景/ブロック) の描画矩形群 (絶対座標)。
    /// レイアウトに効かない装飾なので、これを毎フレーム呼んでも行 TextLayout は再構築されない。</summary>
    public IReadOnlyList<OverlayRect> OverlayRects()
    {
        var outp = new List<OverlayRect>();
        var tmp = new List<TextRect>();
        foreach (Decoration d in _state.Decorations.All())
        {
            switch (d)
            {
                case MarkDecoration m:
                    if (m.Background is { } bg) EmitRange(m.From, m.To, r => outp.Add(new OverlayRect(r, OverlayKind.Background, bg)), tmp);
                    if (m.Underline is { } ul) EmitRange(m.From, m.To, r => outp.Add(new OverlayRect(r, ul.Wavy ? OverlayKind.WavyUnderline : OverlayKind.Underline, ul.Color)), tmp);
                    if (m.Box is { } box) EmitRange(m.From, m.To, r => outp.Add(new OverlayRect(r, OverlayKind.Box, box.Stroke, box.Fill, box.Radius)), tmp);
                    break;
                case LineDecoration ld:
                {
                    int line = _state.Doc.LineOf(ld.At);
                    outp.Add(new OverlayRect(new TextRect(0, _tops[line], _indents[line] + _lines[line].Layout.Width, _lines[line].Height), OverlayKind.LineBackground, ld.Background));
                    break;
                }
                case BlockDecoration bd:
                {
                    int l0 = _state.Doc.LineOf(bd.From), l1 = _state.Doc.LineOf(bd.To);
                    float y0 = _tops[l0], y1 = _tops[l1] + _lines[l1].Height;
                    if (bd.Background is { } b) outp.Add(new OverlayRect(new TextRect(0, y0, _cfg.MaxWidth, y1 - y0), OverlayKind.BlockBackground, b));
                    if (bd.BarColor is { } bar) outp.Add(new OverlayRect(new TextRect(0, y0, bd.BarWidth, y1 - y0), OverlayKind.BlockBar, bar));
                    break;
                }
            }
        }
        return outp;
    }

    private void EmitRange(int from, int to, Action<TextRect> emit, List<TextRect> tmp)
    {
        tmp.Clear();
        AppendRangeRects(from, to, tmp);
        foreach (TextRect r in tmp) emit(r);
    }

    /// <summary>widget の配置枠 (絶対座標)。view が Key から実 Widget を解決して重ねる。</summary>
    public IReadOnlyList<WidgetSlot> WidgetSlots()
    {
        var outp = new List<WidgetSlot>();
        for (int i = 0; i < _lines.Length; i++)
        {
            DisplayLine dl = _lines[i];
            foreach ((object key, int dispStart) in dl.Widgets())
            {
                TextRect[] rr = dl.Layout.SelectionRects(dispStart, dispStart + 1);
                if (rr.Length > 0) outp.Add(new WidgetSlot(key, rr[0] with { X = rr[0].X + _indents[i], Y = rr[0].Y + _tops[i] }));
            }
        }
        return outp;
    }

    /// <summary>縦移動 — goal-x を保ちつつ上/下の表示行へ。折返し行は同一ソース行内、端は隣ソース行へ。
    /// 戻り値は新しいソースオフセット。<paramref name="goalX"/> は初回に現在 x を記録し以後保つ。</summary>
    public int MoveVertical(int sourceOffset, int dir, ref float? goalX)
    {
        (int line, int col) = _state.Doc.CoordAt(sourceOffset);
        DisplayLine dl = _lines[line];
        int d = dl.SourceToDisplay(col);
        TextRect cr = dl.Layout.CaretRect(d);
        float x = goalX ?? cr.X;
        goalX = x;
        float adv = dl.Layout.LineAdvance;
        int row = Math.Clamp((int)MathF.Round(cr.Y / adv), 0, dl.Layout.LineCount - 1);

        int targetLine; float targetY;
        if (dir < 0)
        {
            if (row > 0) { targetLine = line; targetY = (row - 1) * adv + adv / 2; }
            else if (line > 0)
            {
                targetLine = line - 1;
                TextLayout pl = _lines[targetLine].Layout;
                targetY = (pl.LineCount - 1) * pl.LineAdvance + pl.LineAdvance / 2;
            }
            else return sourceOffset;
        }
        else
        {
            if (row < dl.Layout.LineCount - 1) { targetLine = line; targetY = (row + 1) * adv + adv / 2; }
            else if (line < _lines.Length - 1) { targetLine = line + 1; targetY = _lines[targetLine].Layout.LineAdvance / 2; }
            else return sourceOffset;
        }

        DisplayLine tl = _lines[targetLine];
        int td = tl.Layout.HitTest(x, targetY);
        int tcol = tl.DisplayToSource(td);
        return _state.Doc.LineStart(targetLine) + Math.Min(tcol, _state.Doc.LineText(targetLine).Length);
    }
}
