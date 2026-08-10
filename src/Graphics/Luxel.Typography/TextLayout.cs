using System.Globalization;
using System.Text;

namespace Luxel.Typography;

/// <summary>折り返しモード。</summary>
public enum TextWrap
{
    /// <summary>折り返さない (1 行互換。\n の段落分割は常に有効)。</summary>
    None = 0,
    /// <summary>語境界で折る (入らない語は文字単位へ緊急フォールバック)。</summary>
    Word,
    /// <summary>文字 (クラスタ) 単位で折る (禁則は維持)。</summary>
    Char,
}

/// <summary>レイアウト左上原点の矩形 (キャレット/選択用)。</summary>
public readonly record struct TextRect(float X, float Y, float Width, float Height);

/// <summary>行の水平整列。</summary>
public enum TextAlign
{
    Left = 0,
    Center,
    Right,
    /// <summary>両端揃え。空白へ分配、空白の無い CJK 行は字間へ分配。段落末行と \n 行は左寄せ。</summary>
    Justify,
}

/// <summary>ボックス内での縦整列 (MaxHeight が有限のときのみ効く)。</summary>
public enum TextVAlign
{
    Top = 0,
    Center,
    Bottom,
}

/// <summary>TextLayout の設定 (レイアウト時に評価。結果は不変)。</summary>
public sealed class TextLayoutOptions
{
    /// <summary>ボックス幅 (論理 px)。∞ = 折り返しなし。</summary>
    public float MaxWidth = float.PositiveInfinity;
    public TextWrap Wrap = TextWrap.Word;
    public TextAlign Align = TextAlign.Left;
    /// <summary>行送り倍率 (行送り = 行内最大サイズ × LineHeight)。</summary>
    public float LineHeight = 1.2f;
    /// <summary>ボックス高さ (論理 px)。有限にすると <see cref="VAlign"/> の基準になり Height もこの値になる。</summary>
    public float MaxHeight = float.PositiveInfinity;
    /// <summary>縦整列 (MaxHeight 有限時のみ)。</summary>
    public TextVAlign VAlign = TextVAlign.Top;
    /// <summary>最大行数 (0 = 無制限)。超過分は切り捨てて最終行末尾に <see cref="Ellipsis"/> を付ける。</summary>
    public int MaxLines;
    /// <summary>切り捨て記号 (既定 "…")。</summary>
    public string Ellipsis = "…";
    /// <summary>分割判定 (null = <see cref="TextSegmenter.Default"/>)。</summary>
    public ITextSegmenter? Segmenter;
}

/// <summary>
/// ボックス内テキストのレイアウト結果。プレーン string もリッチ (スパン列 = フォント/サイズ/色の混在) も
/// 同じパイプラインで扱う: 段落 (\n) → run 分割 (スタイル境界 + フォントフォールバック) →
/// シェーピング (HarfBuzz, run 毎) → 行分割 (Segmenter の break 機会 + 禁則) → 行組み (整列/Justify)。
/// 行内ベースラインは最大 ascent に揃え、行送りは行内最大サイズ基準。
/// 計測・描画・キャレット・ヒットテストがすべて同じ結果を参照する。クラスタは分割しない。
/// </summary>
public sealed class TextLayout
{
    private readonly FontCollection _fonts;
    private readonly float _px;             // 既定サイズ
    private readonly TextLayoutOptions _opt;
    private readonly Para[] _paras;
    private readonly List<Line> _lines = new();

    private sealed class Run
    {
        public required VectorFont Font;
        public required float Px;
        public required uint Color;
        public int CharStart;               // 段落内 char オフセット
        public ShapedGlyph[] Glyphs = [];
        public float AscentPx;
        public bool IsBox;                  // インラインボックス (占位のみ — グリフは描かない)
    }

    /// <summary>段落内グリフの視覚順参照 (run はスタイル/フォールバック境界で分かれる)。</summary>
    private readonly record struct GRef(int Run, int Glyph);

