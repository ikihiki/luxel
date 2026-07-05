using System.Runtime.CompilerServices;
using System.Text;
using Luxel.Document;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// MDX 風 docs ページの補完文字列 (<see cref="Kit.Docs(DocString, bool, IReadOnlyList{IFenceResolver})"/> の引数)。
/// **リテラル部分 = markdown、hole = ライブ UI / テキスト補完**。
///
/// <code>
/// Docs($"""
///     # Button
///
///     ボタンは **Variant × Intent** で配色が決まります。テーマ数: {themeCount}
///
///     {Button(_ => ctx.Log("clicked"), "触ってみて")}
///     """)
/// </code>
///
/// <list type="bullet">
/// <item><see cref="Widget"/> の hole → その位置に**ブロックレベル**のライブ UI (クリック可、状態も生きる)。
///   内部では ```<c>luxel-ui</c> フェンス (hole 連番) に置き換え、既存の embed 基盤
///   (IFenceResolver + BlockWidgetRegistry) に乗せる。</item>
/// <item><see cref="Signal{T}"/> の hole → 現在値のテキスト補完 (構築時評価、非リアクティブ)。</item>
/// <item>その他の hole → ToString のテキスト補完。</item>
/// </list>
/// 文中インライン UI は非対応 (embed はブロック単位) — hole は行境界だけ保証される。
/// **改行は改行として扱われる** (空行も含めソースの行がそのまま表示行になる) ため、
/// 要素の間隔は書き手のソースがそのまま決める。
/// </summary>
/// <summary>DocString の hole に置ける「生 markdown の断片」。テキスト補完 (ToString 焼き込み) と
/// 違い行境界を保証し、ページ本体の markdown として整形される — storysource のコードフェンス等。</summary>
public readonly record struct DocMarkdown(string Markdown);

[InterpolatedStringHandler]
public sealed class DocString
{
    /// <summary>UI hole の埋め込み TypeId (```luxel-ui フェンス)。</summary>
    public const string UiTypeId = "luxel-ui";

    /// <summary>インライン hole のリンクスキーム (<c>{widget:inline}</c> → <c>[￼](luxel-ui:N)</c>)。</summary>
    public const string InlineScheme = "luxel-ui:";

    private readonly StringBuilder _md;
    private bool _afterFence;   // 直前が UI フェンス — 次の追記の頭で行境界を保証する
    internal readonly List<Widget> Holes = new();

    public DocString(int literalLength, int formattedCount)
        => _md = new StringBuilder(literalLength + formattedCount * 24);

    public void AppendLiteral(string s)
    {
        FenceBoundary(s);
        _md.Append(s);
    }

    /// <summary>ブロックレベルのライブ UI (Storybook の Canvas 相当)。
    /// フェンスは行境界だけ保証する (改行は足さない) — 空行は空行として表示される
    /// (改行 = 改行の行指向モデル) ため、区切りの量は書き手のソースがそのまま決める。</summary>
    public void AppendFormatted(Widget widget)
    {
        if (_md.Length > 0 && _md[^1] != '\n') _md.Append('\n');
        _md.Append("```").Append(UiTypeId).Append('\n')
           .Append(Holes.Count).Append("\n```");
        _afterFence = true;   // 閉じフェンスの行終端はソース側の改行に任せる (二重改行を作らない)
        Holes.Add(widget);
    }

    /// <summary>書式付き widget hole: <c>{widget:inline}</c> = **文中インライン** (行内に占位ボックスを
    /// 取り、その矩形へ実体化 — バッジや小さなボタン向け)。他の書式はブロック hole と同じ。
    /// 内部ではリンク記法 <c>[￼](luxel-ui:N)</c> になり、エディタの InlineWidgetResolver が解決する。</summary>
    public void AppendFormatted(Widget widget, string format)
    {
        if (format == "inline")
        {
            FenceBoundary("[");
            _md.Append("[￼](").Append(InlineScheme).Append(Holes.Count).Append(')');
            Holes.Add(widget);
            return;
        }
        AppendFormatted(widget);
    }

