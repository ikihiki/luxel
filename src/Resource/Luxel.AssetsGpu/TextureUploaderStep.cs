using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// <see cref="CpuImage"/> → <see cref="GpuTexture"/> (GPU アップロード)。
/// GpuDevice はコンストラクタ DI で受け取る。AssetsGpu を Install したときに一緒に登録される。
/// </summary>
public sealed class TextureUploaderStep(GpuDevice device) : IResourceStep<CpuImage, GpuTexture>
{
    private readonly GpuDevice _device = device;
    public Task<GpuTexture> RunAsync(CpuImage img, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(_device.CreateTexture((uint)img.Width, (uint)img.Height, img.Pixels));
}
