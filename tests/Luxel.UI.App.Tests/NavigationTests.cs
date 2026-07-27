using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.App;
using static Luxel.Controls.Kit;

namespace Luxel.UI.App.Tests;

public sealed class NavigationTests
{
    [Fact]
    public void Builder_IsSingleUseAndOptionsAreMutable()
    {
        LuxelAppBuilder builder = LuxelApp.CreateBuilder(["--test"]);
        builder.Options.Title = "Mapped UI";
        Assert.Equal("Mapped UI", builder.Options.Title);
        Assert.Equal("--test", Assert.Single(builder.Args));
        Assert.IsType<LuxelUiApplication>(builder.Build());
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void MapScreen_NormalizesAndRejectsDuplicates()
    {
        LuxelUiApplication app = LuxelApp.CreateBuilder().Build();
        app.MapScreen("/settings/", () => Text("settings"));
        Assert.Throws<InvalidOperationException>(() => app.MapScreen("/settings", () => Text("duplicate")));
        Assert.Throws<ArgumentException>(() => app.MapScreen("settings", () => Text("relative")));
    }

    [Fact]
    public void CreateRoot_ResolvesOnlyCurrentScreenAndSupportsNavigation()
    {
        LuxelUiApplication app = LuxelApp.CreateBuilder().Build();
        int homeBuilds = 0;
        int settingsBuilds = 0;
        app.MapScreen("/", navigation =>
        {
            homeBuilds++;
            return Text(navigation.CurrentPath);
        });
        app.MapScreen("/settings", navigation =>
        {
            settingsBuilds++;
            return Text(navigation.CurrentPath);
        });

        var host = Assert.IsType<NavigationHost>(app.CreateRoot("/"));
        Assert.Equal(0, homeBuilds);
        Assert.Equal(0, settingsBuilds);
        host.Navigation.Navigate("/settings");
        Assert.Equal("/settings", host.Navigation.CurrentPath);
        Assert.Throws<InvalidOperationException>(() => host.Navigation.Navigate("/missing"));
    }

    [Fact]
    public void ShellFactory_ReceivesPersistentNavigationAndContentHost()
    {
        LuxelUiApplication app = LuxelApp.CreateBuilder().Build();
        app.MapScreen("/", () => Text("home"));

        Navigation? receivedNavigation = null;
        Widget? receivedContent = null;
        Widget root = app.CreateRoot("/", (navigation, content) =>
        {
            receivedNavigation = navigation;
            receivedContent = content;
            return NavigationView(navigation,
                [new NavigationViewItem("/", "Home")])[content];
        });

        NavigationView view = Assert.IsType<NavigationView>(root);
        Assert.NotNull(receivedNavigation);
        NavigationHost host = Assert.IsType<NavigationHost>(receivedContent);
        Assert.Same(receivedNavigation, host.Navigation);
        Assert.Equal("Home", view.SelectedItem?.Label);
    }

    [Fact]
    public void CreateRoot_RejectsUnknownInitialPath()
    {
        LuxelUiApplication app = LuxelApp.CreateBuilder().Build();
        app.MapScreen("/", () => Text("home"));
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => app.CreateRoot("/missing"));
        Assert.Contains("/missing", error.Message, StringComparison.Ordinal);
    }
}
