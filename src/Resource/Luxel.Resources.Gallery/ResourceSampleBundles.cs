using System.Runtime.CompilerServices;

namespace Luxel.Resources.Gallery.Stories;

internal static class ResourceSampleBundles
{
    [ModuleInitializer]
    internal static void Register()
    {
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "resources.scenarios", "Resource scenarios",
            "Ten headless scenarios exercise builder readiness, custom domains, typed managers, shared identity, publication, retirement, recovery, and metrics.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelResources/LuxelResources.csproj", SampleFileKind.Project),
             new("samples/LuxelResources/Program.cs", SampleFileKind.CSharp)],
            Dependencies: ["support.source-tree"],
            Requirements: [".NET 10", "Headless on all supported .NET OS"],
            ExportSymbol: "ResourceSystem",
            RunCommand: "dotnet run --project samples/LuxelResources",
            SmokeCommand: "dotnet run --project samples/LuxelResources",
            Platforms: ["Windows", "Linux", "macOS"],
            ExpectedStdoutMarker: "resources: status=Ready, architecture=builder-domain-manager, scenarios=10"));
    }
}
