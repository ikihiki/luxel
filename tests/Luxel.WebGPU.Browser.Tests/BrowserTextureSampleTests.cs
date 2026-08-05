namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserTextureSampleTests
{
    [Fact]
    public void Textures_story_samples_a_generated_checker_texture()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root, "src", "Luxel.Gallery.Stories.CoreUi", "Stories", "GpuViewStories.cs"));

        Assert.Contains("[Story(\"Examples/3D/Textures\"", source);
        Assert.Contains("byte[] pixels = CreateCheckerboard(textureWidth, textureHeight)", source);
        Assert.Contains("ResourceHandle<GpuTexture> texture = resources.CreateSampledTexture(", source);
        Assert.Contains("ResourceHandle<GpuSampler> sampler = resources.CreateSampler(", source);
        Assert.Contains("Signal<ResourceState> textureState = ctx.Observe(texture)", source);
        Assert.Contains("Signal<ResourceState> samplerState = ctx.Observe(sampler)", source);
        Assert.DoesNotContain("device.CreateTexture(", source);
        Assert.Contains("GpuSamplerFilter.Point, GpuSamplerAddress.Repeat", source);
        Assert.Contains("Texture2D g_textures[]", source);
        Assert.Contains("SamplerState g_samplers[]", source);
        Assert.Contains(".Sample(g_samplers[g_args.samplerIndex], input.uv)", source);
        Assert.Contains("TextureIndex = texture.Value.BindlessIndex", source);
        Assert.Contains("SamplerIndex = sampler.Value.BindlessIndex", source);
        Assert.Contains("private static byte[] CreateCheckerboard(uint width, uint height)", source);
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
