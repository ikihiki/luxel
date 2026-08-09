using System.Runtime.CompilerServices;

namespace Luxel.Graphics.Gallery;

internal static class GraphicsSampleBundles
{
    [ModuleInitializer]
    internal static void Register()
        => SampleBundleRegistry.Register(new SampleBundleInfo(
            "rendering.triangle", "Triangle renderer",
            "A complete triangle recipe: standalone app host plus the compiled C#/Slang renderer.", "Beginner",
            SampleCopyLevel.Recipe,
            [new("samples/CanonicalTriangleRecipe.cs", SampleFileKind.CSharp),
             new("samples/LuxelTriangle/TutorialAbi.cs", SampleFileKind.CSharp, "triangle-abi"),
             new("samples/LuxelTriangle/TriangleRenderer.cs", SampleFileKind.CSharp),
             new("shaders/tutorial_triangle.slang", SampleFileKind.Shader)],
            Dependencies: ["rendering.app-host"], Requirements: ["Luxel.Graphics", "Luxel.Platform"],
            ExportSymbol: "TriangleRenderer", RunCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage triangle",
            SmokeCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage triangle --frames 1",
            Platforms: ["Windows", "Linux"], TimeoutSeconds: 180,
            ExpectedStdoutMarker: "tutorial-3d: 1 frame(s), stage=Triangle"));
}
