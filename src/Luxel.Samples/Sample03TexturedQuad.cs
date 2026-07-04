using System.Runtime.InteropServices;
using Luxel;

namespace Luxel.Samples;

/// <summary>
/// サンプル03: bindless テクスチャ + サンプラ。2x2 の 4 色テクスチャをアップロードし、
/// フルスクリーン三角形でサンプリングしてオフスクリーンへ描画。読み戻して 4 象限に
/// 4 色が出ていることを検証する。現状 Vulkan。
/// </summary>
public static class Sample03TexturedQuad
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs { public uint TextureIndex; public uint SamplerIndex; }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 256;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // 2x2 の 4 色テクスチャ (row-major, RGBA8)。
        byte[] texels =
        [
            255, 0, 0, 255,    0, 255, 0, 255,    // (0,0)=赤  (1,0)=緑
            0, 0, 255, 255,    255, 255, 255, 255 // (0,1)=青  (1,1)=白
        ];
        using GpuTexture texture = device.CreateTexture(2, 2, texels);
        using GpuSampler sampler = device.CreateSampler(GpuSamplerFilter.Point);

        using GpuTexture target = device.CreateRenderTarget(w, h, GpuFormat.Rgba8Unorm);
        using GpuBuffer readback = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("textured"), raster);

        var args = new DrawArgs { TextureIndex = texture.BindlessIndex, SamplerIndex = sampler.BindlessIndex };
        using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
        cmd.BeginRendering(target, null, 0, 0, 0, 1)
           .SetGraphicsPipeline(pipeline)
           .SetRootArguments(args)
           .Draw(3)
           .EndRendering()
           .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
           .CopyTextureToBuffer(target, readback);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);

        Span<byte> px = readback.Span<byte>((int)(w * h * 4));
        (int, int)[] quadrants = [((int)(w / 4), (int)(h / 4)), ((int)(3 * w / 4), (int)(h / 4)),
                                  ((int)(w / 4), (int)(3 * h / 4)), ((int)(3 * w / 4), (int)(3 * h / 4))];
        var colors = new (byte r, byte g, byte b)[4];
        for (int i = 0; i < 4; i++)
        {
            int idx = (quadrants[i].Item2 * (int)w + quadrants[i].Item1) * 4;
            colors[i] = (px[idx], px[idx + 1], px[idx + 2]);
            Console.WriteLine($"  象限{i}: RGB=({colors[i].r},{colors[i].g},{colors[i].b})");
        }

        string pngPath = Path.Combine(AppContext.BaseDirectory, "textured.png");
        PngWriter.WriteRgba(pngPath, (int)w, (int)h, px);
        Console.WriteLine($"PNG 出力: {pngPath}");

        bool allColored = colors.All(c => c.r != 0 || c.g != 0 || c.b != 0);
        int distinct = colors.Distinct().Count();
        if (allColored && distinct == 4)
        {
            Console.WriteLine("OK: 2x2 テクスチャの 4 色が正しくサンプリングされました");
            return 0;
        }
        Console.Error.WriteLine($"FAILED: allColored={allColored}, distinct={distinct}");
        return 1;
    }
}