    private sealed class Para
    {
        public required string Text;
        public required int CharOffset;     // 全体 string 内オフセット
        public List<Run> Runs = new();
        public GRef[] Refs = [];
        public int[] Graphemes = [];
    }

    internal struct Line
    {
        public int Para;
        public int RefStart, RefEnd;        // 段落 Refs 内 [start, end) — 末尾空白を含む (キャレット用)
        public int RefVisEnd;               // 描画/整列用の末尾 (末尾空白を除く)
        public int CharStart, CharEnd;      // 段落内 char [start, end)
        public float Width;                 // 末尾空白を除いた自然幅
        public float AlignX;
        public float JustifyExtra;          // gap 1 つあたりの加算 px (0 = なし)
        public bool JustifyBySpace;
        public bool LastOfParagraph;
        public bool Ellipsis;               // この行の末尾に切り捨て記号を描く
        public float Top, MaxPx, AscentPx, AdvancePx;   // 行組み後のメトリクス
    }

    // 切り捨て記号 (MaxLines 超過時のみ)
    private ShapedGlyph[] _ellGlyphs = [];
    private VectorFont? _ellFont;
    private float _ellPx, _ellWidth;
    private uint _ellColor;

    /// <summary>MaxLines 超過で切り捨てたか。</summary>
    public bool Truncated { get; private set; }

    /// <summary>プレーンテキスト (単一フォント/サイズ)。</summary>
    public TextLayout(VectorFont font, string text, float pixelHeight, TextLayoutOptions? options = null)
        : this(new FontCollection(font), [new TextSpan(text)], pixelHeight, options) { }

    /// <summary>リッチテキスト。スパンのフォント/サイズ未指定は <paramref name="fonts"/>.Primary /
    /// <paramref name="defaultPixelHeight"/>。グリフ未収載はコレクション内でフォールバックする。</summary>
    public TextLayout(FontCollection fonts, IReadOnlyList<TextSpan> spans, float defaultPixelHeight,
                      TextLayoutOptions? options = null)
    {
        _fonts = fonts;
        _px = defaultPixelHeight;
        _opt = options ?? new TextLayoutOptions();
        ITextSegmenter seg = _opt.Segmenter ?? TextSegmenter.Default;

        _paras = BuildParagraphs(spans, seg);
        foreach (Para para in _paras) BreakParagraph(para, seg);
        ApplyMaxLines();
        AssignAlignment();
        ComputeLineMetrics();
    }

    // ---- キャッシュ (per-font — フォントはスレッド所有なので島安全) ----

    internal readonly record struct LayoutKey(
        string Text, float Px, float MaxW, TextWrap Wrap, TextAlign Align, float LineHeight,
        float MaxH, TextVAlign VAlign, int MaxLines, string Ellipsis);

    private static readonly TextLayoutOptions DefaultOptions = new();

    /// <summary>プレーンテキストのレイアウトをフォント毎のキャッシュから取得/構築する。
    /// TextLayout は不変なので同条件は再利用できる (SetRoot 再構築の再レイアウト対策)。
    /// カスタム Segmenter 指定時はキャッシュしない。上限超過で全クリア (LRU なしの簡易運用)。</summary>
    public static TextLayout Get(VectorFont font, string text, float pixelHeight, TextLayoutOptions? options = null)
    {
        TextLayoutOptions o = options ?? DefaultOptions;
        if (o.Segmenter is not null) return new TextLayout(font, text, pixelHeight, o);
        var key = new LayoutKey(text, pixelHeight, o.MaxWidth, o.Wrap, o.Align, o.LineHeight,
                                o.MaxHeight, o.VAlign, o.MaxLines, o.Ellipsis);
        Dictionary<LayoutKey, TextLayout> cache = font.LayoutCache;
        if (cache.TryGetValue(key, out TextLayout? hit)) return hit;
        if (cache.Count >= 512) cache.Clear();
        var layout = new TextLayout(font, text, pixelHeight, o);
        cache[key] = layout;
        return layout;
    }

