using System.Runtime.CompilerServices;

namespace Luxel.UI.Gallery;

internal static class UiSampleBundles
{
    [ModuleInitializer]
    internal static void Register()
    {
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "ui.headless-tree", "Headless reactive widget tree",
            "CompositeControl tracks a Signal read during Build, invalidates its root, and rebuilds a declarative StackPanel on the next layout.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelUiHeadless/LuxelUiHeadless.csproj", SampleFileKind.Project),
             new("samples/LuxelUiHeadless/Program.cs", SampleFileKind.CSharp),
             new("assets/fonts/BIZUDGothic-Regular.ttf", SampleFileKind.Asset)],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "Bundled font", "Headless on all supported .NET OS"], ExportSymbol: "CompositeControl",
            RunCommand: "dotnet run --project samples/LuxelUiHeadless", SmokeCommand: "dotnet run --project samples/LuxelUiHeadless",
            Platforms: ["Windows", "Linux", "macOS"], ExpectedStdoutMarker: "ui: builds=2, children=1->3, invalidated=True"));
    }
}
