using System.Runtime.CompilerServices;

namespace Luxel.Scripting.Gallery;

internal static class ScriptingSampleBundles
{
    [ModuleInitializer]
    internal static void Register()
        => SampleBundleRegistry.Register(new SampleBundleInfo(
            "scripting.gallery", "Gallery script hot reload",
            "Gallery-hosted ScriptHost recipe covering compilation, diagnostics, cancellation and successful-swap reload.", "Intermediate",
            SampleCopyLevel.Block,
            [new("src/Scripting/Luxel.Scripting.Gallery/Stories/ScriptingStory.cs", SampleFileKind.CSharp)],
            Requirements: ["Luxel.Scripting", "Gallery service provider"], ExportSymbol: "ScriptHost",
            RunCommand: "dotnet run --project gallery/GalleryNative -- vk --story Examples/Scripting/HotReload",
            SmokeCommand: "dotnet test tests/Luxel.Tests/Luxel.Tests.csproj --filter Scripting",
            Platforms: ["Windows", "Linux", "macOS"], TimeoutSeconds: 180));
}
