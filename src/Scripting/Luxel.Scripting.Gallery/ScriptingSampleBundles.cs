using System.Runtime.CompilerServices;

namespace Luxel.Scripting.Gallery;

internal static class ScriptingSampleBundles
{
    [ModuleInitializer]
    internal static void Register()
        => SampleBundleRegistry.Register(new SampleBundleInfo(
            "scripting.gallery", "Browser Roslyn Gallery scripting",
            "Browser-safe Roslyn Web compilation, diagnostics, multi-file execution, and successful-only preview swap.", "Intermediate",
            SampleCopyLevel.Block,
            [
                new("src/Scripting/Luxel.Scripting.Gallery/BrowserRoslynGalleryRuntime.cs", SampleFileKind.CSharp),
                new("src/Scripting/Luxel.Scripting.Gallery/Stories/ScriptingStory.cs", SampleFileKind.CSharp),
            ],
            Requirements: ["Luxel.Scripting.Roslyn.Web", "browser metadata reference manifest"], ExportSymbol: "BrowserRoslynGalleryRuntime",
            RunCommand: "dotnet publish gallery/GalleryBrowser/GalleryBrowser.csproj -c Release",
            SmokeCommand: "dotnet test tests/Scripting/Luxel.Scripting.Roslyn.Web.Tests/Luxel.Scripting.Roslyn.Web.Tests.csproj -c Release",
            Platforms: ["Browser", "Windows", "Linux", "macOS"], TimeoutSeconds: 180));
}
