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
        StoryResult result = story.Toc
            ? authored.WithMarkdown(MarkdownDoc.InsertToc(authored.Markdown))
            : authored;
        string markdown = StoryFence().Replace(result.Markdown, match => StoryEmbed(result, match));
        markdown = WidgetFence().Replace(markdown,
            "<aside class=\"markdown-embed-unavailable\">対話型Widgetを埋め込めません。ナビゲーションから参照先のStoryを開いてください。</aside>");
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
            return "<aside class=\"markdown-embed-unavailable\">埋め込みStoryの参照が正しくありません。</aside>";

        StoryReference reference = result.References[index];
        string path = WebUtility.HtmlEncode(reference.Path);
        string url = "?story=" + Uri.EscapeDataString(reference.Path) + "&amp;compact=1";
        string args = reference.Args.WithoutDefaults(Array.Empty<StoryArgDefinition>()).ToJson();
        if (args != "{}") url += "&amp;args=" + WebUtility.HtmlEncode(Uri.EscapeDataString(args));
        return $"""
            <section class="markdown-story-embed">
              <header><strong>{path}</strong><a href="?story={WebUtility.HtmlEncode(Uri.EscapeDataString(reference.Path))}">Storyを開く</a></header>
              <iframe src="{url}" title="{path}" loading="lazy"></iframe>
            </section>
            """;
    }

    [GeneratedRegex("```luxel-story\\s*\\r?\\n(\\d+)\\s*\\r?\\n```", RegexOptions.CultureInvariant)]
    private static partial Regex StoryFence();

    [GeneratedRegex("```luxel-ui\\s*\\r?\\n.*?\\r?\\n```", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex WidgetFence();

    [GeneratedRegex("href=\"story:([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex StoryLink();
}
