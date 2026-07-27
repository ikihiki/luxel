#:project ../../src/Luxel.UI.App/Luxel.UI.App.csproj
#:property TargetFramework=net10.0
#:property PublishAot=true

using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.App;
using static Luxel.Controls.Kit;

int? frames = ParseFrames(args)
    ?? (int.TryParse(Environment.GetEnvironmentVariable("LUXEL_RUN_FRAMES"), out int value) ? value : null);

LuxelAppBuilder builder = LuxelApp.CreateBuilder(args);
builder.Options.Title = "Hello Luxel AOT";
builder.Options.Width = 640;
builder.Options.Height = 400;
builder.Options.Theme = Theme.Dark;
builder.Options.RunFrames = frames;
builder.Options.Diagnostic = Console.WriteLine;

LuxelUiApplication app = builder.Build();
app.MapScreen("/", navigation => Center()[
    Card(VStack(12)[
        Heading("Hello, Luxel AOT!"),
        Text("Minimal-API-style UI routing is Native AOT compatible."),
        Button(_ => navigation.Navigate("/settings"), "Open settings")
    ])
]);
app.MapScreen("/settings", navigation => Center()[
    Card(VStack(12)[
        Heading("Settings"),
        Button(_ => navigation.Back(), "Back")
    ])
]);

app.Run("/", (navigation, content) =>
    NavigationView(
        navigation,
        [
            new("/", "Home"),
            new("/settings", "Settings"),
        ])[content]);

static int? ParseFrames(string[] arguments)
{
    for (int i = 0; i < arguments.Length; i++)
    {
        if (arguments[i] == "--frames" && i + 1 < arguments.Length && int.TryParse(arguments[i + 1], out int frames))
            return frames;
        if (arguments[i].StartsWith("--frames=", StringComparison.Ordinal) &&
            int.TryParse(arguments[i]["--frames=".Length..], out frames))
            return frames;
    }
    return null;
}
