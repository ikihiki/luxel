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
/// Sample 86: RiggedSimple.glb (親子ボーン + skinning アニメーション) を汎用 Drawable API で描画。
/// Skinned entity は <see cref="DrawSkinning"/> component を持ち、<see cref="DrawableCollector"/> が
/// 自動的に joint buffer を rg.ImportBuffer で取り込む。Sample 86 は scene_pbr_skinned pipeline を選択。
/// </summary>
public static class Sample86SkinnedDrawable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint IndexBufIndex;
        public uint InstanceBufIndex;
        public uint MaterialBufIndex;
        public uint JointBufIndex;
        public uint Pad0, Pad1, Pad2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InstanceData
    {
        public Matrix4x4 World;
        public uint MaterialIndex;
        public uint _pad0, _pad1, _pad2;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint W = 320, H = 320;
        Console.WriteLine("=== Sample 86: RiggedSimple.glb via 汎用 Drawable + DrawSkinning (DRAW-M7) ===");
        using GpuDevice device = createDevice();

        var assetRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples");
        if (!Directory.Exists(assetRoot)) assetRoot = Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples");

        var world = new Luxel.Ecs.World();
        using var resources = new ResourceSystem(device, assetRoot: assetRoot);
        resources.AddService(world);
        resources.AddStep<GltfStep>();
        resources.AddStep<SceneAssetsStep>();
        resources.AddStep<GltfBufferStep>();

        const string asset = "RiggedSimple.glb";
        using var hAssets = resources.Load<SceneAssets>(asset);
        hAssets.Ready.GetAwaiter().GetResult();
        resources.Pump();
        var assets = hAssets.Value;
        var doc = resources.Load<AssetDocument>(asset).Value;
        Console.WriteLine($"  meshes={assets.Meshes.Count}, skins={assets.Skins.Count}, animations={doc.Animations.Count}, nodes={doc.Nodes.Count}");

        // baseColor を目立つ色に (RiggedSimple のデフォルトは白 → 背景と紛れる)
        var hMats = resources.Load<GpuBuffer>($"{asset}#materials");
        hMats.Ready.GetAwaiter().GetResult();
        resources.Pump();
        var matSpan = hMats.Value.Span<MaterialGpuData>(assets.Materials.Count);
        for (int i = 0; i < matSpan.Length; i++)
        {
            matSpan[i] = new MaterialGpuData
            {
                BaseColor = new Vector4(0.30f, 0.60f, 0.95f, 1f),
                BaseColorTexIndex = 0, SamplerIndex = 0, Flags = 0,
            };
        }

        // === Drawable component を attach (内部で Ready 待ち) ===
        DrawableAttacher.AttachMesh(world, assets, resources, asset);

        // === Sample 側で DrawInstance + DrawSkinning を entity に付与 ===
        var instanceBuffers = new Dictionary<Entity, RenderBuffer<InstanceData>>();
        var jointBuffers = new Dictionary<Entity, RenderBuffer<Matrix4x4>>();
        int slot = 0;
        foreach (var e in assets.NodeEntities)
        {
            if (!e.HasComponent<DrawMesh>()) continue;
            var ib = resources.PublishRenderBuffer<InstanceData>($"scene/instance/{slot}", 1).Value;
            instanceBuffers[e] = ib;
            e.AddComponent(new DrawInstance { Buffer = ib.Buffer, InstanceCount = 1 });

            if (e.HasComponent<AssetSkinRef>())
            {
                int skinIdx = e.GetComponent<AssetSkinRef>().Index;
                int jointCount = assets.Skins[skinIdx].JointNodeIndices.Length;
                var jb = resources.PublishRenderBuffer<Matrix4x4>($"scene/joints/{slot}", jointCount).Value;
                jointBuffers[e] = jb;
                e.AddComponent(new DrawSkinning { JointBuffer = jb.Buffer, JointCount = jointCount });
            }
            slot++;
        }
        int skinnedCount = 0;
        foreach (var (_, _) in jointBuffers) skinnedCount++;
        Console.WriteLine($"  drawable entities: {instanceBuffers.Count}, of which skinned: {skinnedCount}");

        var anim = doc.Animations[0];
        var player = new SceneAnimationPlayer(world, assets, anim);
        Console.WriteLine($"  animation '{anim.Name}': duration={anim.Duration:F2}s, channels={anim.Channels.Count}");

        // === Render pipeline (skinning shader) ===
        using GpuTexture color = device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(W, H);
        using GpuBuffer readback = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true; raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_skinned"), raster);
        var view = Matrix4x4.CreateLookAt(new Vector3(6f, 3f, -6f), new Vector3(0, 1f, 0), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, W / (float)H, 0.1f, 100f);
        var viewProj = view * proj;

        byte[] RenderFrame(int frameIdx, float t)
        {
            player.Sample(t);
            Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
            SkinningSystem.Run(world, assets);

            // Instance buffer 更新
            world.Query<DrawInstance, DrawMaterial, Luxel.Ecs.GlobalTransform>()
                 .ForEachEntity((ref DrawInstance _, ref DrawMaterial dm, ref Luxel.Ecs.GlobalTransform gt, Entity e) =>
            {
                var rb = instanceBuffers[e];
                rb[0] = new InstanceData { World = gt.Matrix, MaterialIndex = (uint)dm.MaterialIndex };
                rb.MarkDirty();
            });
            // Joint buffer 更新 (SkinningSystem が JointMatrices component に書き込んだ値を RenderBuffer に転写)
            world.Query<DrawSkinning>().ForEachEntity((ref DrawSkinning _, Entity e) =>
            {
                if (!e.HasComponent<JointMatrices>()) return;
                var mats = e.GetComponent<JointMatrices>().Matrices;
                var jb = jointBuffers[e];
                for (int i = 0; i < mats.Length; i++) jb[i] = mats[i];
                jb.MarkDirty();
            });
            resources.Pump();

            using var rg = new Luxel.RenderGraph.RenderGraph(device);
            var items = DrawableCollector.Collect(world, rg);

            var pb = rg.AddPass("Render3D", PassQueue.Graphics);
            foreach (var it in items)
            {
                pb.Read(it.Vertex).Read(it.Index).Read(it.Instance).Read(it.Material);
                if (it.HasSkinning) pb.Read(it.Joint);
            }
            pb.Write(items[0].Instance);
            pb.Execute(ctx =>
            {
                ctx.Cmd.BeginRendering(color, depth, 0.08f, 0.10f, 0.14f, 1f, 1f)
                       .SetGraphicsPipeline(pipeline);
                foreach (var it in items)
                {
                    if (!it.HasSkinning) continue;   // scene_pbr_skinned は joint 必須
                    var args = new DrawArgs
                    {
                        ViewProj = Matrix4x4.Transpose(viewProj),
                        VertexBufIndex = ctx.BindlessIndex(it.Vertex),
                        IndexBufIndex = ctx.BindlessIndex(it.Index),
                        InstanceBufIndex = ctx.BindlessIndex(it.Instance),
                        MaterialBufIndex = ctx.BindlessIndex(it.Material),
                        JointBufIndex = ctx.BindlessIndex(it.Joint),
                    };
                    ctx.Cmd.SetRootArguments(args).Draw((uint)it.IndexCount, (uint)it.InstanceCount);
                }
                ctx.Cmd.EndRendering();
            });

            using (var cmd = device.MainQueue.StartCommandRecording())
            {
                rg.Execute(cmd);
                cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy).CopyTextureToBuffer(color, readback);
                cmd.Finish();
                device.MainQueue.SubmitAndWait(cmd);
            }

            var px = readback.Span<byte>((int)(W * H * 4)).ToArray();
            string png = Path.Combine(AppContext.BaseDirectory, $"skinned_drawable_{frameIdx}.png");
            PngWriter.WriteRgba(png, (int)W, (int)H, px);
            int meshPix = 0;
            for (int i = 0; i < W * H; i++)
            {
                byte r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
                if (Math.Abs(r - 21) + Math.Abs(g - 25) + Math.Abs(b - 35) > 30) meshPix++;
            }
            var skin = assets.Skins[0];
            var b1Entity = assets.NodeEntities[skin.JointNodeIndices[1]];
            var b1M = b1Entity.GetComponent<Luxel.Ecs.GlobalTransform>().Matrix;
            Console.WriteLine($"  frame {frameIdx} (t={t:F2}): mesh_pix={meshPix}, bone[1] col1=({b1M.M21:F2},{b1M.M22:F2},{b1M.M23:F2})");
            return px;
        }

        var f0 = RenderFrame(0, 0f);
        var f1 = RenderFrame(1, anim.Duration * 0.33f);
        var f2 = RenderFrame(2, anim.Duration * 0.66f);
        long DiffL1(byte[] a, byte[] b) { long d = 0; for (int i = 0; i < a.Length; i++) d += Math.Abs(a[i] - b[i]); return d; }
        long d01 = DiffL1(f0, f1), d12 = DiffL1(f1, f2), d02 = DiffL1(f0, f2);
        Console.WriteLine($"  frame diff: 0↔1={d01}, 1↔2={d12}, 0↔2={d02}");

        bool ok = d01 > 5000 && d12 > 5000 && d02 > 5000;
        Console.WriteLine(ok ? "OK: DrawSkinning + scene_pbr_skinned で skinning アニメが描画に反映"
                              : "FAILED");
        return ok ? 0 : 1;
    }
}
