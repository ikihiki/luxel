using System.Runtime.InteropServices;
using Luxel;

namespace Luxel.Samples;

/// <summary>
/// サンプル04: 深度テスト。手前(緑, z=0.3)と奥(赤, z=0.7)の 2 枚を「手前→奥」の順に描く。
/// 深度テスト(LessEqual)により重なり中心は手前の緑が残る (描画順では奥が勝つはずなので深度が効いている証拠)。
/// </summary>
public static class Sample04Depth
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public float Px, Py, Pz, Pw, R, G, B, A; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs { public uint VertexBufferIndex; }

    private static GpuBuffer MakeTriangle(GpuDevice d, float z, (float, float)[] xy, (float, float, float, float) c)
    {
        var vb = d.Malloc(3 * 32, GpuMemoryKind.HostMapped);
        var v = vb.Span<Vertex>(3);
        for (int i = 0; i < 3; i++)
            v[i] = new Vertex { Px = xy[i].Item1, Py = xy[i].Item2, Pz = z, Pw = 1, R = c.Item1, G = c.Item2, B = c.Item3, A = c.Item4 };
        return vb;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 256;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using GpuTexture target = device.CreateRenderTarget(w, h, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(w, h);
        using GpuBuffer readback = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        // 手前(緑, 上向き三角形) / 奥(赤, 下向き三角形)。中心で重なる。
        using GpuBuffer near = MakeTriangle(device, 0.3f, [(-0.6f, -0.6f), (0.6f, -0.6f), (0.0f, 0.6f)], (0, 1, 0, 1));
        using GpuBuffer far = MakeTriangle(device, 0.7f, [(-0.6f, 0.6f), (0.6f, 0.6f), (0.0f, -0.6f)], (1, 0, 0, 1));

        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true;
        raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("triangle"), raster);

        using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();
        cmd.BeginRendering(target, depth, 0, 0, 0, 1, 1f)
           .SetGraphicsPipeline(pipeline)
           .SetRootArguments(new DrawArgs { VertexBufferIndex = near.BindlessIndex })
           .Draw(3)                                            // 手前(緑)を先に
           .SetRootArguments(new DrawArgs { VertexBufferIndex = far.BindlessIndex })
           .Draw(3)                                            // 奥(赤)を後に → 深度で中心は負ける
           .EndRendering()
           .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
           .CopyTextureToBuffer(target, readback);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);

        Span<byte> px = readback.Span<byte>((int)(w * h * 4));
        int ci = ((int)h / 2 * (int)w + (int)w / 2) * 4;
        var center = (r: px[ci], g: px[ci + 1], b: px[ci + 2]);
        Console.WriteLine($"中心ピクセル RGB=({center.r},{center.g},{center.b})");
        PngWriter.WriteRgba(Path.Combine(AppContext.BaseDirectory, "depth.png"), (int)w, (int)h, px);

        // 中心は手前(緑)が残るはず。描画順だけなら赤になる → 緑なら深度テストが効いている。
        bool ok = center.g > 128 && center.r < 80;
        Console.WriteLine(ok ? "OK: 深度テストで手前の三角形が正しく前面に出ています"
                             : "FAILED: 深度テストが効いていません");
        return ok ? 0 : 1;
    }
}
