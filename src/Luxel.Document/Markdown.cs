using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Luxel.Document;

/// <summary>
/// Markdown ⇄ <see cref="RichDocument"/>。
/// **パースは Markdig (フル CommonMark)**、AST を行指向のブロック列へ写像する:
/// 段落内のソフト改行は**ブロック分割** (1 表示行 = 1 ブロック — エディタの hybrid 表示と
/// round-trip の安定性のため)。**改行は改行として扱う**: トップレベルの空行は Markdig の AST から
/// 消えるが、ソース行の対応から**空段落として復元**する — 書いた空行がそのまま表示され、
/// 空行を含む文書の round-trip が安定する (末尾の改行 1 つだけは行終端とみなし空行にしない)。
/// シリアライザは自前の正規形 (bold=**, リスト=- , インデント=2 空白, 番号付きは連番再採番)。
/// 対象: 見出し/リスト (ネスト)/引用/フェンスコード/水平線 + bold/italic/code/link。
/// リスト/引用の**内側**の空行は対象外 (コンテナ 1 個の span に畳まれる — 正規形で消える)。
/// </summary>
public static class Markdown
{
    // UseSoftlineBreakAsHardlineBreak: 段落内の単一改行を CommonMark のソフト改行 (= 空白に潰れる)
    // ではなくハード改行として扱う Markdig 拡張 — 「改行は改行」の意図をパーサレベルで宣言する。
    // 写像側は LineBreakInline (ソフト/ハード問わず) でブロック分割するため表示は同じだが、
    // AST 上の意味 (IsHard) が正しくなり、将来 IsHard を見る処理を足しても壊れない。
    // UseEmojiAndSmiley: :smile:/:+1: 等のショートコードと :) 等のスマイリーを Unicode 絵文字へ
    // (EmojiInline は LiteralInline 派生なので既存写像で拾える)。
    // UseSmartyPants: "..." → “…”、-- → –、--- → —、... → … (SmartyPant inline は写像側で文字へ)。
    // どちらも変換後の文字がシリアライズで書き戻される正規化 — round-trip は 1 回で収束する。
    // UseAlertBlocks: GitHub alert 記法 `> [!NOTE]` → AlertBlock (QuoteBlock 派生) —
    // Block.Callout へ写像してコールアウト表示に。
    // UseCjkFriendlyEmphasis: 日本語等の文中 **強調** を CommonMark の区切り規則より緩く判定
    // (「日本語の太字が効かない」問題への公式対応)。
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseSoftlineBreakAsHardlineBreak()
        .UseEmojiAndSmiley()
        .UseSmartyPants()
        .UseAlertBlocks()
        .UseCjkFriendlyEmphasis()
        .UseMathematics()   // $..$ / $$..$$ — インラインは TexText で Unicode 正規化、ブロックは MathPayload embed
        .Build();

    // ---- パース (Markdig AST → RichDocument) ----

    public static RichDocument Parse(string markdown) => Parse(markdown, null);

    /// <summary>フェンスリゾルバ付きパース (MarkdownFormat 経由 — フェンス → embed の昇格をリゾルバが判断)。</summary>
    public static RichDocument Parse(string markdown, IReadOnlyList<IFenceResolver>? fenceResolvers)
    {
        string src = (markdown ?? "").Replace("\r", "");   // 行番号計算のため \r は先に落とす (span と一致させる)
        MarkdownDocument ast = Markdig.Markdown.Parse(src, Pipeline);
        var blocks = new List<Block>();
        var ctx = new ParseCtx(src, fenceResolvers);

        // トップレベルブロックの隙間 (= 空行) を空段落として写す。行指向モデル:
        // ソースの 1 行が表示の 1 行 (空行も含めて) に対応する
        string[] lines = src.Split('\n');
        int lastLine = lines.Length;   // 末尾が \n で終わるとき最後の "" は行終端 (空行ではない)
        if (lastLine > 0 && lines[^1].Length == 0) lastLine--;
        int cursor = 0;
        void EmitBlanks(int until)
        {
            for (int l = cursor; l < until && l < lastLine; l++)
                if (string.IsNullOrWhiteSpace(lines[l])) blocks.Add(new Block(BlockKind.Paragraph));
            cursor = Math.Max(cursor, until);
        }

        foreach (Markdig.Syntax.Block child in ast)
        {
            EmitBlanks(child.Line);
            MapBlock(child, blocks, quote: false, listDepth: -1, ordered: false, ctx);
            cursor = Math.Max(cursor, LineOf(src, child.Span.End) + 1);
        }
        EmitBlanks(lastLine);
        return RichDocument.FromBlocks(blocks);
    }