    // ---- ①② 段落分割 + run 分割 (スタイル境界 / フォールバック境界) + ③ シェーピング ----

    private Para[] BuildParagraphs(IReadOnlyList<TextSpan> spans, ITextSegmenter seg)
    {
        // 段落ごとの (テキスト片, スタイル) 列へ再構成
        var paras = new List<List<(string Text, SpanStyle Style)>> { new() };
        foreach (TextSpan span in spans)
        {
            string s = span.Text.Replace("\r", "");
            int start = 0;
            while (true)
            {
                int nl = s.IndexOf('\n', start);
                string piece = nl < 0 ? s[start..] : s[start..nl];
                if (piece.Length > 0) paras[^1].Add((piece, span.Style));
                if (nl < 0) break;
                paras.Add(new List<(string, SpanStyle)>());
                start = nl + 1;
            }
        }

        var result = new Para[paras.Count];
        int charOffset = 0;
        for (int p = 0; p < paras.Count; p++)
        {
            string text = string.Concat(paras[p].Select(x => x.Text));
            var para = new Para { Text = text, CharOffset = charOffset, Graphemes = seg.GetGraphemeBoundaries(text) };
            int charStart = 0;
            foreach ((string piece, SpanStyle style) in paras[p])
            {
                if (style.BoxW > 0)
                {
                    // インラインボックス: piece 全体 (占位 1 文字) を advance = BoxW の 1 疑似グリフに。
                    // 行高へは BoxH (Px) で寄与しつつ、箱の縦中心を本文 em ボックスの中心へ合わせる
                    // (CSS の vertical-align: middle 相当)。旧実装は下端をベースラインに揃えていたため、
                    // 本文が箱の底に沈み込み行内 widget が本文よりやや上に見えていた。
                    float bh = MathF.Max(style.BoxH, 1);
                    float mid = _fonts.Primary.Ascent(_px) - _px * 0.5f;   // ベースライン→本文 em 中心の距離
                    para.Runs.Add(new Run
                    {
                        Font = _fonts.Primary,
                        Px = bh,
                        Color = style.Color,
                        CharStart = charStart,
                        AscentPx = bh * 0.5f + mid,   // 箱中心を本文中心へ (箱が本文より高いとき上端=行頭に一致)
                        IsBox = true,
                        Glyphs = [new ShapedGlyph(0, 0, style.BoxW, 0, 0)],
                    });
                    charStart += piece.Length;
                    continue;
                }
                foreach ((string sub, VectorFont font) in SplitByFont(piece, style.Font))
                {
                    float px = style.Size ?? _px;
                    var run = new Run
                    {
                        Font = font,
                        Px = px,
                        Color = style.Color,
                        CharStart = charStart,
                        Glyphs = font.ShapeRun(sub, px),
                        AscentPx = font.Ascent(px),
                    };
                    para.Runs.Add(run);
                    charStart += sub.Length;
                }
            }
            // 視覚順の参照列
            var refs = new List<GRef>();
            for (int r = 0; r < para.Runs.Count; r++)
                for (int g = 0; g < para.Runs[r].Glyphs.Length; g++)
                    refs.Add(new GRef(r, g));
            para.Refs = refs.ToArray();
            result[p] = para;
            charOffset += text.Length + 1;   // +1 = '\n'
        }
        return result;
    }

    /// <summary>グリフ収載でテキストをフォント別の連続片へ分割する (結合文字は直前のフォントに従う)。</summary>
    private IEnumerable<(string Text, VectorFont Font)> SplitByFont(string text, VectorFont? preferred)
    {
        VectorFont def = preferred ?? _fonts.Primary;
        int segStart = 0;
        VectorFont cur = def;
        bool first = true;
        int i = 0;
        while (i < text.Length)
        {
            Rune rune = Rune.GetRuneAt(text, i);
            UnicodeCategory cat = Rune.GetUnicodeCategory(rune);
            bool combining = cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
            VectorFont f = combining && !first ? cur : _fonts.FontFor(rune.Value, preferred);
            if (first) { cur = f; first = false; }
            else if (!ReferenceEquals(f, cur))
            {
                yield return (text[segStart..i], cur);
                segStart = i;
                cur = f;
            }
            i += rune.Utf16SequenceLength;
        }
        if (segStart < text.Length) yield return (text[segStart..], cur);
    }

