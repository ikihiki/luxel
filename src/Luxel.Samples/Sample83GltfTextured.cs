using System.Numerics;
using System.Runtime.InteropServices;
using Luxel;
using Luxel.Ecs;
using Luxel.Gltf;
using Luxel.RenderGraph;
using Luxel.Resources;
using Luxel.Assets;
using Luxel.AssetRuntime;

namespace Luxel.Samples;

/// <summary>
/// Sample 83: BoxTextured.glb の GPU buffer/texture を全て <b>fragment 経由</b> で Resources から取得。
/// <c>resources.Load&lt;GpuBuffer&gt;("box.glb#mesh/0/vertex")</c> 形式で、Publish を経由せず自動的に
/// SceneAssets を裏で構築 → 内部 GpuBuffer を借用取得する (RGRE-M2c 拡張)。
/// </summary>
public static class Sample83GltfTextured
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint IndexBufIndex;
        public uint InstanceBufIndex;
        public uint MaterialBufIndex;
        public uint Pad0, Pad1, Pad2, Pad3;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct InstanceData
    {
        public Matrix4x4 World;
        public uint MaterialIndex;
        public uint _pad0, _pad1, _pad2;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint W = 256, H = 256;
        Console.WriteLine("=== Sample 83: fragment 経由 GPU buffer/texture ロード ===");
        using GpuDevice device = createDevice();

        // === Asset root と ResourceSystem ===
        var assetRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples");
        if (!Directory.Exists(assetRoot))
            assetRoot = Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples");

        var world = new Luxel.Ecs.World();
        using var resources = new ResourceSystem(device, assetRoot: assetRoot);
        resources.AddService(world);
        resources.AddStep<GltfStep>();               // byte[] → AssetDocument
        resources.AddStep<SceneAssetsStep>();        // AssetDocument → SceneAssets
        resources.AddStep<GltfBufferStep>();         // SceneAssets → GpuBuffer (fragment 分岐)
        resources.AddStep<MaterialTextureStep>();    // SceneAssets → GpuTexture

        // === 全 GPU buffer/texture を fragment 経由で取得 ===
        // SceneAssets は 1 度だけ構築され、以降 fragment 亜種は借用で共有
        const string asset = "BoxTextured.glb";
        using var hVert = resources.Load<GpuBuffer>($"{asset}#mesh/0/vertex");
        using var hIndex = resources.Load<GpuBuffer>($"{asset}#mesh/0/index");
        using var hMats = resources.Load<GpuBuffer>($"{asset}#materials");
        using var hTex = resources.Load<GpuTexture>($"{asset}#material/0/baseColor");
        Task.WaitAll(hVert.Ready, hIndex.Ready, hMats.Ready, hTex.Ready);
        resources.Pump();

        Console.WriteLine($"  loaded: vertex={hVert.Value.BindlessIndex}, index={hIndex.Value.BindlessIndex}, " +
                          $"mats={hMats.Value.BindlessIndex}, tex={hTex.Value.BindlessIndex}");

        // === Scene 構造 (world matrix, mesh vertexCount 等) を SceneAssets 経由で参照 ===
        using var hAssets = resources.Load<SceneAssets>(asset);
        hAssets.Ready.GetAwaiter().GetResult();
        resources.Pump();
        var assets = hAssets.Value;
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
        var mesh = assets.Meshes[0];

        // === Instance を組み立て ===
        Matrix4x4 worldMat = Matrix4x4.Identity;
        foreach (var e in assets.NodeEntities)
        {
            if (e.HasComponent<AssetMeshRef>() && e.HasComponent<Luxel.Ecs.GlobalTransform>())
            { worldMat = e.GetComponent<Luxel.Ecs.GlobalTransform>().Matrix; break; }
        }
        var instHandle = resources.PublishRenderBuffer<InstanceData>("scene/instances", 1);
        instHandle.Value[0] = new InstanceData { World = worldMat, MaterialIndex = 0 };
        instHandle.Value.MarkDirty();
        resources.Pump();

        // === Render pass ===
        using GpuTexture color = device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(W, H);
        using GpuBuffer readback = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true; raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_tex"), raster);

        var view = Matrix4x4.CreateLookAt(new Vector3(2.0f, 1.5f, -2.5f), Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        var viewProj = view * proj;

        using var rg = new Luxel.RenderGraph.RenderGraph(device);
        BufferHandle hgVerts = rg.ImportBuffer(hVert);
        BufferHandle hgIdx = rg.ImportBuffer(hIndex);
        BufferHandle hgInsts = rg.ImportRenderBuffer(instHandle.Value, "insts");
        BufferHandle hgMats = rg.ImportBuffer(hMats);

        rg.AddPass("Render3D", PassQueue.Graphics)
          .Read(hgVerts).Read(hgIdx).Read(hgInsts).Read(hgMats).Write(hgInsts)
          .Execute(ctx =>
          {
              var args = new DrawArgs
              {
                  ViewProj = Matrix4x4.Transpose(viewProj),
                  VertexBufIndex = ctx.BindlessIndex(hgVerts),
                  IndexBufIndex = ctx.BindlessIndex(hgIdx),
                  InstanceBufIndex = ctx.BindlessIndex(hgInsts),
                  MaterialBufIndex = ctx.BindlessIndex(hgMats),
              };
              ctx.Cmd.BeginRendering(color, depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                     .SetGraphicsPipeline(pipeline)
                     .SetRootArguments(args)
                     .Draw((uint)mesh.IndexCount, 1u)
                     .EndRendering();
          });

        using (var cmd = device.MainQueue.StartCommandRecording())
        {
            rg.Execute(cmd);
            cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy).CopyTextureToBuffer(color, readback);
            cmd.Finish();
            device.MainQueue.SubmitAndWait(cmd);
        }

        var px = readback.Span<byte>((int)(W * H * 4));
        string png = Path.Combine(AppContext.BaseDirectory, "gltf_textured.png");
        PngWriter.WriteRgba(png, (int)W, (int)H, px);
        Console.WriteLine($"  PNG: {png}");

        int total = (int)(W * H);
        int coloredCount = 0;
        long redSum = 0, grnSum = 0, bluSum = 0;
        long redSq = 0, grnSq = 0, bluSq = 0;
        for (int i = 0; i < total; i++)
        {
            byte r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
            if (Math.Abs(r - 13) + Math.Abs(g - 15) + Math.Abs(b - 23) > 30)
            { coloredCount++; redSum += r; grnSum += g; bluSum += b; redSq += r * r; grnSq += g * g; bluSq += b * b; }
        }
        if (coloredCount == 0) { Console.WriteLine("FAILED: no colored"); return 1; }
        double meanR = redSum / (double)coloredCount, meanG = grnSum / (double)coloredCount, meanB = bluSum / (double)coloredCount;
        double variance = (redSq + grnSq + bluSq) / (double)coloredCount - (meanR * meanR + meanG * meanG + meanB * meanB);
        Console.WriteLine($"  non-bg pixel: {coloredCount}, mean=({meanR:F0},{meanG:F0},{meanB:F0}), variance={variance:F1}");

        bool ok = coloredCount > total / 50 && variance > 150;
        Console.WriteLine(ok ? "OK: fragment URI 経由の buffer/texture ロードで描画動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
