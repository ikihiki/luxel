using System.Numerics;
using System.Runtime.InteropServices;
using Luxel;
using Luxel.Ecs;
using Luxel.Ecs.Signal;
using Luxel.Gltf;
using Luxel.RenderGraph;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using Luxel.Controls;
using Luxel.UI.Styling;
using static Luxel.Controls.Kit;
using static Luxel.UI.Decl;

namespace Luxel.Samples;

/// <summary>
/// Sample 82: 3D scene (BoxAnimated.glb) + 2D UI (AnimationController) を同フレームで合成 → PNG。
/// UI 背景色をキーに透過して 3D が見えるようにする (compute_ui_over_3d shader)。
/// </summary>
public static class Sample82Composite3DAndUI
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OverlayArgs
    {
        public uint BaseIdx, UiIdx, DstIdx;
        public uint Width, Height;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint W = 480, H = 320;
        Console.WriteLine("=== Sample 82: 3D + 2D UI 透過合成 → PNG ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // ==== 3D scene (Box.gltf) ====
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples", "BoxAnimated.glb"),
            Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples", "BoxAnimated.glb"),
        };
        string? gltfPath = candidates.FirstOrDefault(File.Exists);
        if (gltfPath is null) { Console.Error.WriteLine("FAILED: no gltf"); return 1; }

        var doc = new GltfLoader().LoadAsync(gltfPath).GetAwaiter().GetResult();
        foreach (var m in doc.Materials) m.BaseColor = new Vector4(0.86f, 0.30f, 0.30f, 1f);

        var world = new Luxel.Ecs.World();
        using var assets = SceneBuilder.Build(world, doc, device);
        var player = new SceneAnimationPlayer(world, assets, doc.Animations[0]);
        player.Sample(doc.Animations[0].Duration * 0.5f);
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
        using var ext = new SceneRenderExtractor(world, assets);
        ext.Extract();

        // ==== 2D UI (AnimationController) ====
        using var raster = new Rasterizer2D(device);
        using var canvas = new RetainedCanvas(raster);
        using var font = VectorFont.LoadSystem();

        uint blue = Color2D.Rgba(60, 120, 210), blueHi = Color2D.Rgba(95, 160, 245);

        var time = new Luxel.UI.Signal<int>(1250);
        var speed = new Luxel.UI.Signal<int>(100);
        var playing = new Luxel.UI.Signal<int>(1);

        // UI: 下側 40% を Border でパネル化 (上側の 60% は panel 色 のまま = 透過して 3D 表示)
        // レイアウト: 3D を上に見せるため、UI は下部に集中
        // UI 側は Rasterizer2D.Render(transparent:true) で背景 alpha=0 になる。
        // 外側 Border 不要 — 下 40% だけを実際のパネルで覆う。上 60% は完全透過で 3D が見える。
        var uiHost = new UiHost(canvas, font, W, H);
        uiHost.SetRoot(
            Grid(columns: [1], rows: [GridLength.Star(3), GridLength.Star(2)])
            [
                Border(background: Color2D.Rgba(30, 33, 40, 255), padding: new Thickness(12),
                       parts: [P.Grid.Row(1)])
                [
                    Grid(columns: [1, 1], rows: [GridLength.Px(30), GridLength.Star(1)])
                    [
                        Text($"time {time} speed {speed} playing {playing}", 16, color: Color2D.White,
                            parts: [P.Grid.Row(0), P.Grid.ColumnSpan(2)]),
                        Button(_ => playing.Value = 1 - playing.Value, "Play/Pause",
                            background: blue, foreground: Color2D.White,
                            parts: [S.On(WidgetState.Hover, S.Bg(blueHi)), P.Grid.Column(0), P.Grid.Row(1)]),
                        Button(_ => Stop, "Stop"(playing, time),
                            background: Color2D.Rgba(100, 116, 139), foreground: Color2D.White,
                            parts: [P.Grid.Column(1), P.Grid.Row(1)])
                    ]
                ]
            ]
        );

        // UI 描画
        using GpuBuffer uiBuf = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
        using (var cmd = device.MainQueue.StartCommandRecording())
        {
            canvas.Render(cmd, Camera2D.Pixels, W, H, uiBuf, transparent: true);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        // ==== 3D 描画 → base RT → base buffer ====
        using GpuTexture color = device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(W, H);
        using GpuBuffer baseBuf = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
        using GpuBuffer finalBuf = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);

        var raster3d = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster3d.DepthTest = true; raster3d.DepthWrite = true;
        using GpuPipeline pipeline3d = device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_lite"), raster3d);
        using GpuPipeline pipelineOverlay = device.CreateComputePipeline(GpuShaderCode.Load("compute_ui_over_3d"));

        var view = Matrix4x4.CreateLookAt(new Vector3(2.2f, 1.6f, -2.5f), new Vector3(0, 0.4f, 0), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, W / (float)H, 0.1f, 100f);
        var viewProj = view * proj;

        using var rg = new Luxel.RenderGraph.RenderGraph(device);
        var mesh = assets.Meshes[0];
        BufferHandle hVerts = rg.ImportBuffer(mesh.VertexBuffer, "verts");
        BufferHandle hInsts = rg.ImportBuffer(ext.InstanceBuffer, "insts");
        BufferHandle hBase = rg.ImportBuffer(baseBuf, "base");
        BufferHandle hUi = rg.ImportBuffer(uiBuf, "ui");
        BufferHandle hFinal = rg.ImportBuffer(finalBuf, "final");

        rg.AddPass("Render3D", PassQueue.Graphics)
          .Read(hVerts).Read(hInsts).Write(hInsts)
          .Execute(ctx =>
          {
              var args = new DrawArgs
              {
                  ViewProj = Matrix4x4.Transpose(viewProj),
                  VertexBufIndex = ctx.BindlessIndex(hVerts),
                  InstanceBufIndex = ctx.BindlessIndex(hInsts),
              };
              ctx.Cmd.BeginRendering(color, depth, 0.10f, 0.15f, 0.25f, 1f, 1f)
                     .SetGraphicsPipeline(pipeline3d)
                     .SetRootArguments(args)
                     .Draw((uint)mesh.VertexCount, (uint)ext.InstanceCount)
                     .EndRendering();
              ctx.Cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                     .CopyTextureToBuffer(color, baseBuf);
          });

        rg.AddPass("Overlay", PassQueue.Compute)
          .Read(hBase).Read(hUi).Write(hFinal)
          .Execute(ctx =>
          {
              var args = new OverlayArgs
              {
                  BaseIdx = ctx.BindlessIndex(hBase),
                  UiIdx = ctx.BindlessIndex(hUi),
                  DstIdx = ctx.BindlessIndex(hFinal),
                  Width = W, Height = H,
              };
              ctx.Cmd.SetComputePipeline(pipelineOverlay)
                     .SetRootArguments(args)
                     .Dispatch((W + 7) / 8, (H + 7) / 8, 1);
          });

        using (var cmd = device.MainQueue.StartCommandRecording())
        {
            rg.Execute(cmd);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        var px = finalBuf.Span<byte>((int)(W * H * 4));
        string png = Path.Combine(AppContext.BaseDirectory, "composite_3d_ui.png");
        PngWriter.WriteRgba(png, (int)W, (int)H, px);
        Console.WriteLine($"  PNG: {png}");

        // 各領域を検証: 上部 (3D エリア) は 3D 色 (赤系), 下部 (UI パネル) はダーク背景
        // 3D 領域 (上部 60%) に box の赤系 (>150,<80,<80) が現れるかカウント
        int redPix = 0;
        int uiPix = 0;
        int upperH = (int)(H * 3 / 5);
        int lowerH = (int)(H * 2 / 5);
        for (int y = 0; y < upperH; y++)
            for (int x = 0; x < (int)W; x++)
            {
                int i = (y * (int)W + x) * 4;
                if (px[i] > 150 && px[i + 1] < 100 && px[i + 2] < 100) redPix++;
            }
        for (int y = upperH; y < (int)H; y++)
            for (int x = 0; x < (int)W; x++)
            {
                int i = (y * (int)W + x) * 4;
                // UI パネル背景 dark (30,33,40) 近辺
                if (px[i] < 50 && px[i + 1] < 55 && px[i + 2] < 60) uiPix++;
            }
        Console.WriteLine($"  3D 赤ピクセル (上部): {redPix}");
        Console.WriteLine($"  UI dark ピクセル (下部): {uiPix}");

        bool has3D = redPix > 200;      // box が最低これくらいのサイズで見える
        bool hasUI = uiPix > lowerH * (int)W / 4;  // UI 領域の 1/4 以上が dark
        bool ok = has3D && hasUI;
        Console.WriteLine(ok ? "OK: DEMO-M7 (3D + 2D UI 透過合成) 動作" : "FAILED");
        return ok ? 0 : 1;
    }

    private static void Stop(Luxel.UI.Signal<int> playing, Luxel.UI.Signal<int> time)
    { playing.Value = 0; time.Value = 0; }

    private static (byte r, byte g, byte b) Rgb(ReadOnlySpan<byte> px, int w, int x, int y)
    { int i = (y * w + x) * 4; return (px[i], px[i + 1], px[i + 2]); }
}
