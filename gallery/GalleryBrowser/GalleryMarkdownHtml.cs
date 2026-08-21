using System.Net;
using System.Text.RegularExpressions;
using Luxel.Controls;
using Luxel.Gallery;
using Markdig;

namespace GalleryBrowser;

internal static partial class GalleryMarkdownHtml
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string Render(StoryInfo story, StoryResult authored)
    {
        StoryResult result = authored.WithMarkdown(MarkdownDoc.RenderTocPlaceholder(authored.Markdown));
        string markdown = StoryFence().Replace(result.Markdown, match => StoryEmbed(result, match));
        markdown = WidgetFence().Replace(markdown, match => WidgetEmbed(result, match));
        string html = Markdown.ToHtml(markdown, Pipeline);
        return StoryLink().Replace(html, match =>
        {
            string path = WebUtility.HtmlDecode(match.Groups[1].Value);
            return "href=\"?story=" + WebUtility.HtmlEncode(Uri.EscapeDataString(path)) + "\"";
        });
    }

    private static string StoryEmbed(StoryResult result, Match match)
    {
        if (!int.TryParse(match.Groups[1].Value, out int index)
            || index < 0 || index >= result.References.Count)
            return GalleryApiTableHtml.Unavailable("Story", null, "埋め込みストーリーの参照が正しくありません。");

        StoryReference reference = result.References[index];
        return StoryEmbed(reference.Path, reference.Args);
    }

    private static string WidgetEmbed(StoryResult result, Match match)
    {
        if (!int.TryParse(match.Groups[1].Value, out int index)
            || index < 0 || index >= result.Embeds.Count)
            return GalleryApiTableHtml.Unavailable("Widget", null, "Widget 埋め込みの参照が正しくありません。");

        StoryMarkdownEmbed embed = result.Embeds[index];
        if (GalleryApiTableHtml.TryRender(embed, out string html)) return html;
        return GalleryApiTableHtml.Unavailable(embed.Kind, embed.Reference,
            "この対話型 Widget はブラウザの文書表示へ直接埋め込めません。");
    }

    private static string StoryEmbed(string referencePath, StoryArgs argsValue)
    {
        string path = WebUtility.HtmlEncode(referencePath);
        string url = "?story=" + Uri.EscapeDataString(referencePath) + "&amp;compact=1";
        string args = argsValue.WithoutDefaults(Array.Empty<StoryArgDefinition>()).ToJson();
        if (args != "{}") url += "&amp;args=" + WebUtility.HtmlEncode(Uri.EscapeDataString(args));
        return $"""
            <section class="markdown-story-embed">
              <header><strong>{path}</strong><a href="?story={WebUtility.HtmlEncode(Uri.EscapeDataString(referencePath))}">ストーリーを開く</a></header>
              <iframe src="{url}" title="{path}" loading="lazy"></iframe>
            </section>
            """;
    }

    [GeneratedRegex("```luxel-story\\s*\\r?\\n(\\d+)\\s*\\r?\\n```", RegexOptions.CultureInvariant)]
    private static partial Regex StoryFence();

    [GeneratedRegex("```luxel-ui\\s*\\r?\\n(\\d+)\\s*\\r?\\n```", RegexOptions.CultureInvariant)]
    private static partial Regex WidgetFence();

    [GeneratedRegex("href=\"story:([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex StoryLink();
}
