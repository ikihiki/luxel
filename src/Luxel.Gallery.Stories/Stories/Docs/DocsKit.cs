using System.Text;
using System.Diagnostics;
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
            parts.Add(KnobsTable(mine, width: 640,
                onEdit: (_, k, v) => ctx.QueueKnobEdit(k, v)));
        }
        return new DocEmbed(VStack(6)[parts.ToArray()], DocEmbedKind.StoryRef, path);
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
    {
        string resource = "Luxel.Sample." + relativePath.Replace('\\', '.').Replace('/', '.');
        using Stream stream = typeof(DocsKit).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded sample source not found: {relativePath} ({resource})");
        using var reader = new StreamReader(stream);
        string source = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        if (region is not null) source = ExtractRegion(source, relativePath, region);
        language ??= Path.GetExtension(relativePath).ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".slang" => "slang",
            ".csproj" => "xml",
            ".ps1" => "powershell",
            _ => "text",
        };
        return new DocMarkdown($"```{language}\n{source.TrimEnd()}\n```");
    }

    /// <summary>検証済み sample bundle の依存、実行コマンド、実ファイルをまとめて表示する。</summary>
    internal static DocMarkdown SampleBundle(string id)
    {
        SampleBundleInfo bundle = SampleBundleRegistry.Find(id)
            ?? throw new InvalidOperationException($"Unknown sample bundle: {id}");
        string dependencies = bundle.Dependencies is { Count: > 0 }
            ? string.Join(", ", bundle.Dependencies.Select(x => $"`{x}`")) : "なし";
        string requirements = bundle.Requirements is { Count: > 0 }
            ? string.Join(" / ", bundle.Requirements) : "なし";
        var text = new StringBuilder();
        text.AppendLine($"## コピーして動かす — {bundle.Name}");
        text.AppendLine();
        text.AppendLine($"> **{bundle.CopyLevel}** · 難易度: {bundle.Difficulty} · 依存 bundle: {dependencies}");
        text.AppendLine();
        text.AppendLine(bundle.Description);
        text.AppendLine();
        text.AppendLine($"**必要条件:** {requirements}");
        if (bundle.Platforms is { Count: > 0 }) text.AppendLine($"  **Platform:** {string.Join(" / ", bundle.Platforms)}");
        text.AppendLine($"  **検証契約:** exit `{bundle.ExpectedExitCode}` / timeout `{bundle.TimeoutSeconds}s`"
            + (bundle.ExpectedStdoutMarker is null ? "" : $" / stdout `{bundle.ExpectedStdoutMarker}`"));
        if (bundle.ExpectedArtifacts is { Count: > 0 }) text.AppendLine($"  **生成物:** {string.Join(", ", bundle.ExpectedArtifacts.Select(x => $"`{x}`"))}");
        if (bundle.ExportSymbol is not null) text.AppendLine($"  **接続点:** `{bundle.ExportSymbol}`");
        if (bundle.RunCommand is not null) text.AppendLine($"\n**Run**\n```powershell\n{bundle.RunCommand}\n```");
        if (bundle.SmokeCommand is not null) text.AppendLine($"\n**Smoke test**\n```powershell\n{bundle.SmokeCommand}\n```");
        foreach (SampleFileInfo file in bundle.Files)
        {
            text.AppendLine($"\n### `{file.Path}` ({file.Kind})\n");
            if (file.Kind == SampleFileKind.Asset) text.AppendLine("Binary asset — bundle materialization preserves the original bytes.");
            else text.AppendLine(SampleSource(file.Path, file.Region, file.Language).Markdown);
        }
        return new DocMarkdown(text.ToString());
    }

    internal static string ExtractRegion(string source, string path, string region)
    {
        string begin = $"docs:begin {region}";
        string end = $"docs:end {region}";
        int beginAt = source.IndexOf(begin, StringComparison.Ordinal);
        int endAt = source.IndexOf(end, StringComparison.Ordinal);
        bool duplicateBegin = beginAt >= 0 && source.IndexOf(begin, beginAt + begin.Length, StringComparison.Ordinal) >= 0;
        bool duplicateEnd = endAt >= 0 && source.IndexOf(end, endAt + end.Length, StringComparison.Ordinal) >= 0;
        if (beginAt < 0 || endAt < 0 || endAt <= beginAt || duplicateBegin || duplicateEnd)
            throw new InvalidOperationException($"Sample source region '{region}' is invalid or duplicated in {path}.");
        int contentStart = source.IndexOf('\n', beginAt);
        int endLineStart = source.LastIndexOf('\n', endAt);
        if (contentStart < 0 || endLineStart <= contentStart)
            throw new InvalidOperationException($"Sample source region '{region}' is empty in {path}.");
        string content = source[(contentStart + 1)..endLineStart];
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException($"Sample source region '{region}' is empty in {path}.");
        return content;
    }

    /// <summary>Rendering Learnページの統一metadata。テストが同じ表示を機械検証する。</summary>
    internal static DocMarkdown RenderingMeta(string difficulty, string environment, string backend, string prerequisites,
        string? previous = null, string? next = null)
    {
        string navigation = previous is null && next is null ? "" : "\n\n"
            + (previous is null ? "" : $"**前へ:** [{previous.Split('/')[^1]}](story:{previous})")
            + (previous is not null && next is not null ? "　 " : "")
            + (next is null ? "" : $"**次:** [{next.Split('/')[^1]}](story:{next})");
        return new DocMarkdown($"**難易度:** {difficulty}　 **実行環境:** {environment}　 **Backend:** {backend}　 **前提知識:** {prerequisites}{navigation}");
    }

    /// <summary>新スタック (テキストスタック / ADR-0012) の docs ページ。<see cref="MarkdownDoc.FromDoc"/> で
    /// 既存の <c>Docs($"...")</c> 記法をそのまま描き、日本語フォント・シンタックスハイライト・mermaid/数式
    /// フェンス・領域いっぱい (fill) を配線し、クリック → <c>story:</c>/外部/<c>#アンカー</c> ナビを繋ぐ。
    /// docs ページの `Docs(ctx, ...)` を `DocNew(ctx, ...)` に替えるだけで移行できる (WS-A / S(A3))。</summary>
    internal static Widget DocNew(StoryContext ctx, DocString content, bool toc = false)
    {
        (VectorFont? bold, _, _, VectorFont? mono) = StoryKit.EditorFaces.Value;
        var fences = new Dictionary<string, Func<string, Widget>>
        {
            ["mermaid"] = body => Luxel.Diagram.Factories.DiagramBlock(body, 640f),
            ["math"] = body => Luxel.MathText.Factories.MathBlockView(body, maxWidth: 640f),
        };
        TextEditorView ed = MarkdownDoc.FromDoc(content, () => UiTheme.T, width: 640f, height: 480f,
            bold: bold, mono: mono, highlighter: Luxel.Highlight.TextMateHighlighter.Instance,
            fences: fences, fonts: StoryKit.JpFallback.Value, fill: true, toc: toc);

        // ナビ: クリック位置のソースオフセット → その位置のリンク → 解決 (Links/DocSource は最終 markdown 基準)
        string src = ed.DocSource!;
        IReadOnlyList<MarkdownLink> links = MarkdownDecorations.Links(src);
        ed.OnClickOffset = off =>
        {
            foreach (MarkdownLink l in links)
                if (off >= l.From && off < l.To) { NavigateDoc(ctx, ed, src, l.Url); return; }
        };
        return ed;
    }

    /// <summary>docs のリンク解決: <c>story:</c> は Navigate、<c>#アンカー</c> は見出しへスクロール、
    /// http(s) は既定ブラウザ、その他は Log。</summary>
    private static void NavigateDoc(StoryContext ctx, TextEditorView ed, string src, string url)
    {
        if (url.StartsWith("story:")) { ctx.Navigate(url["story:".Length..]); return; }
        if (url.StartsWith("#"))
        {
            string slug = url[1..];
            foreach (MarkdownHeading h in MarkdownDecorations.Headings(src))
                if (MarkdownDoc.Slug(h.Text) == slug) { ed.ScrollToSource(h.Offset); return; }
            return;
        }
        if (url.StartsWith("http://") || url.StartsWith("https://"))
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); ctx.Log($"open: {url}"); }
            catch (Exception e) { ctx.Log($"link 失敗: {url} ({e.Message})"); }
            return;
        }
        ctx.Log($"link: {url}");
    }
}
