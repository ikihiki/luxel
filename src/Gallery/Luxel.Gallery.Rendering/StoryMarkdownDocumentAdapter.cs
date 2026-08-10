using Luxel.Controls;
using Luxel.Document;
using Luxel.Gallery;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>Adapts Gallery semantic story results to the production Markdown renderer.</summary>
public static class StoryMarkdownDocumentAdapter
{
    public static TextEditorView FromStoryResult(StoryResult result, Func<Theme> theme, float width, float height,
        Func<StoryReference, Widget> storyResolver, VectorFont? body = null, VectorFont? bold = null,
        VectorFont? mono = null, ISyntaxHighlighter? highlighter = null,
        IReadOnlyDictionary<string, Func<string, Widget>>? fences = null, FontCollection? fonts = null,
        bool fill = false)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(storyResolver);
        var kinds = new HashSet<string> { "luxel-ui", "luxel-story" };
        if (fences is not null) foreach (string key in fences.Keys) kinds.Add(key);
        TextEditorView editor = MarkdownDoc.Create(new Signal<string>(result.Markdown), theme, width, height,
            body: body, bold: bold, mono: mono, highlighter: highlighter, embedKinds: kinds, fonts: fonts, fill: fill);
        editor.WidgetResolver = key =>
        {
            if (key is not EmbedRef embed) return null;
            if (embed.Key == "luxel-story")
                return int.TryParse(embed.Body.Trim(), out int storyIndex)
                    && storyIndex >= 0 && storyIndex < result.References.Count
                    ? storyResolver(result.References[storyIndex]) : null;
            if (embed.Key == "luxel-ui")
                return int.TryParse(embed.Body.Trim(), out int widgetIndex)
                    && widgetIndex >= 0 && widgetIndex < result.Embeds.Count
                    ? result.Embeds[widgetIndex].ResolveWidget() : null;
            return fences is not null && fences.TryGetValue(embed.Key, out Func<string, Widget>? factory)
                ? factory(embed.Body) : null;
        };
        return editor;
    }
}
