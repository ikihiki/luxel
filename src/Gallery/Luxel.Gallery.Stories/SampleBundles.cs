using System.Runtime.CompilerServices;
using Luxel.UI;

namespace Luxel.Gallery;

internal static class SampleBundles
{
    [ModuleInitializer]
    internal static void Register()
    {
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "rendering.webgpu-headless", "Headless WebGPU compute and triangle",
            "Public GpuDevice API recipe covering inline WGSL compute, offscreen rendering and HostCached readback validation.", "Beginner",
            SampleCopyLevel.StandaloneProject,
            [new("samples/LuxelWebGpuHeadless/LuxelWebGpuHeadless.csproj", SampleFileKind.Project),
             new("samples/LuxelWebGpuHeadless/Program.cs", SampleFileKind.CSharp),
             new("samples/LuxelWebGpuHeadless/HeadlessWebGpuSample.cs", SampleFileKind.CSharp)],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "wgpu-native 2.23.0 runtime", "Vulkan adapter; Mesa lavapipe supported on Linux"],
            ExportSymbol: "HeadlessWebGpuSample", RunCommand: "dotnet run --project samples/LuxelWebGpuHeadless -c Release",
            SmokeCommand: "dotnet run --project samples/LuxelWebGpuHeadless -c Release", Platforms: ["Windows", "Linux"],
            ExpectedStdoutMarker: "status=pass"));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "rendering.webgpu-browser", "Browser-WASM WebGPU canvas",
            "Browser-WASM recipe covering async WebGPU initialization, fixed-ABI embedded WGSL compute, textured offscreen rendering, canvas presentation and DOM input/resize events.", "Intermediate",
            SampleCopyLevel.StandaloneProject,
            [new("gallery/GalleryBrowser/GalleryBrowser.csproj", SampleFileKind.Project),
             new("gallery/GalleryBrowser/Program.cs", SampleFileKind.CSharp),
             new("gallery/GalleryBrowser/README.md", SampleFileKind.Asset),
             new("gallery/GalleryBrowser/wwwroot/index.html", SampleFileKind.Asset),
             new("gallery/GalleryBrowser/wwwroot/main.js", SampleFileKind.Asset),
             new("gallery/GalleryBrowser/Shaders/compute.wgsl", SampleFileKind.Shader),
             new("samples/CanonicalTriangleRecipe.cs", SampleFileKind.CSharp),
             new("shaders/compiled/tutorial_triangle.wgsl", SampleFileKind.Shader)],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "wasm-tools workload", "WebGPU browser", "HTTPS or localhost"],
            ExportSymbol: "Luxel.Gallery.Browser.BrowserGalleryApplication", RunCommand: "dotnet publish gallery/GalleryBrowser/GalleryBrowser.csproj -c Release",
            SmokeCommand: "dotnet publish gallery/GalleryBrowser/GalleryBrowser.csproj -c Release", Platforms: ["Browser/WASM"],
            ExpectedStdoutMarker: "data-status=pass", TimeoutSeconds: 240));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "rendering.app-host", "Standalone app host",
            "Window, GPU device, surface, resize, frame loop and deterministic shutdown.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelTriangle/LuxelTriangle.csproj", SampleFileKind.Project),
             new("samples/LuxelTriangle/Program.cs", SampleFileKind.CSharp, "standalone-frame-loop")],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "Vulkan 1.3 or DirectX 12"], ExportSymbol: "Program",
            RunCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage triangle",
            SmokeCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage triangle --frames 1"));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "rendering.2d", "Standalone 2D canvas",
            "A backend-neutral Scene2D rendered by the Skia CPU rasterizer with deterministic output.", "Beginner",
            SampleCopyLevel.StandaloneProject,
            [new("samples/LuxelTwoD/LuxelTwoD.csproj", SampleFileKind.Project),
             new("samples/LuxelTwoD/Program.cs", SampleFileKind.CSharp)],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10"], ExportSymbol: "Scene2D",
            RunCommand: "dotnet run --project samples/LuxelTwoD",
            SmokeCommand: "dotnet run --project samples/LuxelTwoD", Platforms: ["Windows", "Linux", "macOS"],
            ExpectedStdoutMarker: "sha256=ace142b4a50f6e6d1dafa7e72efdd3387305902ad5c5d80eaa0fad907d8aea44"));
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
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "Vulkan 1.3 or DirectX 12"], ExportSymbol: "TriangleRenderer",
            RunCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage post",
            SmokeCommand: "dotnet run --project samples/LuxelTriangle -- vk --stage post --frames 1"));
    }
}
