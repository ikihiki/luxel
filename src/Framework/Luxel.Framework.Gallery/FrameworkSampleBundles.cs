using System.Runtime.CompilerServices;

namespace Luxel.Framework.Gallery;

internal static class FrameworkSampleBundles
{
    [ModuleInitializer]
    internal static void Register()
        => SampleBundleRegistry.Register(new SampleBundleInfo(
            "framework.fixed-timestep", "Deterministic framework timing",
            "A headless FixedTimestep consumer that demonstrates bounded fixed updates, dropped-step diagnostics and interpolation alpha.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelFramework/LuxelFramework.csproj", SampleFileKind.Project),
             new("samples/LuxelFramework/Program.cs", SampleFileKind.CSharp)],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "Headless on all supported .NET OS"], ExportSymbol: "FixedTimestep",
            RunCommand: "dotnet run --project samples/LuxelFramework", SmokeCommand: "dotnet run --project samples/LuxelFramework",
            Platforms: ["Windows", "Linux", "macOS"], ExpectedStdoutMarker: "framework: updates=5, total=5, dropped=2, alpha=0.00"));
}