    /// <summary>オフセット位置の行番号 (0 起点)。</summary>
    private static int LineOf(string src, int offset)
    {
        int line = 0;
        for (int i = 0; i < offset && i < src.Length; i++)
            if (src[i] == '\n') line++;
        return line;
    }

    private readonly record struct ParseCtx(string Source, IReadOnlyList<IFenceResolver>? Resolvers);

    /// <summary>1 行分のソース → ブロック (hybrid 表示のアクティブブロック再パース用)。</summary>
    public static Block ParseLine(string line) => Parse(line).Blocks[0];

    private static void MapContainer(ContainerBlock container, List<Block> into, bool quote, int listDepth, bool ordered, ParseCtx ctx)
    {
        foreach (Markdig.Syntax.Block child in container)
            MapBlock(child, into, quote, listDepth, ordered, ctx);
    }

    private static void MapBlock(Markdig.Syntax.Block child, List<Block> into, bool quote, int listDepth, bool ordered, ParseCtx ctx)
    {
        {
            switch (child)
            {
                case HeadingBlock h:
                    EmitInlineBlocks(h.Inline, into,
                        () => new Block(BlockKind.Heading) { HeadingLevel = Math.Clamp(h.Level, 1, 3) });
                    break;
                case ParagraphBlock p:
                    EmitInlineBlocks(p.Inline, into, NewTextBlock, allowImage: listDepth < 0 && !quote);
                    break;
                case Markdig.Extensions.Mathematics.MathBlock mb:
                {
                    var src = new StringBuilder();
                    for (int li = 0; li < mb.Lines.Count; li++)
                    {
                        if (li > 0) src.Append('\n');
                        src.Append(mb.Lines.Lines[li].Slice.ToString());
                    }
                    into.Add(new Block(BlockKind.Embed) { Payload = new MathPayload(src.ToString()) });
                    break;
                }
                case Markdig.Extensions.Alerts.AlertBlock a:
                {
                    // コールアウト: マーカー行 (ラベル表示 + シリアライズで `> [!KIND]` に戻る) +
                    // 本文行 (Callout 印付きの引用) — round-trip 安定
                    string kind = a.Kind.ToString().ToUpperInvariant();
                    var marker = new Block(BlockKind.Quote) { Callout = kind, CalloutMarker = true };
                    marker.Runs.Add(new InlineRun(kind, new InlineStyle { Bold = true }));
                    into.Add(marker);
                    int start = into.Count;
                    MapContainer(a, into, quote: true, listDepth, ordered, ctx);
                    for (int bi = start; bi < into.Count; bi++)
                        if (into[bi].Kind == BlockKind.Quote) into[bi].Callout = kind;
                    break;
                }
                case QuoteBlock q:
                    MapContainer(q, into, quote: true, listDepth, ordered, ctx);
                    break;
                case ListBlock l:
                    foreach (Markdig.Syntax.Block item in l)
                        if (item is ListItemBlock li)
                            MapContainer(li, into, quote, listDepth + 1, l.IsOrdered, ctx);
                    break;
                case Markdig.Extensions.Tables.Table t:
                    into.Add(new Block(BlockKind.Embed) { Payload = MapTable(t, ctx.Source) });
                    break;
                case FencedCodeBlock f:
                {
                    string info = f.Info?.Trim() ?? "";
                    string body = f.Lines.ToString().Replace("\r", "").TrimEnd('\n');
                    IBlockPayload? payload = ResolveFence(ctx.Resolvers, info, body);
                    into.Add(payload is not null ? new Block(BlockKind.Embed) { Payload = payload } : MakeCode(body, info));
                    break;
                }
                case CodeBlock c:   // インデントコード
                    into.Add(MakeCode(c.Lines.ToString().Replace("\r", "").TrimEnd('\n'), ""));
                    break;
                case ThematicBreakBlock:
                    into.Add(new Block(BlockKind.Divider));
                    break;
                case LeafBlock leaf:   // HtmlBlock 等 — リテラルの段落へ落とす (データを失わない)
                    into.Add(new Block(BlockKind.Paragraph, leaf.Lines.ToString()));
                    break;
                case ContainerBlock cb:
                    MapContainer(cb, into, quote, listDepth, ordered, ctx);
                    break;
            }
        }

        Block NewTextBlock() =>
            listDepth >= 0 ? new Block { Kind = BlockKind.ListItem, Depth = listDepth, Ordered = ordered }
            : quote ? new Block(BlockKind.Quote)
            : new Block(BlockKind.Paragraph);

        static Block MakeCode(string body, string lang)
        {
            var code = new Block(BlockKind.CodeBlock) { CodeLang = lang };
            if (body.Length > 0) code.Runs.Add(new InlineRun(body));
            return code;
        }
    }