    // ---- 参照ヘルパ ----

    private static float Adv(Para p, GRef r) => p.Runs[r.Run].Glyphs[r.Glyph].XAdvance;
    /// <summary>段落内 char index としてのクラスタ (run 跨ぎでも一意)。</summary>
    private static int ClusterOf(Para p, GRef r) => p.Runs[r.Run].CharStart + p.Runs[r.Run].Glyphs[r.Glyph].Cluster;
    private static bool IsSpaceChar(Para p, int cluster)
        => cluster >= 0 && cluster < p.Text.Length && p.Text[cluster] is ' ' or '\t' or '　';

    // ---- 公開結果 ----

    /// <summary>レイアウト後の実寸 (論理 px)。Left 整列は最長行にタイト、それ以外はボックス幅。</summary>
    public float Width { get; private set; }
    public float Height { get; private set; }
    public int LineCount => _lines.Count;
    /// <summary>既定サイズ基準の行送り (px)。行内最大サイズが違う行は行毎の実送りが優先される。</summary>
    public float LineAdvance => _px * _opt.LineHeight;
    /// <summary>run が持つ色の一覧 (重複なし、出現順)。RichTextView の色別ノード分割用。</summary>
    public IReadOnlyList<uint> Colors
    {
        get
        {
            var list = new List<uint>();
            foreach (Para p in _paras)
                foreach (Run r in p.Runs)
                    if (!list.Contains(r.Color)) list.Add(r.Color);
            if (list.Count == 0) list.Add(0xFF000000);
            return list;
        }
    }

    /// <summary>行のテキスト範囲 (全体 string の char index, [start, end))。</summary>
    public (int Start, int End) LineCharRange(int line)
    {
        Line l = _lines[line];
        int off = _paras[l.Para].CharOffset;
        return (off + l.CharStart, off + l.CharEnd);
    }

    public float LineWidth(int line) => _lines[line].Width;
    public float LineX(int line) => _lines[line].AlignX;
    public float LineJustifyExtra(int line) => _lines[line].JustifyExtra;

    /// <summary>配置済みグリフを視覚順に列挙する。描画backend adapterから利用する。</summary>
    internal void VisitGlyphs(
        float x,
        float y,
        uint? filterRunColor,
        Action<VectorFont, uint, float, float, float> visitor)
    {
        foreach (Line l in _lines)
        {
            Para para = _paras[l.Para];
            float baseline = y + l.Top + l.AscentPx;
            float penX = x + l.AlignX;
            int prevCluster = -1;
            for (int i = l.RefStart; i < l.RefVisEnd; i++)
            {
                GRef gr = para.Refs[i];
                Run run = para.Runs[gr.Run];
                int cluster = ClusterOf(para, gr);
                if (l.JustifyExtra > 0 && !l.JustifyBySpace && prevCluster >= 0 && cluster != prevCluster)
                    penX += l.JustifyExtra;
                prevCluster = cluster;

                if (!run.IsBox && (filterRunColor is not uint fc || run.Color == fc))
                {
                    ShapedGlyph sg = run.Glyphs[gr.Glyph];
                    visitor(run.Font, sg.GlyphId, penX + sg.XOffset, baseline - sg.YOffset, run.Px);
                }
                penX += Adv(para, gr);
                if (l.JustifyExtra > 0 && l.JustifyBySpace && IsSpaceChar(para, cluster))
                    penX += l.JustifyExtra;
            }

            if (l.Ellipsis && _ellFont is not null && (filterRunColor is not uint efc || _ellColor == efc))
            {
                foreach (ShapedGlyph g in _ellGlyphs)
                {
                    visitor(_ellFont, g.GlyphId, penX + g.XOffset, baseline - g.YOffset, _ellPx);
                    penX += g.XAdvance;
                }
            }
        }
    }

