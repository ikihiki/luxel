using System.Runtime.CompilerServices;
using Luxel.UI;

namespace Luxel.Gallery;

internal static class SampleBundles
{
    [ModuleInitializer]
    internal static void Register()
    {
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "input.actions", "Deterministic input actions",
            "FakeInputSource drives InputBus, InputStack, ButtonAction and Axis2DAction without a window.", "Beginner",
            SampleCopyLevel.StandaloneProject,
            [new("samples/LuxelInput/LuxelInput.csproj", SampleFileKind.Project), new("samples/LuxelInput/Program.cs", SampleFileKind.CSharp)],
            Requirements: [".NET 10", "Headless on all supported .NET OS"], ExportSymbol: "InputStack",
            RunCommand: "dotnet run --project samples/LuxelInput", SmokeCommand: "dotnet run --project samples/LuxelInput"));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "audio.tone", "Headless audio tone",
            "Procedural PCM16, AudioClip, AudioMixer and NullAudioBackend with observable voice state.", "Beginner",
            SampleCopyLevel.StandaloneProject,
            [new("samples/LuxelAudio/LuxelAudio.csproj", SampleFileKind.Project), new("samples/LuxelAudio/Program.cs", SampleFileKind.CSharp)],
            Requirements: [".NET 10", "Headless: any supported OS", "Audible output: Windows/XAudio2 integration"], ExportSymbol: "AudioMixer",
            RunCommand: "dotnet run --project samples/LuxelAudio", SmokeCommand: "dotnet run --project samples/LuxelAudio"));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "resources.pipeline", "Resource pipeline",
            "Memory VFS and a typed byte[] to TextAsset step demonstrate loading, caching and ownership without GPU or disk.", "Beginner",
            SampleCopyLevel.StandaloneProject,
            [new("samples/LuxelResources/LuxelResources.csproj", SampleFileKind.Project), new("samples/LuxelResources/Program.cs", SampleFileKind.CSharp)],
            Requirements: [".NET 10", "Headless on all supported .NET OS"], ExportSymbol: "ResourceSystem",
            RunCommand: "dotnet run --project samples/LuxelResources", SmokeCommand: "dotnet run --project samples/LuxelResources"));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "rendering.app-host", "Standalone app host",
            "Window, GPU device, surface, resize, frame loop and deterministic shutdown.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelTriangle/LuxelTriangle.csproj", SampleFileKind.Project),
             new("samples/LuxelTriangle/Program.cs", SampleFileKind.CSharp, "standalone-frame-loop")],
            Requirements: [".NET 10", "Vulkan 1.3 or DirectX 12"], ExportSymbol: "Program",
            RunCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage triangle",
            SmokeCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage triangle --frames 1"));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "rendering.triangle", "Triangle renderer",
            "A compiled C#/Slang triangle block that plugs into the standalone app host.", "Beginner",
            SampleCopyLevel.Recipe,
            [new("samples/LuxelTriangle/TutorialAbi.cs", SampleFileKind.CSharp, "triangle-abi"),
             new("samples/LuxelTriangle/TriangleRenderer.cs", SampleFileKind.CSharp),
             new("shaders/tutorial_triangle.slang", SampleFileKind.Shader)],
            Dependencies: ["rendering.app-host"], Requirements: ["Luxel.Graphics", "Luxel.Platform"],
            ExportSymbol: "TriangleRenderer", RunCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage triangle",
            SmokeCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage triangle --frames 1"));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "rendering.2d", "Standalone 2D canvas",
            "A backend-neutral Scene2D rendered by the Skia CPU rasterizer with deterministic output.", "Beginner",
            SampleCopyLevel.StandaloneProject,
            [new("samples/LuxelTwoD/LuxelTwoD.csproj", SampleFileKind.Project),
             new("samples/LuxelTwoD/Program.cs", SampleFileKind.CSharp)],
            Requirements: [".NET 10"], ExportSymbol: "Scene2D",
            RunCommand: "dotnet run --project samples/LuxelTwoD",
            SmokeCommand: "dotnet run --project samples/LuxelTwoD"));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "rendering.3d", "Textured and lit 3D renderer",
            "Texture, camera, depth, lighting, render graph and compute post-process stages.", "Intermediate",
            SampleCopyLevel.StandaloneProject,
            [new("samples/LuxelTriangle/LuxelTriangle.csproj", SampleFileKind.Project),
             new("samples/LuxelTriangle/Program.cs", SampleFileKind.CSharp),
             new("samples/LuxelTriangle/TutorialAbi.cs", SampleFileKind.CSharp),
             new("samples/LuxelTriangle/TriangleRenderer.cs", SampleFileKind.CSharp),
             new("shaders/tutorial_triangle.slang", SampleFileKind.Shader),
             new("shaders/tutorial_3d.slang", SampleFileKind.Shader),
             new("shaders/compute_tutorial_postprocess.slang", SampleFileKind.Shader)],
            Requirements: [".NET 10", "Vulkan 1.3 or DirectX 12"], ExportSymbol: "TriangleRenderer",
            RunCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage post",
            SmokeCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage post --frames 1"));
    }
}
