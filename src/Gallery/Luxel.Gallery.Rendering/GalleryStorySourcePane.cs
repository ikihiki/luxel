using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery;

/// <summary>Builds the shared read-only source pane used by the native Gallery and static-site tests.</summary>
public static class GalleryStorySourcePane
{
    public static Widget Build(StoryInfo? story, float width = 640f, float height = 240f)
    {
        if (story is null)
            return Text("No story selected.", 12, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(8));
        SampleBundleInfo? bundle = SampleBundleRegistry.Find(story.SampleBundle);
        if (string.IsNullOrWhiteSpace(story.Source))
            return Text(bundle is null
                    ? "Source unavailable. Gallery harness required; no standalone sample bundle is registered."
                    : $"Run this sample ({bundle.CopyLevel}): {bundle.RunCommand}", 12,
                color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(8));

        TextEditorView editor = TextEditorView(new Signal<string>(story.Source),
            editorHeight: MathF.Max(40, height), editorWidth: MathF.Max(80, width));
        editor.ReadOnly = true;
        editor.Fill = true;
        editor.ShowLineNumbers = true;
        editor.EditorFont = RenderingStoryKit.EditorFaces.Value.Mono;
        editor.Providers.Add(new SyntaxHighlightProvider(
            Luxel.Highlight.TextMateHighlighter.Instance, "csharp", () => UiTheme.T));
        if (bundle is null) return editor;
        string files = string.Join(" · ", bundle.Files.Select(file => file.Path));
        return VStack(6)[
            Text($"Run this sample · {bundle.CopyLevel} · {bundle.RunCommand}", 12, color: Bind.From(() => UiTheme.T.TextMuted)),
            Text(files, 10, color: Bind.From(() => UiTheme.T.TextMuted)),
            editor];
    }
}
