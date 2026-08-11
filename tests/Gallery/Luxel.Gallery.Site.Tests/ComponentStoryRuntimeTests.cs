using Luxel.Controls;
using Luxel.Gallery;
using Luxel.Typography;
using Luxel.UI;
using Luxel.UI.Gallery;

namespace Luxel.Gallery.Site.Tests;

public sealed class ComponentStoryRuntimeTests
{
    [Fact]
    public void PreviewRebuildsWhenTrackedArgSignalChanges()
    {
        var label = new Signal<string>("first");
        int builds = 0;
        var preview = new ComponentStoryPreview(() =>
        {
            builds++;
            return Kit.Text(label.Value);
        });
        var layout = new LayoutContext { Font = VectorFont.LoadSystem() };

        preview.Layout(Constraints.LooseW(240, 120), layout);
        Widget first = Assert.Single(preview.DebugChildren());
        Assert.Equal(1, builds);

        label.Value = "second";
        Assert.Null(preview.Root);
        preview.Layout(Constraints.LooseW(240, 120), layout);

        Assert.Equal(2, builds);
        Assert.NotSame(first, Assert.Single(preview.DebugChildren()));
    }

    [Fact]
    public void ButtonPlaygroundDeclaresTypedArgsAndAppliesChanges()
    {
        StoryInfo story = Assert.IsType<StoryInfo>(UiGalleryProject.CreateCatalog().Find("Controls/Button/Playground"));
        var context = new StoryContext();
        var preview = Assert.IsType<ComponentStoryPreview>(story.Build(context));
        Assert.Collection(context.ArgDefinitions,
            arg => { Assert.Equal("text", arg.Name); Assert.Equal("string", arg.Type); },
            arg => { Assert.Equal("variant", arg.Name); Assert.StartsWith("enum:", arg.Type); },
            arg => { Assert.Equal("disabled", arg.Name); Assert.Equal("bool", arg.Type); });

        var layout = new LayoutContext { Font = VectorFont.LoadSystem() };
        preview.Layout(Constraints.LooseW(480, 160), layout);
        context.Knobs.Single(knob => knob.Name == "text").SetText("Saved");
        context.Knobs.Single(knob => knob.Name == "variant").SetText("Outline");
        context.Knobs.Single(knob => knob.Name == "disabled").SetText("true");
        Assert.Null(preview.Root);

        preview.Layout(Constraints.LooseW(480, 160), layout);
        Button button = Descendants(preview).OfType<Button>().Single();
        Assert.Equal("Saved", button.Text.Get());
        Assert.Equal(Variant.Outline, button.Variant.Get());
        Assert.False(button.Enabled);
    }

    private static IEnumerable<Widget> Descendants(Widget widget)
    {
        foreach (Widget child in widget.DebugChildren())
        {
            yield return child;
            foreach (Widget descendant in Descendants(child)) yield return descendant;
        }
    }
}
