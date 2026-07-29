using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>Navigation state and the NavigationView shell.</summary>
public static class NavigationStories
{
    [Story("Examples/UI/Navigation", Width = 680, Height = 380)]
    public static Widget NavigationHistory(StoryContext ctx)
    {
        string[] paths = ["/", "/details", "/saved"];
        var navigation = new Navigation("/", paths.Contains);
        var host = new NavigationHost(navigation, (path, nav) => Screen(path, nav, ctx));
        Func<string> status = () =>
            $"CurrentPath = {navigation.CurrentPath}   CanGoBack = {navigation.CanGoBack}";

        return Frame(VStack(12, width: 580)[
            Heading("Navigation — history and replacement"),
            Muted("Navigate pushes history, Replace changes the current entry, and Back restores the previous path."),
            Text(status, 13),
            HStack(8)[
                Button(_ => navigation.Navigate("/details"), "Navigate /details"),
                Button(_ => navigation.Replace("/saved"), "Replace /saved"),
                Button(_ => navigation.Back(), "Back")],
            host]);
    }

    [Story("Controls/NavigationView/Basic", Width = 760, Height = 440)]
    public static Widget NavigationViewBasic(StoryContext ctx)
    {
        string[] paths = ["/", "/projects", "/settings", "/admin"];
        var navigation = new Navigation("/", paths.Contains);
        var host = new NavigationHost(navigation, (path, nav) => ShellScreen(path, nav, ctx));

        NavigationView view = NavigationView(
            navigation,
            [
                new("/", "Home"),
                new("/projects", "Projects"),
                new("/settings", "Settings"),
                new("/admin", "Admin", IsEnabled: false),
            ],
            width: 680,
            height: 340)[host];

        return Frame(view);
    }

    private static Widget Screen(string path, Navigation navigation, StoryContext ctx) =>
        Border(background: Bind.From(() => UiTheme.T.Surface), rounded: 8, padding: new Thickness(16),
            width: 540, height: 150, hAlign: Align.Stretch)[
            VStack(8)[
                Heading(path == "/" ? "Home" : path[1..], 2),
                Text((Func<string>)(() => $"Resolved from {navigation.CurrentPath}")),
                Button(_ =>
                {
                    navigation.Navigate(path == "/" ? "/details" : "/");
                    ctx.Log($"navigate: {navigation.CurrentPath}");
                }, path == "/" ? "Open details" : "Go home")]];

    private static Widget ShellScreen(string path, Navigation navigation, StoryContext ctx)
    {
        string title = path switch
        {
            "/" => "Home",
            "/projects" => "Projects",
            "/settings" => "Settings",
            _ => "Admin",
        };
        string description = path switch
        {
            "/" => "Choose a destination from the navigation pane.",
            "/projects" => "This screen was rebuilt by NavigationHost.",
            "/settings" => "Back returns to the previous history entry.",
            _ => "Disabled items cannot be selected from NavigationView.",
        };

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(28),
            hAlign: Align.Stretch, vAlign: Align.Stretch)[
            VStack(12)[
                Heading(title),
                Muted(description),
                Text((Func<string>)(() => $"path: {navigation.CurrentPath}"), 13),
                HStack(8)[
                    Button(_ =>
                    {
                        navigation.Navigate("/projects");
                        ctx.Log("navigate: /projects");
                    }, "Open projects"),
                    Button(_ =>
                    {
                        navigation.Back();
                        ctx.Log($"back: {navigation.CurrentPath}");
                    }, "Back")]]];
    }
}
