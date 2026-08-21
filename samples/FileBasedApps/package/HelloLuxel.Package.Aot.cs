#:package Luxel.Framework.UI@0.1.0
#:property TargetFramework=net10.0
#:property PublishAot=true
#:property IlcTreatWarningsAsErrors=false

using Luxel.Framework.UI;
using static Luxel.Controls.Kit;

var frames = Environment.GetEnvironmentVariable("LUXEL_RUN_FRAMES") is { } value && int.TryParse(value, out var parsed)
    ? parsed
    : (int?)null;

LuxelApp.Run(
    () => VStack(12)[
        Heading("Hello Luxel package"),
        Button(onClick: _ => Console.WriteLine("clicked"), text: "Click me")
    ],
    new LuxelAppOptions { Title = "Luxel package fixture", Width = 640, Height = 360, RunFrames = frames });
