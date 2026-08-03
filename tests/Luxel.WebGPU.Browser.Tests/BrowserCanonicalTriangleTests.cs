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
        string html = File.ReadAllText(Path.Combine(root, "samples", "LuxelWebGpuBrowser", "wwwroot", "index.html"));
        string shaderPath = Path.Combine(root, "shaders", "compiled", "tutorial_triangle.wgsl");

        Assert.Contains("CanonicalClearColorRecipe.cs", project);
        Assert.Contains("CanonicalClearColorRecipe.Story", program);
        Assert.Contains("RunClearColor()", program);
        Assert.Contains("CanonicalClearColorRecipe.Red", program);
        Assert.Contains("backend.CreateCanvasSurface", program);
        Assert.Contains("CanonicalTriangleRecipe.cs", project);
        Assert.Contains("shaders\\compiled\\tutorial_triangle.wgsl", project);
        Assert.DoesNotContain("Shaders\\triangle.wgsl", project);
        Assert.Contains("CanonicalTriangleRecipe.CreateVertices()", program);
        Assert.DoesNotContain("checker", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("width=\"320\" height=\"240\"", html);
        Assert.DoesNotContain("aspect-ratio", html);
        Assert.Contains(":root,html,body,#runtime-root,#luxel-canvas{width:100%;height:100%;margin:0;padding:0}", html);
        Assert.Contains("<div id=\"status\" hidden", html);
        Assert.DoesNotContain("<h1>", html);
        Assert.DoesNotContain("Move/click over", html);
        Assert.Contains("if (resizePending)", program);
        Assert.Contains("resizePending = false;", program);
        Assert.Contains($"data-story=\"{CanonicalTriangleRecipe.Story}\"", html);
        Assert.Contains($"data-shader=\"{CanonicalTriangleRecipe.Shader}\"", html);
        Assert.Contains($"data-vertex-size=\"{CanonicalTriangleRecipe.VertexSize}\"", html);
        Assert.Contains($"data-root-size=\"{CanonicalTriangleRecipe.DrawArgsSize}\"", html);
        Assert.Contains($"data-canvas=\"{CanonicalTriangleRecipe.Width}x{CanonicalTriangleRecipe.Height}\"", html);
        Assert.Contains($"data-recipe=\"{CanonicalTriangleRecipe.Recipe}\"", html);
        Assert.Contains($"data-hash=\"{CanonicalTriangleRecipe.ShaderSha256}\"", html);
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
