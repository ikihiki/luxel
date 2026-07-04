using System.Runtime.InteropServices;
using Luxel;

namespace Luxel.Samples;

/// <summary>
/// サンプル02: 三角形 (Vulkan)。オフスクリーンの RGBA8 ターゲットへ dynamic rendering で
/// 三角形を描き (vertex pulling)、結果をバッファへコピーして読み戻し、描画されたことを検証する。
/// ウィンドウ表示版は後続。現状 D3D12 は graphics 未対応。
/// </summary>
public static class Sample02Triangle
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public float Px, Py, Pz, Pw;  // position (clip space)
        public float R, G, B, A;      // color
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs { public uint VertexBufferIndex; }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint w = 256, h = 256;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        using GpuTexture target = device.CreateRenderTarget(w, h, GpuFormat.Rgba8Unorm);

        using GpuBuffer vb = device.Malloc(3 * 32, GpuMemoryKind.HostMapped);
        Span<Vertex> verts = vb.Span<Vertex>(3);
        verts[0] = new Vertex { Px = 0.0f, Py = -0.6f, Pz = 0, Pw = 1, R = 1, G = 0, B = 0, A = 1 };
        verts[1] = new Vertex { Px = 0.6f, Py = 0.6f, Pz = 0, Pw = 1, R = 0, G = 1, B = 0, A = 1 };
        verts[2] = new Vertex { Px = -0.6f, Py = 0.6f, Pz = 0, Pw = 1, R = 0, G = 0, B = 1, A = 1 };

        using GpuBuffer readback = device.Malloc(w * h * 4, GpuMemoryKind.HostMapped);

        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("triangle"), raster);

        var args = new DrawArgs { VertexBufferIndex = vb.BindlessIndex };
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
        int total = (int)(w * h);
        int nonBackground = 0;
        for (int i = 0; i < total; i++)
        {
            if (px[i * 4] != 0 || px[i * 4 + 1] != 0 || px[i * 4 + 2] != 0) nonBackground++;
        }
        int center = ((int)(h / 2) * (int)w + (int)(w / 2)) * 4;
        bool centerColored = px[center] != 0 || px[center + 1] != 0 || px[center + 2] != 0;

        Console.WriteLine($"描画ピクセル: {nonBackground}/{total}, 中心ピクセル RGBA=" +
            $"({px[center]},{px[center + 1]},{px[center + 2]},{px[center + 3]})");

        string pngPath = Path.Combine(AppContext.BaseDirectory, "triangle.png");
        PngWriter.WriteRgba(pngPath, (int)w, (int)h, px);
        Console.WriteLine($"PNG 出力: {pngPath}");

        // 三角形が描かれていれば中心は塗られ、非背景ピクセルは一部 (全部でも 0 でもない)。
        if (centerColored && nonBackground > total / 20 && nonBackground < total)
        {
            Console.WriteLine("OK: 三角形が正しく描画されました");
            return 0;
        }
        Console.Error.WriteLine("FAILED: 三角形の描画を検出できませんでした");
        return 1;
    }
}
