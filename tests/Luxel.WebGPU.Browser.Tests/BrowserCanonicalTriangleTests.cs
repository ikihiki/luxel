namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserCanonicalTriangleTests
{
    [Fact]
    public void Browser_sample_tracks_canonical_triangle_shader_and_markers()
    {
        string root = FindRepositoryRoot();
        string project = File.ReadAllText(Path.Combine(root, "src", "Gallery", "Luxel.Gallery.Browser", "Luxel.Gallery.Browser.csproj"));
        string program = File.ReadAllText(Path.Combine(root, "src", "Gallery", "Luxel.Gallery.Browser", "BrowserGalleryApplication.cs"));
        string graphicsGalleryProject = File.ReadAllText(Path.Combine(root, "src", "Luxel.Graphics.Gallery", "Luxel.Graphics.Gallery.csproj"));
        string canonicalTriangle = File.ReadAllText(Path.Combine(root, "samples", "CanonicalTriangleRecipe.cs"));
        string gpuStories = File.ReadAllText(Path.Combine(root, "src", "Luxel.Graphics.Gallery", "Stories", "Gpu", "GpuViewStories.cs"));
        string html = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "wwwroot", "index.html"));
        string css = File.ReadAllText(Path.Combine(root, "gallery", "GalleryBrowser", "wwwroot", "gallery.css"));

        Assert.DoesNotContain("Luxel.Gallery.Stories.CoreUi.csproj", project);
        Assert.DoesNotContain("CanonicalClearColorRecipe.cs", project);
        Assert.DoesNotContain("CanonicalTriangleRecipe.cs", project);
        Assert.DoesNotContain("struct Vertex", canonicalTriangle);
        Assert.DoesNotContain("CreateVertices", canonicalTriangle);
        Assert.Contains("CanonicalClearColorRecipe.cs", graphicsGalleryProject);
        Assert.Contains("CanonicalTriangleRecipe.cs", graphicsGalleryProject);
        Assert.Contains("await RunCatalogStory(story, argsJson)", program);
        Assert.DoesNotContain("RunClearColor", program);
        Assert.DoesNotContain("RunTriangle", program);
        Assert.DoesNotContain("CanonicalClearColorRecipe.Red", gpuStories);
        Assert.Contains("BeginRendering(surface.ColorTarget, null, 0.055f, 0.07f, 0.11f, 1f)", gpuStories);
        Assert.DoesNotContain("CanonicalTriangleRecipe.CreateVertices()", gpuStories);
        Assert.Contains("float[] vertices =", gpuStories);
        Assert.Contains("RWByteAddressBuffer g_buffers[];", gpuStories);
        Assert.Contains("Vertex vertex = g_buffers[g_args.vertexBufferIndex].Load<Vertex>(vertexId * 32);", gpuStories);
        Assert.Contains("new SlangSource(\"triangle.slang\", slang)", gpuStories);
        Assert.Contains("Create<SlangSource, GpuShaderCode>", gpuStories);
        Assert.Contains("Create<float[], GpuBuffer>", gpuStories);
        Assert.Contains("ctx.Observe(vertexBuffer)", gpuStories);
        Assert.DoesNotContain("CreateBuffer<float>", gpuStories);
        Assert.DoesNotContain("vertices.CopyTo(vertexBuffer.Value.Span", gpuStories);
        Assert.DoesNotContain("WaitFor(vertexBuffer)", gpuStories);
        Assert.Contains("resources.CreateGraphicsPipeline(\n            \"triangle.pipeline\", shader,", gpuStories);
        Assert.Contains("ctx.Observe(pipeline)", gpuStories);
        Assert.Contains("GpuViewRenderResult.Loading", gpuStories);
        Assert.Contains("GpuViewRenderResult.Failed", gpuStories);
        Assert.DoesNotContain("ctx.Initialize", gpuStories);
        Assert.DoesNotContain("CreatePipelineAsync", gpuStories);
        Assert.DoesNotContain("await shader.Ready", gpuStories);
        Assert.DoesNotContain("pipeline.GetAwaiter().GetResult()", gpuStories);
        Assert.DoesNotContain("GpuShaderCode.Load", gpuStories);
        Assert.DoesNotContain("const string wgsl", gpuStories);
        Assert.Contains("GpuView(", gpuStories);
        Assert.Contains("ctx.ScopedResources", gpuStories);
        Assert.Contains("browserBackend.CreateCanvasSurface", program);
        Assert.DoesNotContain("tutorial_triangle.wgsl", project);
        Assert.DoesNotContain("Shaders\\triangle.wgsl", project);
        Assert.DoesNotContain("checker", program, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("width=\"320\" height=\"240\"", html);
        Assert.DoesNotContain("aspect-ratio", html);
        Assert.Contains("<link rel=\"stylesheet\" href=\"gallery.css\">", html);
        Assert.Contains("html, body, #app { width: 100%; height: 100%; margin: 0; }", css);
        Assert.Contains(".gallery-embed, .gallery-embed #luxel-canvas", css);
        Assert.Contains("<div id=\"status\" hidden", html);
        Assert.DoesNotContain("<h1>", html);
        Assert.DoesNotContain("Move/click over", html);
        Assert.Contains("if (resizePending)", program);
        Assert.Contains("resizePending = false;", program);
        Assert.DoesNotContain("data-story=", html);
        Assert.DoesNotContain("data-shader=", html);
        Assert.DoesNotContain("data-recipe=", html);
        Assert.Contains("data-status=\"loading\"", html);
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
