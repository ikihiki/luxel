using System.Runtime.CompilerServices;
using Luxel.UI;

namespace Luxel.Gallery;

internal static class SampleBundles
{
    [ModuleInitializer]
    internal static void Register()
    {
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "support.source-tree", "Luxel source dependency closure",
            "Internal support bundle that preserves repository-relative ProjectReference and shader import paths for clean temp builds.", "Internal",
            SampleCopyLevel.GalleryOnly,
            [new("Directory.Build.props", SampleFileKind.Asset),
             new("src", SampleFileKind.Asset, Destination: "src", AssetGlob: "*", Mode: SampleFileMode.Glob),
             new("shaders", SampleFileKind.Asset, Destination: "shaders", AssetGlob: "*", Mode: SampleFileMode.Glob),
             new("eng/Luxel.ShaderWgslGen", SampleFileKind.Asset, Destination: "eng/Luxel.ShaderWgslGen", AssetGlob: "*", Mode: SampleFileMode.Glob)]));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "game.cavern", "Luxel Cavern capstone",
            "Repository recipe integrating Framework, UI, 2D, input, audio, resources, particles and settings.", "Advanced",
            SampleCopyLevel.Recipe,
            [new("samples/LuxelCavern/LuxelCavern/LuxelCavern.csproj", SampleFileKind.Project),
             new("samples/LuxelCavern/LuxelCavern/CavernRealtimeScene.cs", SampleFileKind.CSharp)],
            Requirements: ["Repository checkout", ".NET 10", "Windows real-window host"], ExportSymbol: "CavernRealtimeScene",
            RunCommand: "dotnet run --project samples/LuxelCavern/LuxelCavern",
            SmokeCommand: "dotnet build samples/LuxelCavern/LuxelCavern/LuxelCavern.csproj --configuration Release --no-restore",
            Platforms: ["Windows"], TimeoutSeconds: 180));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "game.range", "Luxel Range capstone",
            "Repository recipe integrating ECS, physics, glTF, GPU asset extraction and 3D particles.", "Advanced",
            SampleCopyLevel.Recipe,
            [new("samples/LuxelRange/LuxelRange/LuxelRange.csproj", SampleFileKind.Project),
             new("samples/LuxelRange/LuxelRange/RangeRealtimeScene.cs", SampleFileKind.CSharp)],
            Requirements: ["Repository checkout", ".NET 10", "Windows real-window host", "Khronos Fox asset"], ExportSymbol: "RangeRealtimeScene",
            RunCommand: "dotnet run --project samples/LuxelRange/LuxelRange",
            SmokeCommand: "dotnet build samples/LuxelRange/LuxelRange/LuxelRange.csproj --configuration Release --no-restore",
            Platforms: ["Windows"], TimeoutSeconds: 240));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "scripting.gallery", "Gallery script hot reload",
            "Gallery-hosted ScriptHost recipe covering compilation, diagnostics, cancellation and successful-swap reload.", "Intermediate",
            SampleCopyLevel.Block,
            [new("src/Luxel.Gallery.Stories/Stories/ScriptingStory.cs", SampleFileKind.CSharp)],
            Requirements: ["Luxel.Scripting", "Gallery service provider"], ExportSymbol: "ScriptHost",
            RunCommand: "dotnet run --project src/Luxel.Gallery.Host -- vk --story Examples/Scripting/HotReload",
            SmokeCommand: "dotnet test tests/Luxel.Tests/Luxel.Tests.csproj --filter Scripting",
            Platforms: ["Windows", "Linux", "macOS"], TimeoutSeconds: 180));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "framework.fixed-timestep", "Deterministic framework timing",
            "A headless FixedTimestep consumer that demonstrates bounded fixed updates, dropped-step diagnostics and interpolation alpha.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelFramework/LuxelFramework.csproj", SampleFileKind.Project),
             new("samples/LuxelFramework/Program.cs", SampleFileKind.CSharp)],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "Headless on all supported .NET OS"], ExportSymbol: "FixedTimestep",
            RunCommand: "dotnet run --project samples/LuxelFramework", SmokeCommand: "dotnet run --project samples/LuxelFramework",
            Platforms: ["Windows", "Linux", "macOS"], ExpectedStdoutMarker: "framework: updates=5, total=5, dropped=2, alpha=0.00"));
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
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "input.actions", "Deterministic input actions",
            "FakeInputSource drives InputBus, InputStack, ButtonAction and Axis2DAction without a window.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelInput/LuxelInput.csproj", SampleFileKind.Project), new("samples/LuxelInput/Program.cs", SampleFileKind.CSharp)],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "Headless on all supported .NET OS"], ExportSymbol: "InputStack",
            RunCommand: "dotnet run --project samples/LuxelInput", SmokeCommand: "dotnet run --project samples/LuxelInput",
            Platforms: ["Windows", "Linux", "macOS"], ExpectedStdoutMarker: "input: jump=True, triggered=1"));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "audio.tone", "Headless audio tone",
            "Procedural PCM16, AudioClip, AudioMixer and NullAudioBackend with observable voice state.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelAudio/LuxelAudio.csproj", SampleFileKind.Project), new("samples/LuxelAudio/Program.cs", SampleFileKind.CSharp)],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "Headless: any supported OS", "Audible output: Windows/XAudio2 integration"], ExportSymbol: "AudioMixer",
            RunCommand: "dotnet run --project samples/LuxelAudio", SmokeCommand: "dotnet run --project samples/LuxelAudio",
            Platforms: ["Windows", "Linux", "macOS"], ExpectedStdoutMarker: "audio: initialized=True, voices=1"));
        SampleBundleRegistry.Register(new SampleBundleInfo(
            "resources.pipeline", "Resource pipeline",
            "Memory VFS and a typed byte[] to TextAsset step demonstrate loading, caching and ownership without GPU or disk.", "Beginner",
            SampleCopyLevel.Block,
            [new("samples/LuxelResources/LuxelResources.csproj", SampleFileKind.Project), new("samples/LuxelResources/Program.cs", SampleFileKind.CSharp)],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "Headless on all supported .NET OS"], ExportSymbol: "ResourceSystem",
            RunCommand: "dotnet run --project samples/LuxelResources", SmokeCommand: "dotnet run --project samples/LuxelResources",
            Platforms: ["Windows", "Linux", "macOS"], ExpectedStdoutMarker: "resources: status=Ready, value=HELLO RESOURCES"));
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
            [new("samples/LuxelWebGpuBrowser/LuxelWebGpuBrowser.csproj", SampleFileKind.Project),
             new("samples/LuxelWebGpuBrowser/Program.cs", SampleFileKind.CSharp),
             new("samples/LuxelWebGpuBrowser/README.md", SampleFileKind.Asset),
             new("samples/LuxelWebGpuBrowser/wwwroot/index.html", SampleFileKind.Asset),
             new("samples/LuxelWebGpuBrowser/wwwroot/main.js", SampleFileKind.Asset),
             new("samples/LuxelWebGpuBrowser/wwwroot/browser-runtime-manifest.json", SampleFileKind.Asset),
             new("samples/LuxelWebGpuBrowser/Shaders/compute.wgsl", SampleFileKind.Shader),
             new("samples/CanonicalTriangleRecipe.cs", SampleFileKind.CSharp),
             new("shaders/compiled/tutorial_triangle.wgsl", SampleFileKind.Shader)],
            Dependencies: ["support.source-tree"], Requirements: [".NET 10", "wasm-tools workload", "WebGPU browser", "HTTPS or localhost"],
            ExportSymbol: "LuxelWebGpuBrowser.Program", RunCommand: "dotnet publish samples/LuxelWebGpuBrowser/LuxelWebGpuBrowser.csproj -c Release",
            SmokeCommand: "dotnet publish samples/LuxelWebGpuBrowser/LuxelWebGpuBrowser.csproj -c Release", Platforms: ["Browser/WASM"],
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
            Platforms: ["Windows", "Linux"], TimeoutSeconds: 180, ExpectedStdoutMarker: "tutorial-3d: 1 frame(s), stage=Triangle"));
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