    // ---- キャレット / ヒットテスト (クラスタ + グラフェム按分) ----

    /// <summary>キャレット矩形 (レイアウト左上原点)。index は全体 string の char index。</summary>
    public TextRect CaretRect(int index)
    {
        int line = LineOf(index);
        Line l = _lines[line];
        return new TextRect(CaretXInLine(line, index), l.Top, 1, l.MaxPx);
    }

    /// <summary>位置が属する行のベースライン (行 Top からの ascent)。インラインボックスの
    /// 縦位置決め用 (ボックス上端 = Top + ascent - BoxH)。</summary>
    public float LineAscentAt(int index) => _lines[LineOf(index)].AscentPx;

    /// <summary>座標 → テキスト位置 (グラフェム境界へ吸着)。</summary>
    public int HitTest(float x, float y)
    {
        int line = 0;
        while (line + 1 < _lines.Count && y >= _lines[line + 1].Top) line++;
        Line l = _lines[line];
        Para para = _paras[l.Para];
        float tx = x - l.AlignX;
        if (tx <= 0) return para.CharOffset + l.CharStart;

        float acc = 0;
        int prevCluster = -1;
        int i = l.RefStart;
        while (i < l.RefEnd)
        {
            (int cluster, int endChar, int next, float adv) = ClusterAt(l, para, i);
            if (l.JustifyExtra > 0 && !l.JustifyBySpace && prevCluster >= 0) acc += l.JustifyExtra;
            float advTotal = adv + (l.JustifyExtra > 0 && l.JustifyBySpace && IsSpaceChar(para, cluster) ? l.JustifyExtra : 0);
            if (tx < acc + advTotal)
            {
                int[] gb = BoundariesIn(para, cluster, endChar);
                int m = gb.Length - 1;
                float frac = (tx - acc) / MathF.Max(1e-3f, advTotal);
                int j = Math.Clamp((int)MathF.Round(frac * m), 0, m);
                return para.CharOffset + gb[j];
            }
            acc += advTotal;
            prevCluster = cluster;
            i = next;
        }
        return para.CharOffset + l.CharEnd;
    }

    /// <summary>選択範囲 [start, end) の行別矩形 (レイアウト左上原点)。</summary>
    public TextRect[] SelectionRects(int start, int end)
    {
        if (end < start) (start, end) = (end, start);
        var rects = new List<TextRect>();
        for (int i = 0; i < _lines.Count; i++)
        {
            (int ls, int le) = LineCharRange(i);
            int s = Math.Max(start, ls), e = Math.Min(end, le);
            if (s >= e) continue;
            float x0 = CaretXInLine(i, s), x1 = CaretXInLine(i, e);
            rects.Add(new TextRect(x0, _lines[i].Top, MathF.Max(1, x1 - x0), _lines[i].MaxPx));
        }
        return rects.ToArray();
    }

