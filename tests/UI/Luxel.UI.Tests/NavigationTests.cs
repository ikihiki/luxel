using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

public sealed class NavigationTests
{
    private static LayoutContext Ctx() => new() { Font = VectorFont.LoadSystem() };

    [Fact]
    public void Paths_AreNormalizedAndCaseSensitive()
    {
        Assert.Equal("/", NavigationPath.Normalize("/"));
        Assert.Equal("/settings", NavigationPath.Normalize("/settings///"));
        Assert.Throws<ArgumentException>(() => NavigationPath.Normalize("settings"));

        var navigation = new Navigation("/", path => path is "/" or "/Settings");
        navigation.Navigate("/Settings/");
        Assert.Equal("/Settings", navigation.CurrentPath);
        Assert.Throws<InvalidOperationException>(() => navigation.Navigate("/settings"));
    }

    [Fact]
    public void NavigateReplaceAndBack_ManageHistory()
    {
        var navigation = new Navigation("/", path => path is "/" or "/a" or "/b");
        Assert.False(navigation.CanGoBack);
        Assert.False(navigation.Back());

        navigation.Navigate("/a");
        Assert.True(navigation.CanGoBack);
        navigation.Navigate("/a/");
        navigation.Replace("/b");
        Assert.Equal("/b", navigation.CurrentPath);

        Assert.True(navigation.Back());
        Assert.Equal("/", navigation.CurrentPath);
        Assert.False(navigation.CanGoBack);
    }

    [Fact]
    public void NavigationHost_RebuildsCurrentScreenAndRecreatesBackDestination()
    {
        var navigation = new Navigation("/", path => path is "/" or "/settings");
        int homeBuilds = 0;
        var host = new NavigationHost(navigation, (path, _) =>
            path == "/" ? Text($"home {++homeBuilds}") : Text("settings"));

        host.Layout(Constraints.LooseW(300, 200), Ctx());
        Widget first = host.Root!;
        Assert.Equal(1, homeBuilds);

        navigation.Navigate("/settings");
        Assert.Null(host.Root);
        host.Layout(Constraints.LooseW(300, 200), Ctx());
        Assert.NotSame(first, host.Root);

        navigation.Back();
        host.Layout(Constraints.LooseW(300, 200), Ctx());
        Assert.Equal(2, homeBuilds);
    }

    [Fact]
    public void NavigationView_IsSingleChildLayoutAndTracksSelection()
    {
        var navigation = new Navigation("/", path => path is "/" or "/settings");
        Widget first = Text("first");
        NavigationView view = NavigationView(navigation,
        [
            new NavigationViewItem("/", "Home"),
            new NavigationViewItem("/settings", "Settings"),
        ])[first];

        Assert.Equal("Home", view.SelectedItem?.Label);
        view.Layout(new Constraints(0, 640, 0, 400), Ctx());
        Assert.NotNull(view.Root);

        navigation.Navigate("/settings");
        Assert.Equal("Settings", view.SelectedItem?.Label);
        Assert.Null(view.Root);

        Widget replacement = Text("replacement");
        _ = view[replacement];
        view.Layout(new Constraints(0, 640, 0, 400), Ctx());
        Assert.Contains(replacement, view.Root!.DebugChildren().SelectMany(Flatten));
    }

    [Fact]
    public void NavigationView_AllowsNoChildAndDoesNotExposeContentAsFactoryParam()
    {
        var navigation = new Navigation("/");
        NavigationView view = NavigationView(navigation, [new NavigationViewItem("/", "Home")]);
        view.Layout(new Constraints(0, 640, 0, 400), Ctx());
        Assert.NotNull(view.Root);

        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(Kit).Module.ModuleHandle);
        ControlApi api = Assert.IsType<ControlApi>(ControlApiRegistry.Find("NavigationView"));
        Assert.Contains(api.Members, member => member.Name == "Navigation" && !member.Inherited);
        Assert.Contains(api.Members, member => member.Name == "Items" && !member.Inherited);
        Assert.DoesNotContain(api.Members, member => member.Name == "Content" && !member.Inherited);
    }

    private static IEnumerable<Widget> Flatten(Widget widget)
    {
        yield return widget;
        foreach (Widget child in widget.DebugChildren())
            foreach (Widget descendant in Flatten(child))
                yield return descendant;
    }
}
