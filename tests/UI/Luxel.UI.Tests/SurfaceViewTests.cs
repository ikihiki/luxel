using Luxel.Controls;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public sealed class SurfaceViewTests
{
    [Fact]
    public void ChildTheme_DefaultsToParentTheme()
    {
        var parentTheme = new Signal<Theme>(Theme.Dark);
        SurfaceView surface = SurfaceView(320, 200);

        Assert.Same(parentTheme, surface.ResolveChildTheme(parentTheme));
    }

    [Fact]
    public void ChildTheme_CanBeIndependentFromParentTheme()
    {
        var parentTheme = new Signal<Theme>(Theme.Dark);
        var previewTheme = new Signal<Theme>(Theme.Light);
        SurfaceView surface = SurfaceView(320, 200);
        surface.ChildTheme = previewTheme;

        Assert.Same(previewTheme, surface.ResolveChildTheme(parentTheme));
    }
}
