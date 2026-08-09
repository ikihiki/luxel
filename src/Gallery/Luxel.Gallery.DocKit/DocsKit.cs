using System.Reflection;
using System.Text;
using Luxel.Controls;
using Luxel.Gallery;

namespace Luxel.Gallery.DocKit;

/// <summary>Gallery documentation Markdown generated from embedded samples and story metadata.</summary>
public static class DocsKit
{
    /// <summary>Embeds another registered story in a documentation page.</summary>
    public static DocEmbed StoryRef(StoryContext context, string path, bool knobs = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        StoryInfo? story = StoryRegistry.Find(path);
        if (story is null)
            return new DocEmbed(
                Luxel.Controls.Kit.VStack(6)[
                    Luxel.Controls.Kit.Text(path, 12, color: Luxel.UI.Bind.From(() => Luxel.UI.UiTheme.T.TextMuted)),
                    Luxel.Controls.Kit.Alert($"ストーリーが見つかりません: {path}", Luxel.UI.Intent.Danger)],
                DocEmbedKind.StoryRef,
                path);

        Luxel.UI.Widget BuildNativeEmbed()
        {
            int before = context.Knobs.Count;
            bool suppressed = context.SuppressPlays;
            context.SuppressPlays = true;
            Luxel.UI.Widget body;
            try { body = story.Build(context); }
            finally { context.SuppressPlays = suppressed; }

            var parts = new List<Luxel.UI.Widget>
            {
                Luxel.Controls.Kit.Text(path, 12, color: Luxel.UI.Bind.From(() => Luxel.UI.UiTheme.T.TextMuted)),
                body,
            };
            if (knobs)
            {
                StoryKnob[] owned = context.Knobs.Skip(before).ToArray();
                parts.Add(Luxel.Controls.Kit.Divider());
                parts.Add(global::Luxel.Gallery.UI.Kit.KnobsTable(owned, width: 640,
                    onEdit: (_, knob, value) => context.QueueKnobEdit(knob, value)));
            }
            return Luxel.Controls.Kit.VStack(6)[parts.ToArray()];
        }

        return new DocEmbed(null, DocEmbedKind.StoryRef, path, WidgetFactory: BuildNativeEmbed);
    }

    /// <summary>Reads an embedded sample file and formats it as a Markdown code fence.</summary>
    public static DocMarkdown SampleSource(
        Assembly resourceAssembly,
        string relativePath,
        string? region = null,
        string? language = null,
        bool searchLoadedAssemblies = false)
    {
        string resource = "Luxel.Sample." + relativePath.Replace('\\', '.').Replace('/', '.');
        Stream? embedded = resourceAssembly.GetManifestResourceStream(resource);
        if (embedded is null && searchLoadedAssemblies)
        {
            embedded = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly != resourceAssembly)
                .Select(assembly => assembly.GetManifestResourceStream(resource))
                .FirstOrDefault(stream => stream is not null);
        }

        using Stream stream = embedded
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

    /// <summary>Formats a registered sample bundle and its embedded files as Markdown.</summary>
    public static DocMarkdown SampleBundle(
        Assembly resourceAssembly,
        string id,
        bool searchLoadedAssemblies = false)
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
            if (file.Kind == SampleFileKind.Asset)
                text.AppendLine("Binary asset — bundle materialization preserves the original bytes.");
            else
                text.AppendLine(SampleSource(resourceAssembly, file.Path, file.Region, file.Language, searchLoadedAssemblies).Markdown);
        }
        return new DocMarkdown(text.ToString());
    }

    /// <summary>Extracts a uniquely named docs region from sample source.</summary>
    public static string ExtractRegion(string source, string path, string region)
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

    /// <summary>Formats Learn-page metadata and previous/next navigation as Markdown.</summary>
    public static DocMarkdown RenderingMeta(
        string difficulty,
        string environment,
        string backend,
        string prerequisites,
        string? previous = null,
        string? next = null)
    {
        string navigation = previous is null && next is null ? "" : "\n\n"
            + (previous is null ? "" : $"**前へ:** [{previous.Split('/')[^1]}](story:{previous})")
            + (previous is not null && next is not null ? "　 " : "")
            + (next is null ? "" : $"**次:** [{next.Split('/')[^1]}](story:{next})");
        return new DocMarkdown($"**難易度:** {difficulty}　 **実行環境:** {environment}　 **Backend:** {backend}　 **前提知識:** {prerequisites}{navigation}");
    }
}
