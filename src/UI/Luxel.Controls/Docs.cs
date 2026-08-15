using System.Runtime.CompilerServices;
using System.Text;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// MDX 風 docs ページの補完文字列。structured document renderingにも使用する。
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
public readonly record struct DocMarkdown(string Markdown) : IMarkdownFragment
{
    public override string ToString() => Markdown;
}

/// <summary>Structured description of a live widget embedded in a <see cref="DocString"/>.
/// Static exporters use this metadata to replace the live widget with an equivalent capture.
/// <paramref name="Reference"/> is the referenced story path for <see cref="DocEmbedKind.StoryRef"/>.</summary>
public sealed record DocEmbed(Widget? Widget, DocEmbedKind Kind = DocEmbedKind.Widget, string? Reference = null,
    bool Inline = false, bool IncludeInherited = false, Func<Widget>? WidgetFactory = null) : IMarkdownEmbed
{
    string IMarkdownEmbed.Kind => Kind.ToString();

    /// <summary>Resolves the native live widget only when an interactive document is realized.</summary>
    public Widget ResolveWidget() => Widget ?? WidgetFactory?.Invoke()
        ?? throw new InvalidOperationException("Documentation embed has no native widget factory.");
}

/// <summary>Semantic documentation exposed without realizing a native widget tree.</summary>
public interface ISemanticDocument
{
    string? DocumentSource { get; }
    IReadOnlyList<DocEmbed> DocumentEmbeds { get; }
}

/// <summary>Kind of live content represented by a <see cref="DocEmbed"/>.</summary>
public enum DocEmbedKind
{
    Widget,
    StoryRef,
    ControlApiTable,
    TypeApiTable,
}

[InterpolatedStringHandler]
public sealed class DocString
{
    /// <summary>UI hole の埋め込み TypeId (```luxel-ui フェンス)。</summary>
    public const string UiTypeId = "luxel-ui";

    /// <summary>インライン hole のリンクスキーム (<c>{widget:inline}</c> → <c>[￼](luxel-ui:N)</c>)。</summary>
    public const string InlineScheme = "luxel-ui:";

    private readonly StringBuilder _md;
    private bool _afterFence;   // 直前が UI フェンス — 次の追記の頭で行境界を保証する
    private readonly List<DocEmbed> _embeds = new();

    /// <summary>組み上がった markdown (```luxel-ui フェンス等を含む)。静的exportでも利用する。</summary>
    public string Markdown => _md.ToString();
    internal string Md => Markdown;
    /// <summary>ブロック hole の Widget 列 (```luxel-ui の index が指す)。native realization時だけ解決する。</summary>
    internal IReadOnlyList<Widget> HoleWidgets => _embeds.Select(static embed => embed.ResolveWidget()).ToArray();
    /// <summary>Structured metadata for every UI hole, in the same index order as <see cref="HoleWidgets"/>.</summary>
    public IReadOnlyList<DocEmbed> Embeds => _embeds;

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
    public void AppendFormatted(Widget widget) => AppendFormatted(new DocEmbed(widget));

    /// <summary>Adds a live UI hole with structured metadata for native rendering and static export.</summary>
    public void AppendFormatted(DocEmbed embed)
    {
        if (_md.Length > 0 && _md[^1] != '\n') _md.Append('\n');
        _md.Append("```").Append(UiTypeId).Append('\n')
           .Append(_embeds.Count).Append("\n```");
        _afterFence = true;   // 閉じフェンスの行終端はソース側の改行に任せる (二重改行を作らない)
        _embeds.Add(embed with { Inline = false });
    }

    /// <summary>書式付き widget hole: <c>{widget:inline}</c> = **文中インライン** (行内に占位ボックスを
    /// 取り、その矩形へ実体化 — バッジや小さなボタン向け)。他の書式はブロック hole と同じ。
    /// 内部ではリンク記法 <c>[￼](luxel-ui:N)</c> になり、エディタの InlineWidgetResolver が解決する。</summary>
    public void AppendFormatted(Widget widget, string format)
    {
        if (format == "inline")
        {
            FenceBoundary("[");
            _md.Append("[￼](").Append(InlineScheme).Append(_embeds.Count).Append(')');
            _embeds.Add(new DocEmbed(widget, Inline: true));
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

}
