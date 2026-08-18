using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.UI;

/// <summary>Builds stories through the shared Gallery presentation policy.</summary>
public static class StoryPresentation
{
    /// <summary>
    /// Builds a story and applies the standard theme background, padding, and centering to
    /// widget-valued Basic and Playground stories. Markdown Docs remain responsible for their
    /// own document layout; embedded story references pass through this same method.
    /// </summary>
    public static StoryResult Build(StoryInfo story, StoryContext context)
    {
        ArgumentNullException.ThrowIfNull(story);
        ArgumentNullException.ThrowIfNull(context);

        StoryResult result = story.Build(context);
        if (result.Kind != StoryResultKind.Widget || result.Widget is null
            || story.Kind is not (StoryKind.Basic or StoryKind.Playground))
            return result;

        Widget frame = Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Center()[result.Widget]];
        return frame;
    }
}
