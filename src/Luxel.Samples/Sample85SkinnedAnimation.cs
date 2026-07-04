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
/// Sample 85: CesiumMilkTruck.glb を <b>汎用 Drawable component</b> (DrawMesh/DrawInstance/DrawMaterial)
/// + <see cref="DrawableAttacher"/> + <see cref="DrawableCollector"/> で描画する。
/// Sample 特有の component は 0、ECS query だけで per-frame 更新と RG 構築が完結する。
/// 親子関係: Yup2Zup → Cesium_Milk_Truck → { Node → Wheels, Node.001 → Wheels.001 }。
/// </summary>
public static class Sample85SkinnedAnimation
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
    public struct InstanceData
    {
        public Matrix4x4 World;
        public uint MaterialIndex;
        public uint _pad0, _pad1, _pad2;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        const uint W = 384, H = 256;
        Console.WriteLine("=== Sample 85: CesiumMilkTruck.glb via 汎用 Drawable components (DRAW-M1..M4) ===");
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

        const string asset = "CesiumMilkTruck.glb";
        using var hAssets = resources.Load<SceneAssets>(asset);
        hAssets.Ready.GetAwaiter().GetResult();
        resources.Pump();
        var assets = hAssets.Value;
        var doc = resources.Load<AssetDocument>(asset).Value;
        Console.WriteLine($"  meshes={assets.Meshes.Count}, materials={assets.Materials.Count}, animations={doc.Animations.Count}, nodes={doc.Nodes.Count}, ecsEntities={assets.NodeEntities.Count}");
        int meshRefCount = 0;
        foreach (var e in assets.NodeEntities) if (e.HasComponent<AssetMeshRef>()) meshRefCount++;
        Console.WriteLine($"  entities with AssetMeshRef: {meshRefCount}");

        // === DrawableAttacher で DrawMesh + DrawMaterial を自動 attach (内部で Ready 待ち) ===
        DrawableAttacher.AttachMesh(world, assets, resources, asset);

        // === Sample 側は DrawInstance だけを追加 (shader が使う InstanceData layout は sample が決める) ===
        // Entity → RenderBuffer を dict で紐付け ─ query 順は archetype 依存なので順序に依存しない形にする。
        var instanceBuffers = new Dictionary<Entity, RenderBuffer<InstanceData>>();
        int slot = 0;
        foreach (var e in assets.NodeEntities)
        {
            if (!e.HasComponent<DrawMesh>()) continue;
            var rb = resources.PublishRenderBuffer<InstanceData>($"scene/instance/{slot}", 1).Value;
            instanceBuffers[e] = rb;
            e.AddComponent(new DrawInstance { Buffer = rb.Buffer, InstanceCount = 1 });
            slot++;
        }
        Console.WriteLine($"  drawable entities (DrawMesh + DrawInstance + DrawMaterial): {instanceBuffers.Count}");

        // === animation player ===
        var anim = doc.Animations[0];
        var player = new SceneAnimationPlayer(world, assets, anim);
        Console.WriteLine($"  animation '{anim.Name}': duration={anim.Duration:F2}s");

        // === Render pipeline ===
        using GpuTexture color = device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
        using GpuTexture depth = device.CreateDepthTarget(W, H);
        using GpuBuffer readback = device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
        var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.DepthTest = true; raster.DepthWrite = true;
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_tex"), raster);
        var view = Matrix4x4.CreateLookAt(new Vector3(6f, 3f, -6f), new Vector3(0, 0.3f, 0), Vector3.UnitY);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, W / (float)H, 0.1f, 100f);
        var viewProj = view * proj;

        byte[] RenderFrame(int frameIdx, float t)
        {
            player.Sample(t);
            Luxel.AssetRuntime.TransformPropagateSystem.Run(world);

            // per-frame: DrawInstance + DrawMaterial + GlobalTransform を query して RenderBuffer を更新
            world.Query<DrawInstance, DrawMaterial, Luxel.Ecs.GlobalTransform>()
                 .ForEachEntity((ref DrawInstance _, ref DrawMaterial dm, ref Luxel.Ecs.GlobalTransform gt, Entity e) =>
            {
                var rb = instanceBuffers[e];
                rb[0] = new InstanceData { World = gt.Matrix, MaterialIndex = (uint)dm.MaterialIndex };
                rb.MarkDirty();
            });
            resources.Pump();

            // === RG 構築: DrawableCollector が query 走査 + ImportBuffer を代行 ===
            using var rg = new Luxel.RenderGraph.RenderGraph(device);
            var items = DrawableCollector.Collect(world, rg);
            if (items.Count == 0) throw new InvalidOperationException("no drawable");

            var pb = rg.AddPass("Render3D", PassQueue.Graphics);
            foreach (var it in items) pb.Read(it.Vertex).Read(it.Index).Read(it.Instance).Read(it.Material);
            pb.Write(items[0].Instance);
            pb.Execute(ctx =>
            {
                ctx.Cmd.BeginRendering(color, depth, 0.08f, 0.10f, 0.14f, 1f, 1f)
                       .SetGraphicsPipeline(pipeline);
                foreach (var it in items)
                {
                    var args = new DrawArgs
                    {
                        ViewProj = Matrix4x4.Transpose(viewProj),
                        VertexBufIndex = ctx.BindlessIndex(it.Vertex),
                        IndexBufIndex = ctx.BindlessIndex(it.Index),
                        InstanceBufIndex = ctx.BindlessIndex(it.Instance),
                        MaterialBufIndex = ctx.BindlessIndex(it.Material),
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
            string png = Path.Combine(AppContext.BaseDirectory, $"milktruck_frame_{frameIdx}.png");
            PngWriter.WriteRgba(png, (int)W, (int)H, px);
            int meshPix = 0;
            for (int i = 0; i < W * H; i++)
            {
                byte r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
                if (Math.Abs(r - 21) + Math.Abs(g - 25) + Math.Abs(b - 35) > 30) meshPix++;
            }
            var wheelsGT = assets.NodeEntities[0].GetComponent<Luxel.Ecs.GlobalTransform>().Matrix;
            Console.WriteLine($"  frame {frameIdx} (t={t:F2}): mesh_pix={meshPix}, wheels[0] col0=({wheelsGT.M11:F2},{wheelsGT.M12:F2},{wheelsGT.M13:F2})");
            return px;
        }

        var f0 = RenderFrame(0, 0f);
        var f1 = RenderFrame(1, anim.Duration * 0.33f);
        var f2 = RenderFrame(2, anim.Duration * 0.66f);
        long DiffL1(byte[] a, byte[] b) { long d = 0; for (int i = 0; i < a.Length; i++) d += Math.Abs(a[i] - b[i]); return d; }
        long d01 = DiffL1(f0, f1), d12 = DiffL1(f1, f2), d02 = DiffL1(f0, f2);
        Console.WriteLine($"  frame diff: 0↔1={d01}, 1↔2={d12}, 0↔2={d02}");

        bool ok = d01 > 500 && d12 > 500 && d02 > 500;
        Console.WriteLine(ok ? "OK: 汎用 Drawable component + Collector で描画動作"
                              : "FAILED");
        return ok ? 0 : 1;
    }
}
