using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Gltf;
using Luxel.RenderGraph;
using Luxel.Assets;
using Luxel.AssetRuntime;

namespace Luxel.Samples;

/// <summary>
/// Sample 76: Box.gltf を Scene → ECS → 実描画 → PNG 出力 → ピクセル検証。
/// scene_pbr_lite shader + SceneRenderExtractor の E2E 動作実証。
/// </summary>
public static class Sample76GltfBoxRender
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint W = 256, H = 256;
        Console.WriteLine("=== Sample 76: Box.gltf 実描画 + PNG ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // Khronos sample 取得
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples", "Box.gltf"),
            Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples", "Box.gltf"),
        };
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is null) { Console.Error.WriteLine("FAILED: Box.gltf not found"); return 1; }

        // Scene → ECS
        var doc = new GltfLoader().LoadAsync(path).GetAwaiter().GetResult();
        // glTF の Box.gltf は white material → 赤に上書きして検証しやすく
        if (doc.Materials.Count > 0) doc.Materials[0].BaseColor = new Vector4(0.86f, 0.30f, 0.30f, 1f);

        var world = new Luxel.Ecs.World();
        using var assets = SceneBuilder.Build(world, doc, device);
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);

        // Extract
        using var extractor = new SceneRenderExtractor(world, assets);
        extractor.Extract();
        Console.WriteLine($"  instances: {extractor.InstanceCount}, drawCalls: {extractor.DrawList.Count}");

        // RT/Depth/readback
        using GpuTexture color = device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(W, H);
        using GpuBuffer readback = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);

        // Pipeline
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true;
        raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_lite"), raster);

        // Camera (Box は 1x1x1 unit、適度な距離から見る)
        var view = Matrix4x4.CreateLookAt(new Vector3(2.0f, 1.5f, -2.5f), Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        var viewProj = view * proj;

        using var rg = new Luxel.RenderGraph.RenderGraph(device);

        // SceneAssets.Meshes[0] (Box は 1 mesh) を取り出す
        if (assets.Meshes.Count == 0) { Console.Error.WriteLine("FAILED: no mesh"); return 1; }
        var mesh = assets.Meshes[0];
        BufferHandle hVerts = rg.ImportBuffer(mesh.VertexBuffer, "verts");
        BufferHandle hInsts = rg.ImportBuffer(extractor.InstanceBuffer, "insts");

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
              ctx.Cmd.BeginRendering(color, depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                     .SetGraphicsPipeline(pipeline)
                     .SetRootArguments(args)
                     .Draw((uint)mesh.VertexCount, (uint)extractor.InstanceCount)
                     .EndRendering();
          });

        using (var cmd = device.MainQueue.StartCommandRecording())
        {
            rg.Execute(cmd);
            cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy)
               .CopyTextureToBuffer(color, readback);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        // PNG 出力 + 検証
        var px = readback.Span<byte>((int)(W * H * 4));
        string png = Path.Combine(AppContext.BaseDirectory, "gltf_box.png");
        PngWriter.WriteRgba(png, (int)W, (int)H, px);
        Console.WriteLine($"  PNG: {png}");

        int total = (int)(W * H);
        int colored = 0;
        for (int i = 0; i < total; i++)
        {
            byte r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
            // 背景 (13,15,23) との差で判定
            if (Math.Abs(r - 13) + Math.Abs(g - 15) + Math.Abs(b - 23) > 30) colored++;
        }
        int pct = colored * 100 / total;
        Console.WriteLine($"  非背景ピクセル: {colored}/{total} ({pct}%)");

        bool ok = extractor.InstanceCount >= 1 && colored > total / 50 && colored < total * 4 / 5;
        Console.WriteLine(ok ? "OK: DEMO-M4 (Box.gltf 実描画 + PNG) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