    private static IBlockPayload? ResolveFence(IReadOnlyList<IFenceResolver>? resolvers, string info, string body)
    {
        if (resolvers is null) return null;
        foreach (IFenceResolver r in resolvers)
            if (r.Resolve(info, body) is IBlockPayload p) return p;
        return null;
    }

    /// <summary>GFM pipe table → TablePayload。セルはソース原文 (インライン記法はリテラル保持)。</summary>
    private static TablePayload MapTable(Markdig.Extensions.Tables.Table t, string source)
    {
        var rows = new List<string[]>();
        foreach (Markdig.Syntax.Block rb in t)
        {
            if (rb is not Markdig.Extensions.Tables.TableRow row) continue;
            var cells = new List<string>();
            foreach (Markdig.Syntax.Block cb in row)
            {
                if (cb is not Markdig.Extensions.Tables.TableCell cell) { cells.Add(""); continue; }
                // セルはソース原文を切り出す (ExtractText だと **x** 等のマーカーが落ちて round-trip が壊れる)
                var span = cell.Span;
                string raw = span.End >= span.Start && span.End < source.Length
                    ? source.Substring(span.Start, span.Length).Trim()
                    : "";
                cells.Add(raw.Replace("\\|", "|"));
            }
            rows.Add(cells.ToArray());
        }
        var aligns = t.ColumnDefinitions.Select(c => c.Alignment switch
        {
            Markdig.Extensions.Tables.TableColumnAlign.Left => TableAlign.Left,
            Markdig.Extensions.Tables.TableColumnAlign.Center => TableAlign.Center,
            Markdig.Extensions.Tables.TableColumnAlign.Right => TableAlign.Right,
            _ => TableAlign.None,
        }).ToArray();
        return new TablePayload(rows, aligns.Length == (rows.Count > 0 ? rows.Max(r => r.Length) : 0) ? aligns : null);
    }

    /// <summary>インラインツリー → run 列。ソフト/ハード改行で**ブロックを分割**する (行指向モデル)。
    /// <paramref name="allowImage"/> のとき「行全体が画像 1 つ」の行は Image embed へ昇格する。</summary>
    private static void EmitInlineBlocks(ContainerInline? inline, List<Block> into, Func<Block> newBlock, bool allowImage = false)
    {
        var runs = new List<InlineRun>();
        ImagePayload? lineImage = null;   // 行頭に現れた画像 (行に他の内容が無ければ embed へ)
        void Flush()
        {
            if (lineImage is not null && runs.Count == 0)
            {
                into.Add(new Block(BlockKind.Embed) { Payload = lineImage });
            }
            else
            {
                if (lineImage is not null) runs.Insert(0, new InlineRun(lineImage.Alt));   // 画像 + 文 → alt に退化
                Block b = newBlock();
                b.Runs.AddRange(MergeAdjacent(runs));
                into.Add(b);
            }
            lineImage = null;
            runs.Clear();
        }

        Walk(inline?.FirstChild, InlineStyle.Plain);
        Flush();

        void Walk(Inline? node, InlineStyle style)
        {
            for (; node is not null; node = node.NextSibling)
            {
                switch (node)
                {
                    case LinkInline { IsImage: true } img:
                        if (allowImage && lineImage is null && runs.Count == 0)
                            lineImage = new ImagePayload(img.Url ?? "", ExtractText(img));
                        else
                            runs.Add(new InlineRun(ExtractText(img), style));   // 文中画像は alt テキスト (v1)
                        break;
                    case LiteralInline lit:
                        runs.Add(new InlineRun(lit.Content.ToString(), style));
                        break;
                    case CodeInline code:
                        runs.Add(new InlineRun(code.Content, style with { Code = true }));
                        break;
                    case EmphasisInline em:
                        Walk(em.FirstChild, em.DelimiterCount >= 2 ? style with { Bold = true } : style with { Italic = true });
                        break;
                    case LinkInline { IsImage: false } link:
                        runs.Add(new InlineRun(ExtractText(link), style with { Link = link.Url ?? "" }));
                        break;
                    case AutolinkInline auto:
                        runs.Add(new InlineRun(auto.Url, style with { Link = auto.Url }));
                        break;
                    case LineBreakInline:
                        Flush();
                        break;
                    case Markdig.Extensions.SmartyPants.SmartyPant sp:
                        runs.Add(new InlineRun(SmartyPantText(sp), style));
                        break;
                    case Markdig.Extensions.Mathematics.MathInline math:
                        // インライン数式は Unicode 正規化して焼き込み (オフセット一貫、round-trip は 1 回で収束)
                        runs.Add(new InlineRun(TexText.ToUnicode(math.Content.ToString()), style with { Math = true }));
                        break;
                    case HtmlInline html:
                        runs.Add(new InlineRun(html.Tag, style));
                        break;
                    case ContainerInline ci:
                        Walk(ci.FirstChild, style);
                        break;
                }
            }
        }

    }