    /// <summary>生の markdown を差し込む hole (storysource 等 — テキスト補完と違い行境界を保証する)。
    /// ページ本体のパーサ/ハイライト/リンク機構がそのまま効く。</summary>
    public void AppendFormatted(DocMarkdown md)
    {
        if (_md.Length > 0 && _md[^1] != '\n') _md.Append('\n');
        _md.Append(md.Markdown);
        _afterFence = true;   // 直後の追記は行境界から (フェンスと同じ扱い)
    }

    /// <summary>Signal はその時点の値をテキスト補完 (非リアクティブ)。</summary>
    public void AppendFormatted<T>(Signal<T> signal)
    {
        string s = signal.Peek()?.ToString() ?? "";
        FenceBoundary(s);
        _md.Append(s);
    }

    /// <summary>その他の値はテキスト補完。**Widget 派生は UI hole へ振り直す** —
    /// hole の静的型がコントロール型 (例: ApiTable ファクトリの戻り値) だと、ジェネリックの
    /// 本オーバーロードが <see cref="AppendFormatted(Widget)"/> より優先されるため (恒等変換 > 基底変換)。</summary>
    public void AppendFormatted<T>(T value)
    {
        if (value is Widget w) { AppendFormatted(w); return; }
        string s = value?.ToString() ?? "";
        FenceBoundary(s);
        _md.Append(s);
    }

    /// <summary>フェンス直後の追記がフェンス行に食い込まないよう、必要なときだけ改行を補う。</summary>
    private void FenceBoundary(string next)
    {
        if (_afterFence && (next.Length == 0 || next[0] != '\n')) _md.Append('\n');
        _afterFence = false;
    }

    internal string ToMarkdown() => _md.ToString();

    /// <summary>```luxel-ui フェンスだけ embed へ昇格するリゾルバ (他のフェンスはコードブロックのまま)。</summary>
    internal sealed class UiFenceResolver : IFenceResolver
    {
        public static readonly UiFenceResolver Instance = new();
        public IBlockPayload? Resolve(string info, string body)
            => info.Split(' ', 2)[0] == UiTypeId ? new FencePayload(info, body) : null;
    }
}

public static partial class Kit
{
    /// <summary>
    /// MDX 風 docs ページ (Storybook Docs 相当)。markdown の文章にライブ UI を混ぜて表示する —
    /// <c>Docs($"# 見出し\n\n説明 {Button(...)} ...")</c>。表示は読み取り専用
    /// (<see cref="RichTextEditor.ReadOnly"/>) で、スクロールと埋め込み UI の操作だけ有効。
    /// **サイズは与えられた領域いっぱい** (HAlign/VAlign = Stretch)。UI hole は幅いっぱいの
    /// キャンバス枠 (SurfaceAlt 地 + 角丸) に載る。
    /// </summary>
    public static RichTextEditor Docs(DocString content, bool toc = false,
                                      IReadOnlyList<IFenceResolver>? fences = null)
    {
        var format = new MarkdownFormat();
        format.FenceResolvers.Add(DocString.UiFenceResolver.Instance);
        // 追加フェンス (```mermaid 等 — resolver は別アセンブリが提供、widget は Widgets へ登録)
        if (fences is not null)
            foreach (IFenceResolver f in fences)
                format.FenceResolvers.Add(f);

        List<Widget> holes = content.Holes;
        var widgets = new BlockWidgetRegistry();
        widgets.Register(DocString.UiTypeId, bctx =>
        {
            Widget inner = int.TryParse(((FencePayload)bctx.Payload).Body.Trim(), out int i)
                           && i >= 0 && i < holes.Count
                ? holes[i]
                : Label($"(不明な doc-ui 参照: {((FencePayload)bctx.Payload).Body})");
            // キャンバス枠 (Storybook の Canvas): 幅いっぱい、UI はそのまま操作できる。
            // テーマはホスト (エディタ) の signal を使う — 島でも正しく追従する
            Signal<Theme> theme = bctx.Theme;
            return Border(background: Bind.From(() => theme.Value.SurfaceAlt),
                          rounded: 8, padding: new Thickness(14, 12),
                          width: Length.Percent(100))[inner];
        });

        widgets.Register("table", TableBlocks.Factory());   // markdown テーブルを embed 表示

        string md = content.ToMarkdown();
        if (toc) md = InsertToc(md);
        RichTextEditor doc = RichTextEditor(new Signal<string>(md),
                                            format: format, widgets: widgets);
        // インライン hole ({widget:inline}) の解決 — 同じ N には常に同じインスタンス (状態が生きる)
        doc.InlineWidgetResolver = url =>
            url.StartsWith(DocString.InlineScheme)
            && int.TryParse(url[DocString.InlineScheme.Length..], out int n)
            && n >= 0 && n < holes.Count ? holes[n] : null;
        doc.ReadOnly = true;
        doc.HAlign.SetBase(Align.Stretch);
        doc.VAlign.SetBase(Align.Stretch);
        return doc;
    }

