namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserRenderGraphTests
{
    [Fact]
    public void Blur_story_uses_browser_safe_RenderGraph_submission_and_lifetimes()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(root,
            "src", "Luxel.Graphics.Gallery", "Stories", "RenderGraph", "BrowserRenderGraphStories.cs"));
        string project = File.ReadAllText(Path.Combine(root,
            "src", "Luxel.Graphics.Gallery", "Luxel.Graphics.Gallery.csproj"));

        Assert.Contains("[Story(\"Examples/RenderGraph/Blur\"", source);
        Assert.Contains("Luxel.Graphics.RenderGraph.csproj", project);
        Assert.Contains("ResourceHandle<GpuPipeline> blurPipeline", source);
        Assert.Contains("ResourceHandle<GpuPipeline> compositePipeline", source);
        Assert.Contains("new BufferDesc(bytes, GpuMemoryKind.DeviceLocal)", source);
        Assert.Contains("gpu.MainQueue.Submit(command)", source);
        Assert.DoesNotContain("SubmitAndWait", source);
        Assert.Contains("graph?.Dispose()", source);
        Assert.Contains("input.Dispose()", source);
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
