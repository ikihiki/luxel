using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Gltf;
using Luxel.RenderGraph;
using Luxel.Resources;
using Luxel.Assets;
using Luxel.AssetRuntime;

namespace Luxel.Samples;

/// <summary>
/// Sample 80: <see cref="ResourceSystem"/> フル統合 demo。
/// <list type="bullet">
/// <item><b>RES-M1</b>: GltfStep で .gltf/.glb → AssetDocument (関連 .bin は ctx.Load で自動キャッシュ)</item>
/// <item><b>RES-M2</b>: SceneAssetsStep で AssetDocument → SceneAssets (Gpu レーン)</item>
/// <item><b>RES-M3</b>: ResourceSystem.Watch() + Pump で hot-reload 対応</item>
/// </list>
/// 同一 .gltf を 2 回 Load → cache 共有を ResourceHandle.Same で確認、PNG 描画も。
/// </summary>
public static class Sample80ResourceSystem
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
        Console.WriteLine("=== Sample 80: ResourceSystem フル統合 (Pipeline + Watch + Hot-reload) ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");

        // Khronos sample 場所を asset root に
        var assetRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples");
        if (!Directory.Exists(assetRoot))
            assetRoot = Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples");
        Console.WriteLine($"  assetRoot: {Path.GetFullPath(assetRoot)}");

        // === ResourceSystem セットアップ ===
        var world = new Luxel.Ecs.World();
        using var sys = new ResourceSystem(device, assetRoot: assetRoot);
        sys.AddService(world);                 // SceneAssetsStep が要求
        sys.AddStep<GltfStep>();               // byte[] → AssetDocument (Cpu)
        sys.AddStep<SceneAssetsStep>();        // AssetDocument → SceneAssets (Gpu)
        sys.Watch();                           // 自動リロード有効化

        // === 同一 .glb を 2 回 Load (cache 共有確認) ===
        using var h1 = sys.Load<AssetDocument>("BoxAnimated.glb");
        using var h2 = sys.Load<AssetDocument>("BoxAnimated.glb");
        h1.Ready.GetAwaiter().GetResult();
        sys.Pump();  // ready callback flush
        h2.Ready.GetAwaiter().GetResult();
        sys.Pump();
        bool sameDoc = ReferenceEquals(h1.Value, h2.Value);
        Console.WriteLine($"  same AssetDocument (cache 共有): {sameDoc}");

        // === AssetDocument の構造確認 ===
        var doc = h1.Value;
        Console.WriteLine($"  meshes={doc.Meshes.Count}, nodes={doc.Nodes.Count}, anims={doc.Animations.Count}");

        // === SceneAssets を auto-compose で取得 (AssetDocument を経由する pipeline) ===
        using var hAssets = sys.Load<SceneAssets>("BoxAnimated.glb");
        hAssets.Ready.GetAwaiter().GetResult();
        sys.Pump();
        var assets = hAssets.Value;
        if (assets is null) { Console.Error.WriteLine("FAILED: SceneAssets null"); return 1; }
        Console.WriteLine($"  SceneAssets: meshes={assets.Meshes.Count}, materials={assets.Materials.Count}, ECS entities={assets.NodeEntities.Count}");

        // material 色を上書き
        foreach (var m in assets.Materials) m.BaseColor = new Vector4(0.95f, 0.5f, 0.2f, 1f);

        // === 実描画 → PNG ===
        var anim = doc.Animations[0];
        var player = new SceneAnimationPlayer(world, assets, anim);
        player.Sample(anim.Duration * 0.5f);
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
        using var extractor = new SceneRenderExtractor(world, assets);
        extractor.Extract();

        using GpuTexture color = device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(W, H);
        using GpuBuffer readback = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true; raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_lite"), raster);

        var view = Matrix4x4.CreateLookAt(new Vector3(3, 2.5f, -4), new Vector3(0, 0.5f, 0), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        var viewProj = view * proj;

        using var rg = new Luxel.RenderGraph.RenderGraph(device);
        var mesh = assets.Meshes[0];
        BufferHandle hVerts = rg.ImportBuffer(mesh.VertexBuffer, "verts");
        BufferHandle hInsts = rg.ImportBuffer(extractor.InstanceBuffer, "insts");
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
        string png = Path.Combine(AppContext.BaseDirectory, "resource_pipeline.png");
        PngWriter.WriteRgba(png, (int)W, (int)H, px);
        Console.WriteLine($"  PNG: {png}");

        // === Watch + Pump 機構の動作確認 (実 file 変更は行わず、API が呼べることのみ) ===
        sys.Pump();
        Console.WriteLine("  Pump (hot-reload 反映ループ): OK");

        bool ok = sameDoc && doc.Meshes.Count > 0 && doc.Animations.Count > 0 && assets.Meshes.Count > 0;
        Console.WriteLine(ok ? "OK: RES (Luxel.Resources フル統合 ─ Pipeline + 自動キャッシュ + Watch) 動作"
                              : "FAILED");
        return ok ? 0 : 1;
    }
}