    /// <summary>ctx 付き糖衣: <c>story:</c> リンクを <see cref="StoryContext.Navigate"/> へ、
    /// その他の未知スキームは Log へ配線する (<c>#アンカー</c> はエディタが内部で解決)。
    /// ホストが ResourceSystem を持つなら markdown 画像 <c>![alt](src)</c> も表示される。</summary>
    public static RichTextEditor Docs(StoryContext ctx, DocString content, bool toc = false,
                                      IReadOnlyList<IFenceResolver>? fences = null)
    {
        RichTextEditor doc = Docs(content, toc, fences);
        if (ctx.ResourcesOrNull is { } res)
            doc.WidgetRegistry.Register("image", ImageBlocks.Factory(res));
        Action<RichTextEditor, string> onLink = (_, url) =>   // lambda 直代入は不可 (EV の変換規則) — 変数経由
        {
            if (url.StartsWith("story:")) { ctx.Navigate(url["story:".Length..]); return; }
            if (url.StartsWith("http://") || url.StartsWith("https://"))
            {
                // 外部リンクは既定ブラウザで (ShellExecute)。失敗は Log へ
                try
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                    ctx.Log($"open: {url}");
                }
                catch (Exception e) { ctx.Log($"link 失敗: {url} ({e.Message})"); }
                return;
            }
            ctx.Log($"link: {url}");   // 未知スキームは Log のみ
        };
        doc.OnLink = onLink;
        return doc;
    }

    /// <summary>TOC = アンカーリンク付き markdown リストとして最初の H1 直後へ挿入する
    /// (H1 が無ければ先頭)。ただの markdown なのでエディタのフォント (日本語/絵文字) と
    /// リンク機構 (#アンカー → スクロール) がそのまま効く。H2/H3 が対象、コードフェンス内は無視。</summary>
    private static string InsertToc(string md)
    {
        string[] lines = md.Split('\n');
        var toc = new List<string>();
        bool inFence = false;
        foreach (string l in lines)
        {
            if (l.TrimStart().StartsWith("```")) { inFence = !inFence; continue; }
            if (inFence) continue;
            // Kit.RichTextEditor (ファクトリ) が型名を隠すため完全修飾 (CS0119)
            if (l.StartsWith("## ")) toc.Add($"- [{l[3..].Trim()}](#{Luxel.Controls.RichTextEditor.Slug(l[3..])})");
            else if (l.StartsWith("### ")) toc.Add($"  - [{l[4..].Trim()}](#{Luxel.Controls.RichTextEditor.Slug(l[4..])})");
        }
        if (toc.Count == 0) return md;
        string block = string.Join('\n', toc);
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].StartsWith("# "))
            {
                lines[i] += "\n\n" + block;
                return string.Join('\n', lines);
            }
        return block + "\n\n" + md;
    }
}
