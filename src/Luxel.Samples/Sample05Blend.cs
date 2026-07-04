using System.Runtime.InteropServices;
using Luxel;

namespace Luxel.Samples;

/// <summary>
/// サンプル05: アルファブレンド。赤(不透明)を描いた上に青(α=0.5)を重ねる。
/// 重なり中心は 0.5*青 + 0.5*赤 = 紫 (約 128,0,128) になることを検証する。
/// </summary>
public static class Sample05Blend
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public float Px, Py, Pz, Pw, R, G, B, A; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs { public uint VertexBufferIndex; }

    private static GpuBuffer MakeTriangle(GpuDevice d, (float, float)[] xy, (float, float, float, float) c)
    {
        var vb = d.Malloc(3 * 32, GpuMemoryKind.HostMapped);
        var v = vb.Span<Vertex>(3);
        for (int i = 0; i < 3; i++)
            v[i] = new Vertex { Px = xy[i].Item1, Py = xy[i].Item2, Pz = 0, Pw = 1, R = c.Item1, G = c.Item2, B = c.Item3, A = c.Item4 };
        return vb;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 256;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using GpuTexture target = device.CreateRenderTarget(w, h, GpuFormat.Rgba8Unorm);
        using GpuBuffer readback = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        using GpuBuffer red = MakeTriangle(device, [(-0.6f, -0.6f), (0.6f, -0.6f), (0.0f, 0.6f)], (1, 0, 0, 1));
        using GpuBuffer blue = MakeTriangle(device, [(-0.6f, 0.6f), (0.6f, 0.6f), (0.0f, -0.6f)], (0, 0, 1, 0.5f));

        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.Blend = GpuBlendMode.AlphaBlend;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("triangle"), raster);

        using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
        cmd.BeginRendering(target, null, 0, 0, 0, 1, 1f)
           .SetGraphicsPipeline(pipeline)
           .SetRootArguments(new DrawArgs { VertexBufferIndex = red.BindlessIndex })
           .Draw(3)
           .SetRootArguments(new DrawArgs { VertexBufferIndex = blue.BindlessIndex })
           .Draw(3)
           .EndRendering()
           .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
           .CopyTextureToBuffer(target, readback);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);

        Span<byte> px = readback.Span<byte>((int)(w * h * 4));
        int ci = ((int)h / 2 * (int)w + (int)w / 2) * 4;
        (byte r, byte g, byte b) = (px[ci], px[ci + 1], px[ci + 2]);
        Console.WriteLine($"中心ピクセル RGB=({r},{g},{b})  (期待: 約 128,0,128)");
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "blend.png"), (int)w, (int)h, px);

        bool ok = r is > 100 and < 160 && b is > 100 and < 160 && g < 30;
        Console.WriteLine(ok ? "OK: アルファブレンドで赤と青が混ざり紫になりました"
                             : "FAILED: ブレンド結果が期待と異なります");
        return ok ? 0 : 1;
    }
}
