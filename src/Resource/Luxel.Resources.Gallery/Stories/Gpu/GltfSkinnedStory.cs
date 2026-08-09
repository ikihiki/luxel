using System.Numerics;
using System.Runtime.InteropServices;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.AssetsGpu;
using Luxel.Assets.Gltf;
using Luxel.Controls;
using Luxel.Graphics.RenderGraph;
using Luxel.Resources;
using Luxel.Resources.Browser;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Resources.Gallery.Stories.ResourceStoryKit;

namespace Luxel.Resources.Gallery.Stories;

/// <summary>
/// **glTF スケルタルアニメーション (skin)** — JOINTS_0/WEIGHTS_0 を持つメッシュを GPU 頂点スキニングで描く。
/// <see cref="SceneBuilder"/> で ECS 展開 → アニメを固定時刻で <see cref="SceneAnimationPlayer"/> sample →
/// <see cref="TransformPropagateSystem"/> → <see cref="SkinningSystem"/> が joint 行列 (InverseBind × jointWorld) を
/// 計算 → joint バッファへ upload → <c>scene_pbr_skinned</c> シェーダで描画。ポーズは決定的 (固定 sample 時刻)。
/// </summary>
public static class GltfSkinnedStories
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SkinnedDrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint IndexBufIndex;
        public uint InstanceBufIndex;
        public uint JointBufIndex;
        public uint InstanceStart;
        public uint Pad0, Pad1, Pad2;
    }

    /// <summary>RiggedSimple.glb (2 ボーンの曲がる棒) を、アニメの途中ポーズで描く。</summary>
    [Story("Examples/Resources/Gltf/RiggedSimpleSkinning", Height = 320, Order = 127)]
    public static Widget GltfSkinned(StoryContext ctx)
    {
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaultHandles core = OperatingSystem.IsBrowser()
            ? builder.AddBrowserCore()
            : ResourceSystemDefaults.AddCore(builder);
        builder.Sources.Add(new GltfStoryAssets.EmbeddedFixtureSource())
            .RunOn(core.IoDomain).ManagedBy(core.IoManager).Register();
        builder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep())
            .RunOn(core.CpuDomain).ManagedBy(core.CpuManager).Register();
        ResourceSystem resources = builder.Build();
        ResourceHandle<AssetDocument> document = resources.Load<AssetDocument>(GltfStoryAssets.RiggedSimple);
        Signal<ResourceState> documentState = ctx.Observe(resources, document);
        SkinnedScene? scene = null;
        int sceneVersion = -1;

        Widget view = GpuView(256, 256, (device, surface, time) =>
        {
            ResourceState snapshot = documentState.Value;
            if (!snapshot.HasValue)
                return snapshot.Status == ResourceStatus.Failed
                    ? GpuViewRenderResult.Failed
                    : GpuViewRenderResult.Loading;

            if (scene is null || sceneVersion != snapshot.Version)
            {
                scene?.Dispose();
                scene = new SkinnedScene(document.Value);
                sceneVersion = snapshot.Version;
            }

            return scene.Render(device, surface, time);
        }, animated: false, dispose: () =>
        {
            scene?.Dispose();
            document.Dispose();
            resources.Dispose();
        });
        return ctx.Snap(Frame(view));
    }

    private sealed class SkinnedScene(AssetDocument document) : GpuSceneBase
    {
        private GpuTexture _depth = null!;
        private GpuPipeline _pipeline = null!;
        private ScenePrimitiveGpu _prim = null!;
        private RenderBuffer<Matrix4x4> _joints = null!;
        private RenderBuffer<SceneInstanceData> _instance = null!;

        protected override void OnInit()
        {
            var baseColor = new Vector4(0.85f, 0.55f, 0.30f, 1f);
            for (int i = 0; i < document.Materials.Count; i++)
                document.Materials[i].BaseColorFactor = baseColor;

            var world = new Luxel.Ecs.World();
            SceneAssets assets = Track(SceneBuilder.Build(world, document, Device));

            // アニメを固定時刻で sample (決定的ポーズ) → 伝播 → joint 行列
            if (document.Animations.Count > 0)
            {
                var player = new SceneAnimationPlayer(world, assets, document.Animations[0]);
                float dur = MathF.Max(0.01f, document.Animations[0].Duration);
                player.Sample(dur * 0.30f);   // 曲がりの見える途中ポーズ
            }
            TransformPropagateSystem.Run(world);
            SkinningSystem.Run(world, assets);

            // スキン付き entity を探し、joint 行列を GPU へ
            Matrix4x4[] jointMats = Array.Empty<Matrix4x4>();
            world.Query<AssetMeshRef, AssetSkinRef, JointMatrices>().ForEachEntity(
                (ref AssetMeshRef mr, ref AssetSkinRef _, ref JointMatrices jm, Friflo.Engine.ECS.Entity _) =>
                {
                    if (_prim is not null) return;   // 最初のスキンメッシュのみ
                    AssetPrimitive p = mr.Mesh.Primitives[0];
                    if (assets.Primitives.TryGetValue(p, out ScenePrimitiveGpu gpu) && gpu.HasSkinning)
                    {
                        _prim = gpu;
                        jointMats = jm.Matrices;
                    }
                });
            if (_prim is null) throw new InvalidOperationException("スキン付き primitive が見つかりません");

            _joints = Track(new RenderBuffer<Matrix4x4>(Device, Math.Max(1, jointMats.Length), "jointMats"));
            for (int i = 0; i < jointMats.Length; i++) _joints.Data[i] = jointMats[i];
            _joints.MarkDirty();
            _joints.FlushImmediate();

            _instance = Track(new RenderBuffer<SceneInstanceData>(Device, 1, "skinnedInstance"));
            _instance.Data[0] = new SceneInstanceData { World = Matrix4x4.Identity, BaseColor = baseColor };
            _instance.MarkDirty();
            _instance.FlushImmediate();

            _depth = Track(Device.CreateDepthTarget(W, H));
            var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
            raster.DepthTest = true;
            raster.DepthWrite = true;
            _pipeline = Track(Device.CreateGraphicsPipeline(ResourceStoryShaders.Load("scene_pbr_skinned"), raster));
        }

        protected override void OnRender(float time)
        {
            // RiggedSimple のバインドポーズは原点付近・高さ ~6。斜め上から俯瞰。
            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(9f, 7f, 12f), new Vector3(0, 3f, 0), Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, 1f, 0.1f, 100f);
            Matrix4x4 viewProj = view * proj;

            using var rg = new Luxel.Graphics.RenderGraph.RenderGraph(Device);
            BufferHandle hV = rg.ImportBuffer(_prim.VertexBuffer, "skinnedVerts");
            BufferHandle hI = rg.ImportBuffer(_prim.IndexBuffer, "indices");
            BufferHandle hInst = rg.ImportBuffer(_instance.Buffer, "instance");
            BufferHandle hJoint = rg.ImportBuffer(_joints.Buffer, "joints");

            // Write が無いパスはデッドパスカリングされるため、instance を liveness アンカーに Write 宣言
            var pass = rg.AddPass("RenderSkinned", PassQueue.Graphics)
                .Read(hV).Read(hI).Read(hInst).Read(hJoint).Write(hInst);
            pass.Execute(ctx =>
            {
                bool indexed = _prim.IndexCount > 0;
                ctx.Cmd.BeginRendering(Target, _depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                       .SetGraphicsPipeline(_pipeline)
                       .SetRootArguments(new SkinnedDrawArgs
                       {
                           ViewProj = Matrix4x4.Transpose(viewProj),
                           VertexBufIndex = ctx.BindlessIndex(hV),
                           IndexBufIndex = indexed ? ctx.BindlessIndex(hI) : 0xFFFFFFFFu,
                           InstanceBufIndex = ctx.BindlessIndex(hInst),
                           JointBufIndex = ctx.BindlessIndex(hJoint),
                           InstanceStart = 0,
                       })
                       .Draw((uint)(indexed ? _prim.IndexCount : _prim.VertexCount), 1);
                ctx.Cmd.EndRendering();
            });

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            rg.Execute(cmd);
            cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy)
               .CopyTextureToBuffer(Target, OutBuffer);
            cmd.Finish();
            Device.MainQueue.Submit(cmd);
        }
    }
}
