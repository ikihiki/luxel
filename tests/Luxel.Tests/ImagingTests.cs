using Luxel.Imaging;
using Luxel.Resources;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Luxel.Tests;

/// <summary>EX-M3: 画像デコード step (ImageSharp) と Resource システム経由のロード。</summary>
public class ImagingTests
{
    private static byte[] MakePng(int w, int h, Rgba32 color)
    {
        using var img = new Image<Rgba32>(w, h, color);
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task ImageSharpDecoder_DecodesPngToCpuImage()
    {
        var dec = new ImageSharpDecoder();
        byte[] png = MakePng(3, 2, new Rgba32(10, 20, 30, 255));
        CpuImage img = await dec.RunAsync(png, new ResourceUri("file://x.png"), null!);
        Assert.Equal(3, img.Width);
        Assert.Equal(2, img.Height);
        Assert.Equal(3 * 2 * 4, img.Pixels.Length);
        Assert.Equal(10, img.Pixels[0]);   // R
        Assert.Equal(20, img.Pixels[1]);   // G
        Assert.Equal(30, img.Pixels[2]);   // B
    }

    [Fact]
    public async Task ResourceSystem_LoadsPngViaDecoder_AndCaches()
    {
        string dir = Path.Combine(Path.GetTempPath(), "luxel-imaging-test");
        Directory.CreateDirectory(dir);
        byte[] png = MakePng(4, 4, new Rgba32(1, 2, 3, 255));
        var files = new MemoryFileSystem();
        files.Set("t.png", png);

        using var res = new ResourceSystem(
            sources: [new FileSource(files)],
            steps: [new ImageSharpDecoder()]);

        using ResourceHandle<CpuImage> h1 = res.Load<CpuImage>("t.png");
        await h1.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ResourceStatus.Ready, h1.Status);
        Assert.Equal(4, h1.Value.Width);

        using ResourceHandle<CpuImage> h2 = res.Load<CpuImage>("t.png");
        Assert.True(h2.IsReady);                     // キャッシュヒット (即 Ready)
        Assert.Same(h1.Value, h2.Value);             // 同一インスタンス共有
    }
}
