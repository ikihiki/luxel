#:project ../../src/Framework/Luxel.Framework.UI/Luxel.Framework.UI.csproj
#:property TargetFramework=net10.0
#:property PublishAot=false

using Luxel.Controls;
using Luxel.UI;
using Luxel.Framework.UI;
using static Luxel.Controls.Kit;

int? frames = ParseFrames(args)
    ?? (int.TryParse(Environment.GetEnvironmentVariable("LUXEL_RUN_FRAMES"), out int value) ? value : null);

LuxelAppBuilder builder = LuxelApp.CreateBuilder(args);
builder.Options.Title = "Hello Luxel";
builder.Options.Width = 640;
builder.Options.Height = 400;
builder.Options.Theme = Theme.Dark;
builder.Options.RunFrames = frames;
builder.Options.Diagnostic = Console.WriteLine;

LuxelUiApplication app = builder.Build();
app.MapScreen("/", navigation => Center()[
    Card(VStack(12)[
        Heading("Hello, Luxel!"),
        Text("A .NET 10 file-based Linux UI app."),
        Button(_ => navigation.Navigate("/settings"), "Open settings")
    ])
]);
app.MapScreen("/settings", navigation => Center()[
    Card(VStack(12)[
        Heading("Settings"),
        Text("This screen was registered with MapScreen."),
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
