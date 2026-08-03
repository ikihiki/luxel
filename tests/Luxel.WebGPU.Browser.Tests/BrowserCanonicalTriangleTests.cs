using System.Security.Cryptography;
using System.Text;

namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserCanonicalTriangleTests
{
    [Fact]
    public void Browser_sample_tracks_canonical_triangle_shader_and_markers()
    {
        string root = FindRepositoryRoot();
        string project = File.ReadAllText(Path.Combine(root, "samples", "LuxelWebGpuBrowser", "LuxelWebGpuBrowser.csproj"));
        string program = File.ReadAllText(Path.Combine(root, "samples", "LuxelWebGpuBrowser", "Program.cs"));
        string storiesProject = File.ReadAllText(Path.Combine(root, "src", "Luxel.Gallery.Stories.CoreUi", "Luxel.Gallery.Stories.CoreUi.csproj"));
        string gpuStories = File.ReadAllText(Path.Combine(root, "src", "Luxel.Gallery.Stories.CoreUi", "Stories", "GpuViewStories.cs"));
        string html = File.ReadAllText(Path.Combine(root, "samples", "LuxelWebGpuBrowser", "wwwroot", "index.html"));
        string shaderPath = Path.Combine(root, "shaders", "compiled", "tutorial_triangle.wgsl");

        Assert.Contains("Luxel.Gallery.Stories.CoreUi.csproj", project);
        Assert.DoesNotContain("CanonicalClearColorRecipe.cs", project);
        Assert.DoesNotContain("CanonicalTriangleRecipe.cs", project);
        Assert.Contains("CanonicalClearColorRecipe.cs", storiesProject);
        Assert.Contains("CanonicalTriangleRecipe.cs", storiesProject);
        Assert.Contains("await RunCatalogStory(story)", program);
        Assert.DoesNotContain("RunClearColor", program);
        Assert.DoesNotContain("RunTriangle", program);
        Assert.Contains("CanonicalClearColorRecipe.Red", gpuStories);
        Assert.Contains("CanonicalTriangleRecipe.CreateVertices()", gpuStories);
        Assert.Contains("GpuView(", gpuStories);
        Assert.Contains("ctx.ScopedResources", gpuStories);
        Assert.Contains("browserBackend.CreateCanvasSurface", program);
        Assert.DoesNotContain("tutorial_triangle.wgsl", project);
        Assert.DoesNotContain("Shaders\\triangle.wgsl", project);
        Assert.DoesNotContain("checker", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("width=\"320\" height=\"240\"", html);
        Assert.DoesNotContain("aspect-ratio", html);
        Assert.Contains(":root,html,body,#runtime-root,#luxel-canvas{width:100%;height:100%;margin:0;padding:0}", html);
        Assert.Contains("<div id=\"status\" hidden", html);
        Assert.DoesNotContain("<h1>", html);
        Assert.DoesNotContain("Move/click over", html);
        Assert.Contains("if (resizePending)", program);
        Assert.Contains("resizePending = false;", program);
        Assert.DoesNotContain("data-story=", html);
        Assert.DoesNotContain("data-shader=", html);
        Assert.DoesNotContain("data-recipe=", html);
        Assert.Contains("data-status=\"loading\"", html);

        byte[] shader = File.ReadAllBytes(shaderPath);
        Assert.Equal(CanonicalTriangleRecipe.ShaderSha256, Convert.ToHexString(SHA256.HashData(shader)).ToLowerInvariant());
        string wgsl = Encoding.UTF8.GetString(shader);
        Assert.Contains("fn vsMain", wgsl);
        Assert.Contains("fn psMain", wgsl);
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
