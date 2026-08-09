using System.Runtime.CompilerServices;

namespace Luxel.Resources.Gallery.Stories;

internal static class ResourceSampleBundles
{
    [ModuleInitializer]
    internal static void Register()
    {
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "resources.scenarios", "Resource scenarios",
            "Eight focused, headless scenarios cover custom sources, typed pipelines, selection, DAG sharing, scopes, reload recovery, and HTTP composition.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelResources/LuxelResources.csproj", SampleFileKind.Project),
             new("samples/LuxelResources/Program.cs", SampleFileKind.CSharp)],
            Dependencies: ["support.source-tree"],
            Requirements: [".NET 10", "Headless on all supported .NET OS"],
            ExportSymbol: "ResourceSystem",
            RunCommand: "dotnet run --project samples/LuxelResources",
            SmokeCommand: "dotnet run --project samples/LuxelResources",
            Platforms: ["Windows", "Linux", "macOS"],
            ExpectedStdoutMarker: "resources: status=Ready, value=HELLO RESOURCES, scenarios=8"));
    }
}
