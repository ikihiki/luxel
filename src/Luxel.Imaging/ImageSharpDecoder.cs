using Luxel.Resources;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Luxel.Imaging;

/// <summary>
/// 一般画像 (png/jpg/bmp/gif/webp/tga) → <see cref="CpuImage"/> の Resource step (ImageSharp)。
/// アプリが ResourceSystem に登録すると `Load&lt;CpuImage&gt;("file://x.png")` が通るようになる。
/// </summary>
public sealed class ImageSharpDecoder : IResourceStep<byte[], CpuImage>
{
    public Executor Executor => Executor.Cpu;
    public IEnumerable<string> Extensions => [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tga"];

    public Task<CpuImage> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx)
    {
        using Image<Rgba32> img = Image.Load<Rgba32>(input);
        var pixels = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(pixels);
        return Task.FromResult(new CpuImage(img.Width, img.Height, pixels));
    }
}
