using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// <see cref="CpuImage"/> → <see cref="GpuTexture"/> (GPU アップロード)。
/// The generation-aware registry resolves the current device when work begins.
/// </summary>
public sealed class TextureUploaderStep(AssetGpuRegistry registry) : IResourceStep<CpuImage, GpuTexture>
{
    public Task<GpuTexture> RunAsync(CpuImage img, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(registry.Create(new GpuTextureRequest(GpuTextureRequestKind.Sampled,
            (uint)img.Width, (uint)img.Height, GpuFormat.Rgba8Unorm, img.Pixels)));
}
