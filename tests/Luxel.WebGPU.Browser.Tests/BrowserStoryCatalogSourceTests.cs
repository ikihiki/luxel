using System.Text.Json;

namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserStoryCatalogSourceTests
{
    [Fact]
    public void Browser_host_uses_the_CoreUi_catalog_and_protocol_v2_descriptor_manifest()
    {
        string root = FindRepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "samples", "LuxelWebGpuBrowser", "Program.cs"));
        string script = File.ReadAllText(Path.Combine(root, "samples", "LuxelWebGpuBrowser", "wwwroot", "main.js"));
        string project = File.ReadAllText(Path.Combine(root, "samples", "LuxelWebGpuBrowser", "LuxelWebGpuBrowser.csproj"));
        string compiler = File.ReadAllText(Path.Combine(root, "src", "Luxel.Shaders.Slang.Browser", "BrowserSlangCompiler.cs"));
        string gpuResources = File.ReadAllText(Path.Combine(root, "src", "Luxel.AssetsGpu", "ResourceSystemExtensions.cs"));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "samples", "LuxelWebGpuBrowser", "wwwroot", "browser-runtime-manifest.json")));

        Assert.Contains("CoreUiStoryProject.CreateCatalog()", program, StringComparison.Ordinal);
        Assert.Contains("Catalog.Find(path)", program, StringComparison.Ordinal);
        Assert.Contains("story.RuntimeBundleId != CoreUiStoryProject.RuntimeBundleId", program, StringComparison.Ordinal);
        Assert.Contains("new StoryContext(resources, args)", program, StringComparison.Ordinal);
        Assert.Contains("context.SetGpuHost(device, font)", program, StringComparison.Ordinal);
        Assert.Contains("resources.InstallAssetGpuLifecycle(device)", program, StringComparison.Ordinal);
        Assert.Contains("new BrowserSlangCompiler()", program, StringComparison.Ordinal);
        Assert.Contains("AddStep<SlangSource, GpuShaderCode>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("context.Ready", program, StringComparison.Ordinal);
        Assert.True(program.IndexOf("ui.SetRoot(result.Widget)", StringComparison.Ordinal)
            < program.IndexOf("resources.Pump()", StringComparison.Ordinal));
        Assert.True(program.IndexOf("resources.Pump()", StringComparison.Ordinal)
            < program.IndexOf("ui.Tick(1f / 60f)", StringComparison.Ordinal));
        Assert.Contains("Luxel.Shaders.Slang.Browser.csproj", project, StringComparison.Ordinal);
        Assert.Contains("wwwroot\\slang-worker.js", project, StringComparison.Ordinal);
        Assert.Contains("BrowserSlangJsonContext.Default.BrowserCompileRequest", compiler, StringComparison.Ordinal);
        Assert.Contains("BrowserSlangJsonContext.Default.BrowserCompileResponse", compiler, StringComparison.Ordinal);
        Assert.Contains("AddStep<CpuImage, GpuTexture>", gpuResources, StringComparison.Ordinal);
        Assert.Contains("AddStep<GpuBufferRequest, GpuBuffer>", gpuResources, StringComparison.Ordinal);
        Assert.Contains("AddStep<float[], GpuBuffer>", gpuResources, StringComparison.Ordinal);
        Assert.Contains("SnapshotWidgets(result.Widget)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("path switch", program, StringComparison.Ordinal);
        Assert.DoesNotContain("RunClearColor", program, StringComparison.Ordinal);
        Assert.DoesNotContain("RunTriangle", program, StringComparison.Ordinal);

        Assert.Contains("const protocolVersion = 2", script, StringComparison.Ordinal);
        Assert.Contains("import * as slang from \"./slang-browser.js\"", script, StringComparison.Ordinal);
        Assert.Contains("runtime.setModuleImports(\"luxel-slang\", slang)", script, StringComparison.Ordinal);
        Assert.Contains("message.type !== \"set-args\"", script, StringComparison.Ordinal);
        Assert.Contains("event.source !== parent", script, StringComparison.Ordinal);
        Assert.Contains("event.origin !== location.origin", script, StringComparison.Ordinal);
        Assert.Contains("publishArgsChanged", script, StringComparison.Ordinal);
        Assert.Contains("publishEvent", program, StringComparison.Ordinal);
        Assert.Contains("publishEvent", script, StringComparison.Ordinal);
        Assert.Contains("post(\"event\", { entry })", script, StringComparison.Ordinal);
        Assert.Contains("publishDiagnostics", script, StringComparison.Ordinal);

        JsonElement manifest = document.RootElement;
        Assert.Equal(2, manifest.GetProperty("protocolVersion").GetInt32());
        JsonElement[] production = manifest.GetProperty("stories").EnumerateArray()
            .Where(story => story.GetProperty("componentType").ValueKind == JsonValueKind.String)
            .ToArray();
        JsonElement blur = manifest.GetProperty("stories").EnumerateArray()
            .Single(story => story.GetProperty("path").GetString() == "Examples/RenderGraph/Blur");
        string[] pipelineStories =
        [
            "Examples/3D/PipelineState/Topology", "Examples/3D/PipelineState/Rasterizer",
            "Examples/3D/PipelineState/Depth", "Examples/3D/PipelineState/Blend",
            "Examples/3D/PipelineState/Stencil", "Examples/3D/PipelineState/ViewportScissor",
            "Examples/3D/PipelineState/Separation", "Examples/3D/Depth", "Examples/3D/Blend",
        ];
        string[] runtimePaths = manifest.GetProperty("stories").EnumerateArray()
            .Select(story => story.GetProperty("path").GetString()!)
            .ToArray();
        Assert.All(pipelineStories, path => Assert.Contains(path, runtimePaths));
        Assert.Equal(320, blur.GetProperty("width").GetInt32());
        Assert.Equal(320, blur.GetProperty("height").GetInt32());
        Assert.Equal("Runs through the shared Gallery WebAssembly story runner.",
            blur.GetProperty("capabilityNote").GetString());

        string[] inputStories =
        [
            "Examples/Input/SourcesAndBus",
            "Examples/Input/Actions",
            "Examples/Input/ContextStack",
            "Examples/Input/Bindings",
        ];
        foreach (string path in inputStories)
        {
            JsonElement input = manifest.GetProperty("stories").EnumerateArray()
                .Single(story => story.GetProperty("path").GetString() == path);
            Assert.Equal(JsonValueKind.Array, input.GetProperty("args").ValueKind);
            Assert.Null(input.GetProperty("capabilityNote").GetString());
        }

        Assert.Equal(60, production.Length);
        Assert.Equal(60, production.Select(story => story.GetProperty("path").GetString())
            .Distinct(StringComparer.Ordinal).Count());
        Assert.All(production, story => Assert.EndsWith("/Basic", story.GetProperty("path").GetString(), StringComparison.Ordinal));
        Assert.All(production, story => Assert.Equal(JsonValueKind.Array, story.GetProperty("args").ValueKind));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Luxel.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the Luxel repository root.");
    }
}