    /// <summary>SmartyPant inline → 置換文字 (未対応型はソースのデリミタ表現のまま)。</summary>
    private static string SmartyPantText(Markdig.Extensions.SmartyPants.SmartyPant sp)
        => sp.Type switch
        {
            Markdig.Extensions.SmartyPants.SmartyPantType.LeftQuote => "‘",
            Markdig.Extensions.SmartyPants.SmartyPantType.RightQuote => "’",
            Markdig.Extensions.SmartyPants.SmartyPantType.LeftDoubleQuote => "“",
            Markdig.Extensions.SmartyPants.SmartyPantType.RightDoubleQuote => "”",
            Markdig.Extensions.SmartyPants.SmartyPantType.LeftAngleQuote => "«",
            Markdig.Extensions.SmartyPants.SmartyPantType.RightAngleQuote => "»",
            Markdig.Extensions.SmartyPants.SmartyPantType.Dash2 => "–",
            Markdig.Extensions.SmartyPants.SmartyPantType.Dash3 => "—",
            Markdig.Extensions.SmartyPants.SmartyPantType.Ellipsis => "…",
            _ => sp.ToString() ?? "",
        };

    private static string ExtractText(ContainerInline container)
    {
        var sb = new StringBuilder();
        for (Inline? n = container.FirstChild; n is not null; n = n.NextSibling)
            switch (n)
            {
                case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
                case CodeInline code: sb.Append(code.Content); break;
                case ContainerInline ci: sb.Append(ExtractText(ci)); break;
            }
        return sb.ToString();
    }

    private static List<InlineRun> MergeAdjacent(List<InlineRun> runs)
    {
        var merged = new List<InlineRun>();
        foreach (InlineRun r in runs)
        {
            if (r.Text.Length == 0) continue;
            if (merged.Count > 0 && merged[^1].Style == r.Style)
                merged[^1] = merged[^1] with { Text = merged[^1].Text + r.Text };
            else merged.Add(r);
        }
        return merged;
    }

    // ---- シリアライズ (正規形: bold=**, リスト=- , インデント=2 空白) ----

    public static string Serialize(RichDocument doc)
    {
        var sb = new StringBuilder();
        var ordinal = new Dictionary<int, int>();   // depth → 連番 (番号付きリストの再採番)
        for (int i = 0; i < doc.Blocks.Count; i++)
        {
            Block b = doc.Blocks[i];
            if (b.Kind != BlockKind.ListItem || !b.Ordered) ordinal.Clear();
            // pipe table は前後に空行がないと段落の継続に食われて表にならない (Markdig の解釈) —
            // テーブルの前後には必ず空行を出す。ただし隣が空段落 (= 明示的な空行) のときは
            // 既に区切れているので重ねない (重ねると再パースで空行が増殖する)
            if (i > 0)
            {
                static bool IsEmptyPara(Block x) => x.Kind == BlockKind.Paragraph && x.Length == 0;
                bool tableBoundary =
                       (b is { Kind: BlockKind.Embed, Payload: TablePayload } && !IsEmptyPara(doc.Blocks[i - 1]))
                    || (doc.Blocks[i - 1] is { Kind: BlockKind.Embed, Payload: TablePayload } && !IsEmptyPara(b));
                sb.Append(tableBoundary ? "\n\n" : "\n");
            }
            sb.Append(SerializeBlock(b, ordinal));
        }
        return sb.ToString();
    }

    /// <summary>1 ブロックのソース (hybrid 表示のアクティブブロック用)。</summary>
    public static string SerializeBlock(Block b) => SerializeBlock(b, new Dictionary<int, int>());

