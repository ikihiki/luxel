using Luxel.Controls;
using Luxel.Gallery;
using Luxel.Typography;
using Luxel.UI;

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

}
