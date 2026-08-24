using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery;

/// <summary>Builds the shared read-only source pane used by the native Gallery and static-site tests.</summary>
public static class GalleryStorySourcePane
{
    public static Widget Build(StoryInfo? story, float width = 800f, float height = 240f)
    {
        if (story is null)
            return Text(NativeRenderingLabels.NoStorySelected, 13,
                color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(8));
        if (string.IsNullOrWhiteSpace(story.Source))
            return Text(NativeRenderingLabels.SourceUnavailable, 13,
                color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(8));

        TextEditorView editor = TextEditorView(new Signal<string>(story.Source),
            editorHeight: MathF.Max(40, height), editorWidth: MathF.Max(80, width));
        editor.ReadOnly = true;
        editor.Fill = true;
        editor.ShowLineNumbers = true;
        editor.EditorFont = RenderingStoryKit.EditorFaces.Value.Mono;
        editor.Providers.Add(new SyntaxHighlightProvider(
            Luxel.Highlight.TextMateHighlighter.Instance, "csharp", () => UiTheme.T));
        return editor;
    }
}
