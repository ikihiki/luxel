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
        string gpuResources = File.ReadAllText(Path.Combine(root, "src", "Luxel.AssetsGpu", "ResourceSystemExtensions.cs"));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "samples", "LuxelWebGpuBrowser", "wwwroot", "browser-runtime-manifest.json")));

        Assert.Contains("CoreUiStoryProject.CreateCatalog()", program, StringComparison.Ordinal);
        Assert.Contains("Catalog.Find(path)", program, StringComparison.Ordinal);
        Assert.Contains("story.RuntimeBundleId != CoreUiStoryProject.RuntimeBundleId", program, StringComparison.Ordinal);
        Assert.Contains("new StoryContext(resources, args)", program, StringComparison.Ordinal);
        Assert.Contains("context.SetGpuHost(device, font)", program, StringComparison.Ordinal);
        Assert.Contains("resources.InstallAssetGpuLifecycle(device)", program, StringComparison.Ordinal);
        Assert.Contains("AddStep<CpuImage, GpuTexture>", gpuResources, StringComparison.Ordinal);
        Assert.Contains("AddStep<GpuBufferRequest, GpuBuffer>", gpuResources, StringComparison.Ordinal);
        Assert.Contains("SnapshotWidgets(result.Widget)", program, StringComparison.Ordinal);
        Assert.DoesNotContain("path switch", program, StringComparison.Ordinal);
        Assert.DoesNotContain("RunClearColor", program, StringComparison.Ordinal);
        Assert.DoesNotContain("RunTriangle", program, StringComparison.Ordinal);

        Assert.Contains("const protocolVersion = 2", script, StringComparison.Ordinal);
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
