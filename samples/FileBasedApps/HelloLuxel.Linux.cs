#:project ../../src/Luxel.UI.App/Luxel.UI.App.csproj
#:property TargetFramework=net10.0
#:property PublishAot=false

using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.App;
using static Luxel.Controls.Kit;

int? frames = ParseFrames(args)
    ?? (int.TryParse(Environment.GetEnvironmentVariable("LUXEL_RUN_FRAMES"), out int value) ? value : null);

LuxelApp.Run(
    () => Center()[
        Card(VStack(12)[
            Heading("Hello, Luxel!"),
            Text("A .NET 10 file-based Linux UI app."),
            Button(onClick: _ => Console.WriteLine("Hello from Luxel."), text: "It works")
        ])
    ],
    new LuxelAppOptions
    {
        Title = "Hello Luxel",
        Width = 640,
        Height = 400,
        Theme = Theme.Dark,
        RunFrames = frames,
        Diagnostic = Console.WriteLine,
    });

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
