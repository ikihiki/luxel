using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// docs ページ共通の道具箱 — 他ストーリーの埋め込み (StoryRef)、ソース引用 (StorySource)、
/// フェンス拡張 (mermaid)、フォント/ハイライト/widget 配線 (WithDocFonts)。
/// ページ本体は Stories/Docs/ 配下の各ファイルへ。
/// </summary>
internal static class DocsKit
{
    /// <summary>他ストーリーの埋め込み (Storybook の <c>&lt;Story of=... /&gt;</c> 相当)。
    /// docs ページの <paramref name="ctx"/> で実体化するので Log/knob は docs ページに合流する。
    /// <paramref name="knobs"/> = true でストーリーの下に Knobs テーブル (autodoc の Controls 相当) を
    /// 出す — **この Build が登録した knob だけ** (登録数の前後差分で切り出す)。
    /// ソース表示は <see cref="StorySource"/> (生 markdown hole) を隣に置く。
    /// パス不明はエラーカード (ページ全体は落とさない)。</summary>
    internal static Widget StoryRef(StoryContext ctx, string path, bool knobs = false)
    {
        StoryInfo? s = StoryRegistry.Find(path);
        if (s is null)
            return VStack(6)[
                Text(path, 12, color: Bind.From(() => UiTheme.T.TextMuted)),
                Alert($"ストーリーが見つかりません: {path}", Intent.Danger)];

        int before = ctx.Knobs.Count;
        Widget body = s.Build(ctx);
        var parts = new List<Widget>
        {
            Text(path, 12, color: Bind.From(() => UiTheme.T.TextMuted)),
            body,
        };
        if (knobs)
        {
            StoryKnob[] mine = ctx.Knobs.Skip(before).ToArray();
            parts.Add(Divider());
            parts.Add(KnobsTable(mine, width: 640,
                onEdit: (_, k, v) => ctx.QueueKnobEdit(k, v)));
        }
        return VStack(6)[parts.ToArray()];
    }

    /// <summary>ストーリーの C# ソース (storysource — ジェネレーターが焼き込み) をコードフェンスとして
    /// 差し込む生 markdown hole。ページ本体のシンタックスハイライトがそのまま効く。</summary>
    internal static DocMarkdown StorySource(string path)
        => StoryRegistry.Find(path) is { Source.Length: > 0 } s
            ? new DocMarkdown($"```csharp\n{s.Source}\n```")
            : new DocMarkdown($"```\n(ソースなし: {path})\n```");

    /// <summary>docs ページ共通のフェンス拡張 (```mermaid → Luxel.Diagram)。</summary>
    internal static readonly Luxel.Document.IFenceResolver[] DocsFences =
        [Luxel.Diagram.MermaidFenceResolver.Instance];

    /// <summary>日本語/絵文字フォールバック + シンタックスハイライト + mermaid/math widget を配線する。
    /// docs ページは必ずこれで包む。</summary>
    internal static RichTextEditor WithDocFonts(RichTextEditor doc)
    {
        doc.Fonts = StoryKit.JpFallback.Value;
        doc.Highlighter = Luxel.Highlight.TextMateHighlighter.Instance;
        doc.Widgets.Register("mermaid", bc => Luxel.Diagram.Factories.DiagramBlock(
            ((Luxel.Document.FencePayload)bc.Payload).Body, bc.MaxWidth));
        doc.Widgets.Register("math", bc => Luxel.MathText.Factories.MathBlockView(
            ((Luxel.Document.MathPayload)bc.Payload).Source, maxWidth: bc.MaxWidth));
        return doc;
    }
}
