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
/// Sample 84: fragment URI で mesh/index/texture を取得しつつ、material buffer だけを
/// <see cref="RenderBuffer{MaterialGpuData}"/> で Publish して CPU 動的更新するデモ (RGRE-M2c 拡張)。
/// 静的 asset (mesh/tex) は <c>#mesh/0/vertex</c> 等の fragment 経由、動的 buffer (material/instance) は Publish。
/// </summary>
public static class Sample84MaterialEdit
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
        Console.WriteLine("=== Sample 84: MaterialBuffer 動的編集 (fragment URI ロード + Publish 混在) ===");
        using GpuDevice device = createDevice();

        var assetRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples");
        if (!Directory.Exists(assetRoot)) assetRoot = Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples");

        var world = new Luxel.Ecs.World();
        using var resources = new ResourceSystem(device, assetRoot: assetRoot);
        resources.AddService(world);
        resources.AddStep<GltfStep>();
        resources.AddStep<SceneAssetsStep>();
        resources.AddStep<GltfBufferStep>();
        resources.AddStep<MaterialTextureStep>();

        const string asset = "BoxTextured.glb";
        using var hVert = resources.Load<GpuBuffer>($"{asset}#mesh/0/vertex");
        using var hIndex = resources.Load<GpuBuffer>($"{asset}#mesh/0/index");
        using var hTex = resources.Load<GpuTexture>($"{asset}#material/0/baseColor");
        using var hAssets = resources.Load<SceneAssets>(asset);
        Task.WaitAll(hVert.Ready, hIndex.Ready, hTex.Ready, hAssets.Ready);
        resources.Pump();
        var assets = hAssets.Value;
        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
        var mesh = assets.Meshes[0];

        // 動的 material buffer は Publish で
        var matHandle = resources.PublishRenderBuffer<MaterialGpuData>("scene/materials", 1);
        var matBuf = matHandle.Value;
        int reloadedCount = 0;
        matHandle.Reloaded += () => reloadedCount++;

        uint texIdx = hTex.Value.BindlessIndex;
        uint sampIdx = assets.DefaultSampler!.BindlessIndex;

        void WriteMaterial(Vector4 baseColor)
        {
            matBuf[0] = new MaterialGpuData
            {
                BaseColor = baseColor, BaseColorTexIndex = texIdx, SamplerIndex = sampIdx,
                Flags = MaterialGpuData.FlagHasTexture,
            };
            matBuf.MarkDirty();
            resources.Pump();
        }

        Matrix4x4 worldMat = Matrix4x4.Identity;
        foreach (var e in assets.NodeEntities)
            if (e.HasComponent<AssetMeshRef>() && e.HasComponent<Luxel.Ecs.GlobalTransform>())
            { worldMat = e.GetComponent<Luxel.Ecs.GlobalTransform>().Matrix; break; }

        var instHandle = resources.PublishRenderBuffer<InstanceData>("scene/instances", 1);
        instHandle.Value[0] = new InstanceData { World = worldMat, MaterialIndex = 0 };
        instHandle.Value.MarkDirty();

        using GpuTexture color = device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(W, H);
        using GpuBuffer readback = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true; raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_tex"), raster);
        var view = Matrix4x4.CreateLookAt(new Vector3(2.0f, 1.5f, -2.5f), Vector3.Zero, Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        var viewProj = view * proj;

        (long RSum, long GSum, long BSum, int Count) RenderAndCount(int frameIndex)
        {
            using var rg = new Luxel.RenderGraph.RenderGraph(device);
            BufferHandle hgVerts = rg.ImportBuffer(hVert);
            BufferHandle hgIdx = rg.ImportBuffer(hIndex);
            BufferHandle hgInsts = rg.ImportRenderBuffer(instHandle.Value, "insts");
            BufferHandle hgMats = rg.ImportRenderBuffer(matBuf, "materials");
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
            var px = readback.Span<byte>((int)(W * H * 4)).ToArray();
            string png = Path.Combine(AppContext.BaseDirectory, $"material_edit_{frameIndex}.png");
            PngWriter.WriteRgba(png, (int)W, (int)H, px);
            long r = 0, g = 0, b = 0; int c = 0;
            for (int i = 0; i < W * H; i++)
            {
                byte pr = px[i * 4], pg = px[i * 4 + 1], pb = px[i * 4 + 2];
                if (Math.Abs(pr - 13) + Math.Abs(pg - 15) + Math.Abs(pb - 23) > 30)
                { r += pr; g += pg; b += pb; c++; }
            }
            Console.WriteLine($"  frame {frameIndex}: mean=({r/Math.Max(1,c)},{g/Math.Max(1,c)},{b/Math.Max(1,c)}), non-bg={c}, reloaded={reloadedCount}");
            return (r, g, b, c);
        }

        WriteMaterial(new Vector4(1f, 1f, 1f, 1f));
        var f0 = RenderAndCount(0);
        WriteMaterial(new Vector4(1f, 0.3f, 0.3f, 1f));
        var f1 = RenderAndCount(1);
        WriteMaterial(new Vector4(0.3f, 0.4f, 1f, 1f));
        var f2 = RenderAndCount(2);

        double avgR1 = f1.RSum / (double)Math.Max(1, f1.Count);
        double avgG1 = f1.GSum / (double)Math.Max(1, f1.Count);
        double avgB1 = f1.BSum / (double)Math.Max(1, f1.Count);
        double avgR2 = f2.RSum / (double)Math.Max(1, f2.Count);
        double avgG2 = f2.GSum / (double)Math.Max(1, f2.Count);
        double avgB2 = f2.BSum / (double)Math.Max(1, f2.Count);

        bool red1 = avgR1 > avgG1 + 10 && avgR1 > avgB1 + 10;
        bool blu2 = avgB2 > avgR2 + 10 && avgB2 > avgG2 + 10;
        // 3 回の WriteMaterial(白/赤/青) それぞれで Pump → Flush → Reloaded 発火
        bool events = reloadedCount == 3;
        Console.WriteLine($"  frame1 red-dominant: {red1}, frame2 blue-dominant: {blu2}, reloaded events: {reloadedCount} (期待 3)");
        bool ok = red1 && blu2 && events;
        Console.WriteLine(ok ? "OK: fragment ロード + Publish 混在で MaterialBuffer 動的更新" : "FAILED");
        return ok ? 0 : 1;
    }
}
