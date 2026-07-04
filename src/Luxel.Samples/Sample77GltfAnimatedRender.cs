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
/// Sample 77: BoxAnimated.glb を SceneAnimationPlayer で 3 時刻 sample → 各 PNG 出力 → 差分で
/// アニメーションが実描画に反映されていることを検証 (PNG 内容が時刻ごとに変わる)。
/// </summary>
public static class Sample77GltfAnimatedRender
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
        Console.WriteLine("=== Sample 77: BoxAnimated 実描画 + 時刻別 PNG ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples", "BoxAnimated.glb"),
            Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples", "BoxAnimated.glb"),
        };
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is null) { Console.Error.WriteLine("FAILED: BoxAnimated.glb not found"); return 1; }

        var doc = new GltfLoader().LoadAsync(path).GetAwaiter().GetResult();
        if (doc.Animations.Count == 0) { Console.Error.WriteLine("no anim"); return 1; }
        var anim = doc.Animations[0];

        // material 色を統一して見やすく
        foreach (var m in doc.Materials) m.BaseColor = new Vector4(0.30f, 0.70f, 0.40f, 1f);

        var world = new Luxel.Ecs.World();
        using var assets = SceneBuilder.Build(world, doc, device);
        var player = new SceneAnimationPlayer(world, assets, anim);

        // GPU resources
        using GpuTexture color = device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(W, H);
        using GpuBuffer readback = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true; raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_lite"), raster);

        var view = Matrix4x4.CreateLookAt(new Vector3(3.0f, 2.5f, -4.0f), new Vector3(0, 0.5f, 0), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        var viewProj = view * proj;

        float[] times = { 0f, anim.Duration * 0.33f, anim.Duration * 0.66f };
        byte[][] snapshots = new byte[3][];

        for (int frame = 0; frame < 3; frame++)
        {
            // Animation sample → transform 伝搬 → extract
            player.Sample(times[frame]);
            Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
            using var extractor = new SceneRenderExtractor(world, assets);
            extractor.Extract();

            // 全 mesh draw
            using var rg = new Luxel.RenderGraph.RenderGraph(device);
            BufferHandle hInsts = rg.ImportBuffer(extractor.InstanceBuffer, "insts");
            // 簡素化: 最初の mesh のみ描画 (全 entity が同一 mesh を共有する想定)。multi-mesh は将来課題
            var firstMesh = assets.Meshes[0];
            BufferHandle hVerts = rg.ImportBuffer(firstMesh.VertexBuffer, "verts");
            int instCount = extractor.InstanceCount;

            rg.AddPass("Mesh0", PassQueue.Graphics)
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
                         .Draw((uint)firstMesh.VertexCount, (uint)instCount)
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

            var px = readback.Span<byte>((int)(W * H * 4));
            snapshots[frame] = px.ToArray();
            string p = Path.Combine(AppContext.BaseDirectory, $"gltf_animated_{frame}.png");
            PngWriter.WriteRgba(p, (int)W, (int)H, px);
            Console.WriteLine($"  t={times[frame]:F2}s: PNG={Path.GetFileName(p)}");
        }

        // 3 frame 間の差分 (sum of abs)
        long diff01 = 0, diff12 = 0;
        for (int i = 0; i < snapshots[0].Length; i++)
        {
            diff01 += Math.Abs(snapshots[0][i] - snapshots[1][i]);
            diff12 += Math.Abs(snapshots[1][i] - snapshots[2][i]);
        }
        Console.WriteLine($"  diff frame0-1: {diff01}, frame1-2: {diff12}");

        bool ok = diff01 > 1000 || diff12 > 1000;  // 何らかのアニメ変化が見える
        Console.WriteLine(ok ? "OK: DEMO-M4 (BoxAnimated 時刻別実描画 + アニメ反映) 動作" : "FAILED: no animation visible");
        return ok ? 0 : 1;
    }
}