    private int LineOf(int index)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            (int s, int e) = LineCharRange(i);
            if (index <= e) return index >= s ? i : Math.Max(0, i - 1);
        }
        return _lines.Count - 1;
    }

    private float CaretXInLine(int line, int index)
    {
        Line l = _lines[line];
        Para para = _paras[l.Para];
        int local = Math.Clamp(index - para.CharOffset, l.CharStart, l.CharEnd);

        float x = l.AlignX;
        int prevCluster = -1;
        int i = l.RefStart;
        while (i < l.RefEnd)
        {
            (int cluster, int endChar, int next, float adv) = ClusterAt(l, para, i);
            if (local <= cluster) return x;
            if (l.JustifyExtra > 0 && !l.JustifyBySpace && prevCluster >= 0) x += l.JustifyExtra;
            float advTotal = adv + (l.JustifyExtra > 0 && l.JustifyBySpace && IsSpaceChar(para, cluster) ? l.JustifyExtra : 0);
            if (local < endChar)
            {
                int[] gb = BoundariesIn(para, cluster, endChar);
                int m = gb.Length - 1;
                int j = 0;
                while (j < m && gb[j + 1] <= local) j++;
                return x + advTotal * j / MathF.Max(1, m);
            }
            x += advTotal;
            prevCluster = cluster;
            i = next;
        }
        return x;
    }

    /// <summary>参照 i から始まるクラスタの (クラスタ char, 次クラスタ char, 次参照 index, advance 合計)。
    /// クラスタは run を跨がない (run 分割は char 境界)。</summary>
    private (int Cluster, int EndChar, int Next, float Advance) ClusterAt(in Line l, Para para, int i)
    {
        int cluster = ClusterOf(para, para.Refs[i]);
        float adv = 0;
        while (i < l.RefEnd && ClusterOf(para, para.Refs[i]) == cluster) { adv += Adv(para, para.Refs[i]); i++; }
        int endChar = i < l.RefEnd ? ClusterOf(para, para.Refs[i]) : l.CharEnd;
        return (cluster, endChar, i, adv);
    }

    private static int[] BoundariesIn(Para para, int start, int end)
    {
        var list = new List<int> { start };
        foreach (int b in para.Graphemes)
            if (b > start && b < end) list.Add(b);
        list.Add(end);
        return list.ToArray();
    }

    // ---- ④ 行分割 ----

    private void BreakParagraph(Para para, ITextSegmenter seg)
    {
        int paraIdx = Array.IndexOf(_paras, para);
        GRef[] refs = para.Refs;
        int len = para.Text.Length;
        if (refs.Length == 0 || len == 0)
        {
            _lines.Add(new Line { Para = paraIdx, LastOfParagraph = true });
            return;
        }

        Span<LineBreakKind> breaks = len <= 512 ? stackalloc LineBreakKind[len] : new LineBreakKind[len];
        seg.GetLineBreaks(para.Text, breaks);

        bool wrap = _opt.Wrap != TextWrap.None && !float.IsInfinity(_opt.MaxWidth);
        if (!wrap)
        {
            AddLine(paraIdx, para, 0, refs.Length, 0, len, lastOfParagraph: true);
            return;
        }

        LineBreakKind minKind = _opt.Wrap == TextWrap.Char ? LineBreakKind.CharAllowed : LineBreakKind.Allowed;

        int lineRefStart = 0, lineCharStart = 0;
        float w = 0;
        int wordRef = -1, wordChar = -1;
        int charRef = -1, charChar = -1;

        for (int i = 0; i < refs.Length; i++)
        {
            if (i > lineRefStart && ClusterOf(para, refs[i]) != ClusterOf(para, refs[i - 1]))
            {
                int c = ClusterOf(para, refs[i]);
                LineBreakKind k = c > 0 ? breaks[c - 1] : LineBreakKind.Prohibited;
                if (k >= LineBreakKind.Allowed) { wordRef = i; wordChar = c; }
                if (k >= LineBreakKind.CharAllowed) { charRef = i; charChar = c; }
            }

            w += Adv(para, refs[i]);
            // 行末の空白は溢れ扱いにしない (ぶら下げ)
            if (w <= _opt.MaxWidth || i == lineRefStart || IsSpaceChar(para, ClusterOf(para, refs[i]))) continue;

            int br, bc;
            if (minKind == LineBreakKind.Allowed && wordRef > lineRefStart) { br = wordRef; bc = wordChar; }
            else if (charRef > lineRefStart) { br = charRef; bc = charChar; }
            else
            {
                int cs = ClusterOf(para, refs[i]);
                br = i;
                while (br > lineRefStart && ClusterOf(para, refs[br - 1]) == cs) br--;
                if (br == lineRefStart)
                {
                    br = i + 1;
                    bc = i + 1 < refs.Length ? ClusterOf(para, refs[i + 1]) : len;
                }
                else bc = cs;
            }

            AddLine(paraIdx, para, lineRefStart, br, lineCharStart, bc, lastOfParagraph: false);

            // 次行へ (先頭の空白は読み飛ばし、前行の範囲へ取り込む)
            while (br < refs.Length && IsSpaceChar(para, ClusterOf(para, refs[br]))) br++;
            lineRefStart = br;
            lineCharStart = br < refs.Length ? ClusterOf(para, refs[br]) : len;
            Line prev = _lines[^1];
            prev.RefEnd = br;
            prev.CharEnd = lineCharStart;
            _lines[^1] = prev;

            wordRef = charRef = -1;
            w = 0;
            for (int k2 = lineRefStart; k2 <= i && k2 < refs.Length; k2++) w += Adv(para, refs[k2]);
            if (lineRefStart > i) i = lineRefStart - 1;
        }
        if (lineRefStart < refs.Length)
            AddLine(paraIdx, para, lineRefStart, refs.Length, lineCharStart, len, lastOfParagraph: true);
        else if (_lines.Count > 0 && _lines[^1].Para == paraIdx)
        {
            Line last = _lines[^1];
            last.LastOfParagraph = true;
            _lines[^1] = last;
        }
    }

    private void AddLine(int paraIdx, Para para, int rs, int re, int cs, int ce, bool lastOfParagraph)
    {
        int reVis = re;
        while (reVis > rs && IsSpaceChar(para, ClusterOf(para, para.Refs[reVis - 1]))) reVis--;
        float width = 0;
        for (int i = rs; i < reVis; i++) width += Adv(para, para.Refs[i]);
        _lines.Add(new Line
        {
            Para = paraIdx,
            RefStart = rs,
            RefEnd = re,
            RefVisEnd = reVis,
            CharStart = cs,
            CharEnd = ce,
            Width = width,
            LastOfParagraph = lastOfParagraph,
        });
    }

    // ---- MaxLines / ellipsis ----

    private void ApplyMaxLines()
    {
        if (_opt.MaxLines <= 0 || _lines.Count <= _opt.MaxLines) return;
        Truncated = true;
        _lines.RemoveRange(_opt.MaxLines, _lines.Count - _opt.MaxLines);

        int li = _lines.Count - 1;
        Line l = _lines[li];
        Para para = _paras[l.Para];

        // 記号は最終可視 run のスタイル (無ければ既定)
        if (l.RefVisEnd > l.RefStart)
        {
            Run r = para.Runs[para.Refs[l.RefVisEnd - 1].Run];
            (_ellFont, _ellPx, _ellColor) = (r.Font, r.Px, r.Color);
        }
        else (_ellFont, _ellPx, _ellColor) = (_fonts.Primary, _px, 0xFF000000u);
        _ellGlyphs = _ellFont.ShapeRun(_opt.Ellipsis, _ellPx);
        _ellWidth = 0;
        foreach (ShapedGlyph g in _ellGlyphs) _ellWidth += g.XAdvance;

        // 記号込みでボックスに収まるまでクラスタ単位で末尾を削る
        float box = _opt.MaxWidth;
        while (!float.IsInfinity(box) && l.Width + _ellWidth > box && l.RefVisEnd > l.RefStart)
        {
            int c = ClusterOf(para, para.Refs[l.RefVisEnd - 1]);
            while (l.RefVisEnd > l.RefStart && ClusterOf(para, para.Refs[l.RefVisEnd - 1]) == c) l.RefVisEnd--;
            while (l.RefVisEnd > l.RefStart && IsSpaceChar(para, ClusterOf(para, para.Refs[l.RefVisEnd - 1]))) l.RefVisEnd--;
            float w = 0;
            for (int i = l.RefStart; i < l.RefVisEnd; i++) w += Adv(para, para.Refs[i]);
            l.Width = w;
        }
        l.Width += _ellWidth;
        l.Ellipsis = true;
        l.LastOfParagraph = true;   // Justify は掛けない
        _lines[li] = l;
    }

    // ---- ⑤ 行組み (整列 / Justify / 行メトリクス) ----

    private void AssignAlignment()
    {
        float maxLine = 0;
        foreach (Line l in _lines) maxLine = MathF.Max(maxLine, l.Width);
        float box = float.IsInfinity(_opt.MaxWidth) ? maxLine : _opt.MaxWidth;

        for (int i = 0; i < _lines.Count; i++)
        {
            Line l = _lines[i];
            switch (_opt.Align)
            {
                case TextAlign.Center: l.AlignX = MathF.Max(0, (box - l.Width) / 2); break;
                case TextAlign.Right: l.AlignX = MathF.Max(0, box - l.Width); break;
                case TextAlign.Justify when !l.LastOfParagraph && l.Width < box:
                    {
                        Para para = _paras[l.Para];
                        int spaceGaps = 0;
                        for (int g = l.RefStart; g < l.RefVisEnd; g++)
                            if (IsSpaceChar(para, ClusterOf(para, para.Refs[g]))) spaceGaps++;
                        float shortfall = box - l.Width;
                        if (spaceGaps > 0 && shortfall / spaceGaps <= _px * 2)
                        {
                            l.JustifyExtra = shortfall / spaceGaps;
                            l.JustifyBySpace = true;
                        }
                        else
                        {
                            int clusters = 0, prev = -1;
                            for (int g = l.RefStart; g < l.RefVisEnd; g++)
                            {
                                int c = ClusterOf(para, para.Refs[g]);
                                if (c != prev) { clusters++; prev = c; }
                            }
                            int gaps = clusters - 1;
                            if (gaps > 0 && shortfall / gaps <= _px * 0.5f)
                            {
                                l.JustifyExtra = shortfall / gaps;
                                l.JustifyBySpace = false;
                            }
                        }
                        break;
                    }
            }
            _lines[i] = l;
        }
    }

    private void ComputeLineMetrics()
    {
        float top = 0, maxLine = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            Line l = _lines[i];
            Para para = _paras[l.Para];
            float maxPx = 0, ascent = 0;
            for (int g = l.RefStart; g < l.RefEnd; g++)
            {
                Run run = para.Runs[para.Refs[g].Run];
                maxPx = MathF.Max(maxPx, run.Px);
                ascent = MathF.Max(ascent, run.AscentPx);
            }
            if (maxPx == 0) { maxPx = _px; ascent = _fonts.Primary.Ascent(_px); }   // 空行
            l.Top = top;
            l.MaxPx = maxPx;
            l.AscentPx = ascent;
            l.AdvancePx = maxPx * _opt.LineHeight;
            top += l.AdvancePx;
            maxLine = MathF.Max(maxLine, l.Width);
            _lines[i] = l;
        }
        Width = _opt.Align == TextAlign.Left || float.IsInfinity(_opt.MaxWidth) ? maxLine : _opt.MaxWidth;
        float contentH = _lines.Count == 0 ? _px : _lines[^1].Top + _lines[^1].MaxPx;

        // 縦整列 (MaxHeight 有限時のみ): 各行の Top を平行移動 — キャレット/ヒットも自動で追従
        if (!float.IsInfinity(_opt.MaxHeight))
        {
            float extra = MathF.Max(0, _opt.MaxHeight - contentH);
            float yOff = _opt.VAlign switch
            {
                TextVAlign.Center => extra / 2,
                TextVAlign.Bottom => extra,
                _ => 0,
            };
            if (yOff > 0)
                for (int i = 0; i < _lines.Count; i++)
                {
                    Line l = _lines[i];
                    l.Top += yOff;
                    _lines[i] = l;
                }
            Height = _opt.MaxHeight;
        }
        else Height = contentH;
    }
}
