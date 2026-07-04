using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Gltf;
using Luxel.RenderGraph;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Luxel.Scene.UI;

namespace Luxel.Samples;

/// <summary>
/// Sample 78: BoxAnimated + PlaybackState (UI 制御) で 3 つの状態を実描画 + PNG。
/// UI からの操作 (IsPlaying / CurrentTime / Speed / Looped) が実 GPU 描画に反映されるか検証。
/// </summary>
public static class Sample78AnimationUI
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
        Console.WriteLine("=== Sample 78: Animation UI 操作 → 実描画 PNG ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples", "BoxAnimated.glb"),
            Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples", "BoxAnimated.glb"),
        };
        string? path = candidates.FirstOrDefault(File.Exists);
        if (path is null) { Console.Error.WriteLine("FAILED: BoxAnimated.glb"); return 1; }

        var doc = new GltfLoader().LoadAsync(path).GetAwaiter().GetResult();
        foreach (var m in doc.Materials) m.BaseColor = new Vector4(0.30f, 0.55f, 0.90f, 1f);
        var anim = doc.Animations[0];

        var world = new Luxel.Ecs.World();
        using var assets = SceneBuilder.Build(world, doc, device);
        var player = new SceneAnimationPlayer(world, assets, anim);

        // PlaybackState (UI が操作する想定)
        var state = new PlaybackState { };
        state.Duration.Value = anim.Duration;
        state.IsPlaying.Value = true;
        state.Speed.Value = 1.0f;
        state.Looped.Value = true;
        Console.WriteLine($"  anim duration: {anim.Duration:F2}s");

        // GPU setup
        using GpuTexture color = device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(W, H);
        using GpuBuffer readback = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true; raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_lite"), raster);

        var view = Matrix4x4.CreateLookAt(new Vector3(3.0f, 2.5f, -4.0f), new Vector3(0, 0.5f, 0), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        var viewProj = view * proj;

        // 3 ステップで UI 操作を模擬:
        //   step 0: t=0s で開始 (Play 直後)
        //   step 1: Tick 1s で playing 自動進行
        //   step 2: Speed 2x で Tick 0.5s (Tick 1s 相当)
        byte[][] snaps = new byte[3][];
        string[] labels = { "t=0", "t=1s playing", "t=2s after speed-2x" };

        for (int i = 0; i < 3; i++)
        {
            if (i == 1) state.Tick(1.0f);
            if (i == 2) { state.Speed.Value = 2.0f; state.Tick(0.5f); }
            Console.WriteLine($"  step{i}: time={state.CurrentTime.Value:F2}s, playing={state.IsPlaying.Value}, speed={state.Speed.Value}");

            // PlaybackState.CurrentTime → Animation Player に渡す (Signal → 値)
            player.Sample(state.CurrentTime.Value);
            Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
            using var extractor = new SceneRenderExtractor(world, assets);
            extractor.Extract();

            using var rg = new Luxel.RenderGraph.RenderGraph(device);
            BufferHandle hInsts = rg.ImportBuffer(extractor.InstanceBuffer, "insts");
            var mesh = assets.Meshes[0];
            BufferHandle hVerts = rg.ImportBuffer(mesh.VertexBuffer, "verts");

            rg.AddPass("Mesh", PassQueue.Graphics)
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
            var px = readback.Span<byte>((int)(W * H * 4));
            snaps[i] = px.ToArray();
            string png = Path.Combine(AppContext.BaseDirectory, $"anim_ui_{i}.png");
            PngWriter.WriteRgba(png, (int)W, (int)H, px);
            Console.WriteLine($"    PNG: {Path.GetFileName(png)} ({labels[i]})");
        }

        long diff01 = 0, diff12 = 0;
        for (int i = 0; i < snaps[0].Length; i++)
        {
            diff01 += Math.Abs(snaps[0][i] - snaps[1][i]);
            diff12 += Math.Abs(snaps[1][i] - snaps[2][i]);
        }
        Console.WriteLine($"  step 0→1 diff: {diff01} (UI Tick で animation 進行)");
        Console.WriteLine($"  step 1→2 diff: {diff12} (UI Speed 2x で別位置)");

        bool ok = diff01 > 1000 && diff12 > 1000;
        Console.WriteLine(ok ? "OK: DEMO-M5 (PlaybackState UI 操作 → 実描画 反映) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
