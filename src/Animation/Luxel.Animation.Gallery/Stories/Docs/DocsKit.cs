using Luxel.Controls;
using Luxel.Typography;
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
    internal static DocEmbed StoryRef(StoryContext ctx, string path, bool knobs = false)
    {
        StoryInfo? s = StoryRegistry.Find(path);
        if (s is null)
            return new DocEmbed(VStack(6)[
                Text(path, 12, color: Bind.From(() => UiTheme.T.TextMuted)),
                Alert($"ストーリーが見つかりません: {path}", Intent.Danger)], DocEmbedKind.StoryRef, path);

        Widget BuildNativeEmbed()
        {
            int before = ctx.Knobs.Count;
            // 埋め込みは ctx を共有するが、play はページへ漏らさない (golden はページ自身の play が撮る)
            bool suppressed = ctx.SuppressPlays;
            ctx.SuppressPlays = true;
            Widget body;
            try { body = s.Build(ctx); }
            finally { ctx.SuppressPlays = suppressed; }
            var parts = new List<Widget>
            {
                Text(path, 12, color: Bind.From(() => UiTheme.T.TextMuted)),
                body,
            };
            if (knobs)
            {
                StoryKnob[] mine = ctx.Knobs.Skip(before).ToArray();
                parts.Add(Divider());
                parts.Add(global::Luxel.Gallery.UI.Kit.KnobsTable(mine, width: 640,
                    onEdit: (_, k, v) => ctx.QueueKnobEdit(k, v)));
            }
            return VStack(6)[parts.ToArray()];
        }

        return new DocEmbed(null, DocEmbedKind.StoryRef, path, WidgetFactory: BuildNativeEmbed);
    }

    /// <summary>Story methodの公開された本体だけを表示する。private helperや外部sampleを含む「完全なsource」ではない。
    /// 実行可能sampleは<see cref="SampleSource"/>で実ファイルから引用する。</summary>
    internal static DocMarkdown StorySource(string path)
        => StoryRegistry.Find(path) is { Source.Length: > 0 } s
            ? new DocMarkdown($"```csharp\n{s.Source}\n```")
            : new DocMarkdown($"```\n(ソースなし: {path})\n```");

    /// <summary>実sample fileをGallery assemblyから読み、任意regionをcode fenceとして表示する。
    /// source fileが唯一の正で、native/static/publishのcwdに依存しない。</summary>
    internal static DocMarkdown SampleSource(string relativePath, string? region = null, string? language = null)
        => global::Luxel.Gallery.DocKit.DocsKit.SampleSource(
            typeof(DocsKit).Assembly, relativePath, region, language, searchLoadedAssemblies: true);

    /// <summary>検証済み sample bundle の依存、実行コマンド、実ファイルをまとめて表示する。</summary>
    internal static DocMarkdown SampleBundle(string id)
        => global::Luxel.Gallery.DocKit.DocsKit.SampleBundle(
            typeof(DocsKit).Assembly, id, searchLoadedAssemblies: true);

    internal static string ExtractRegion(string source, string path, string region)
        => global::Luxel.Gallery.DocKit.DocsKit.ExtractRegion(source, path, region);

    /// <summary>Rendering Learnページの統一metadata。テストが同じ表示を機械検証する。</summary>
    internal static DocMarkdown RenderingMeta(string difficulty, string environment, string backend, string prerequisites,
        string? previous = null, string? next = null)
        => global::Luxel.Gallery.DocKit.DocsKit.RenderingMeta(
            difficulty, environment, backend, prerequisites, previous, next);

}
