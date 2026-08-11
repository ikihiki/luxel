namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserBufferQuadTests
{
    [Fact]
    public void BuffersAndBindings_story_renders_an_indexed_quad_from_three_buffers()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root, "src", "Graphics", "Luxel.Graphics.Gallery", "Stories", "Gpu", "GpuViewStories.cs"));

        Assert.Contains("[Story(\"Examples/3D/BuffersAndBindings\"", source);
        Assert.Contains("float[] vertices", source);
        Assert.Contains("uint[] indices = [0, 1, 2, 0, 2, 3]", source);
        Assert.Contains("float[] colors", source);
        Assert.Contains("Vertices = CreateBuffer(device, vertices)", source);
        Assert.Contains("Indices = CreateBuffer(device, indices)", source);
        Assert.Contains("Colors = CreateBuffer(device, colors)", source);
        Assert.Contains("Load<uint>(vertexId * 4)", source);
        Assert.Contains("Load2(index * 8)", source);
        Assert.Contains("Load4(index * 16)", source);
        Assert.Contains("VertexBufferIndex = buffers.Vertices.BindlessIndex", source);
        Assert.Contains("IndexBufferIndex = buffers.Indices.BindlessIndex", source);
        Assert.Contains("ColorBufferIndex = buffers.Colors.BindlessIndex", source);
        Assert.Contains(".Draw(6)", source);
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