    /// <summary>選択範囲 [min, max) を markdown として書き出す (コピー用)。端のブロックは
    /// 選択部分の run だけを切り出し、ブロック型の記法は保つ。</summary>
    public static string SerializeRange(RichDocument doc, DocPos min, DocPos max)
    {
        if (max < min) (min, max) = (max, min);
        var sb = new StringBuilder();
        var ordinal = new Dictionary<int, int>();
        for (int i = min.Block; i <= max.Block && i < doc.Blocks.Count; i++)
        {
            Block b = doc.Blocks[i];
            int s0 = i == min.Block ? min.Offset : 0;
            int s1 = i == max.Block ? max.Offset : b.Length;
            if (b.Kind != BlockKind.ListItem || !b.Ordered) ordinal.Clear();
            if (i > min.Block) sb.Append('\n');
            sb.Append(SerializeBlock(Slice(b, s0, s1), ordinal));
        }
        return sb.ToString();

        static Block Slice(Block b, int s0, int s1)
        {
            if (s0 == 0 && s1 == b.Length) return b;
            Block c = b.Clone();
            c.Runs.Clear();
            int pos = 0;
            foreach (InlineRun r in b.Runs)
            {
                int rs = pos, re = pos + r.Text.Length;
                pos = re;
                int cutS = Math.Max(rs, s0), cutE = Math.Min(re, s1);
                if (cutS < cutE) c.Runs.Add(r with { Text = r.Text[(cutS - rs)..(cutE - rs)] });
            }
            return c;
        }
    }

    private static string SerializeBlock(Block b, Dictionary<int, int> ordinal)
    {
        switch (b.Kind)
        {
            case BlockKind.Divider: return "---";
            case BlockKind.Embed:
                switch (b.Payload)
                {
                    case ImagePayload img: return $"![{img.Alt}]({img.Src})";
                    case TablePayload table: return table.SerializePipe();
                    case MathPayload math: return $"$$\n{math.Source}\n$$";
                    case IBlockPayload p:
                    {
                        (string info, string body) = p.ToFence();
                        return body.Length > 0 ? $"```{info}\n{body}\n```" : $"```{info}\n```";
                    }
                    default: return "";
                }
            case BlockKind.CodeBlock: return $"```{b.CodeLang}\n{b.Text}\n```";
            case BlockKind.Heading: return new string('#', Math.Clamp(b.HeadingLevel, 1, 3)) + " " + SerializeInline(b.Runs);
            case BlockKind.Quote:
                return b is { CalloutMarker: true, Callout: not null }
                    ? $"> [!{b.Callout}]"
                    : "> " + SerializeInline(b.Runs);
            case BlockKind.ListItem:
            {
                string ind = new(' ', b.Depth * 2);
                if (!b.Ordered) return ind + "- " + SerializeInline(b.Runs);
                ordinal[b.Depth] = ordinal.TryGetValue(b.Depth, out int n) ? n + 1 : 1;
                return $"{ind}{ordinal[b.Depth]}. " + SerializeInline(b.Runs);
            }
            default: return SerializeInline(b.Runs);
        }
    }

    internal static string SerializeInline(IReadOnlyList<InlineRun> runs)
    {
        // bold/italic はスタックで管理し、隣接 run へ継続するスタイルはマーカーを開いたままにする —
        // run 毎に閉じ/開きすると `**bold *****both***` のような壊れた連結になる (round-trip 破壊)。
        var sb = new StringBuilder();
        var stack = new List<char>();   // 'B' = **, 'I' = *
        bool Open(char m) => stack.Contains(m);
        void Pop()
        {
            char m = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            sb.Append(m == 'B' ? "**" : "*");
        }
        void Push(char m)
        {
            stack.Add(m);
            sb.Append(m == 'B' ? "**" : "*");
        }

        foreach (InlineRun r in runs)
        {
            bool b = r.Style.Bold, i = r.Style.Italic;
            // 目標状態に含まれないマーカーが上に乗っていれば pop (正しい入れ子順で閉じる)
            while (stack.Count > 0 && ((Open('B') && !b) || (Open('I') && !i)))
                Pop();
            if (b && !Open('B')) Push('B');
            if (i && !Open('I')) Push('I');

            if (r.Style.Link is string url)
                sb.Append('[').Append(Escape(r.Text)).Append("](").Append(url).Append(')');
            else if (r.Style.Math)
                sb.Append('$').Append(r.Text).Append('$');   // 数式はリテラル (正規化済み Unicode)
            else if (r.Style.Code)
                sb.Append('`').Append(r.Text).Append('`');   // code 内はリテラル
            else
                sb.Append(Escape(r.Text));
        }
        while (stack.Count > 0) Pop();
        return sb.ToString();
    }

    /// <summary>プレーンテキスト中の記法文字をエスケープする (round-trip 安定化)。</summary>
    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c is '*' or '`' or '[' or '\\') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
