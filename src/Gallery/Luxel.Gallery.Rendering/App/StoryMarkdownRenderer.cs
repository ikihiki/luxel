using System.Diagnostics;
using Luxel.Controls;
using Luxel.Gallery.Stories;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery;

/// <summary>Story metadataを反映してdirect Markdown Storyをnative表示する共通renderer。</summary>
public static class StoryMarkdownRenderer
{
    public static string EffectiveMarkdown(StoryInfo story, string markdown)
        => story.Toc ? MarkdownDoc.InsertToc(markdown) : markdown;

    public static Widget Build(StoryInfo story, StoryContext context, StoryResult authored)
    {
        if (authored.Kind == StoryResultKind.Widget && authored.Widget is not null) return authored.Widget;
        StoryResult result = authored.WithMarkdown(EffectiveMarkdown(story, authored.Markdown));

        (VectorFont? bold, _, _, VectorFont? mono) = RenderingStoryKit.EditorFaces.Value;
        var fences = new Dictionary<string, Func<string, Widget>>
        {
            ["mermaid"] = body => Luxel.Diagram.Factories.DiagramBlock(body, 640f),
            ["math"] = body => Luxel.MathText.Factories.MathBlockView(body, maxWidth: 640f),
        };
        TextEditorView editor = StoryMarkdownDocumentAdapter.FromStoryResult(result, () => UiTheme.T, width: 640f, height: 480f,
            reference => BuildReference(context, reference), bold: bold, mono: mono,
            highlighter: Luxel.Highlight.TextMateHighlighter.Instance, fences: fences,
            fonts: RenderingStoryKit.JpFallback.Value, fill: true);
        string source = editor.DocSource!;
        IReadOnlyList<MarkdownLink> links = MarkdownDecorations.Links(source);
        editor.OnClickOffset = offset =>
        {
            foreach (MarkdownLink link in links)
                if (offset >= link.From && offset < link.To)
                {
                    Navigate(context, editor, source, link.Url);
                    return;
                }
        };
        return editor;
    }

    private static Widget BuildReference(StoryContext context, StoryReference reference)
    {
        StoryInfo? story = StoryRegistry.Find(reference.Path);
        if (story is null) return Alert($"ストーリーが見つかりません: {reference.Path}", Intent.Danger);
        bool suppressed = context.SuppressPlays;
        context.SuppressPlays = true;
        try
        {
            StoryResult result = story.BuildResult(context);
            return result.Kind == StoryResultKind.Widget && result.Widget is not null
                ? result.Widget
                : Build(story, context, result);
        }
        finally { context.SuppressPlays = suppressed; }
    }

    private static void Navigate(StoryContext context, TextEditorView editor, string source, string url)
    {
        if (url.StartsWith("story:", StringComparison.Ordinal))
        {
            context.Navigate(url["story:".Length..]);
            return;
        }
        if (url.StartsWith('#'))
        {
            string slug = url[1..];
            foreach (MarkdownHeading heading in MarkdownDecorations.Headings(source))
                if (MarkdownDoc.Slug(heading.Text) == slug)
                {
                    editor.ScrollToSource(heading.Offset);
                    return;
                }
            return;
        }
        if (url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                context.Log($"open: {url}");
            }
            catch (Exception error) { context.Log($"link 失敗: {url} ({error.Message})"); }
            return;
        }
        context.Log($"link: {url}");
    }
}
