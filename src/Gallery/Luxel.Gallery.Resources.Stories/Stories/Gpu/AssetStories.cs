using System.Numerics;
using System.Runtime.InteropServices;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.AssetsGpu;
using Luxel.Assets.Gltf;
using Luxel.Graphics.RenderGraph;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// アセットパイプラインのデモ — glTF を <see cref="GltfResourceStep"/> で AssetDocument に読み、
/// <see cref="SceneBuilder"/> で ECS + GPU バッファへ展開し、<see cref="SceneRenderExtractor"/> が
/// instance バッファを作って scene_pbr_lite シェーダで描く。
/// </summary>
public static class AssetStories
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint IndexBufIndex;      // 0xFFFFFFFF = non-indexed
        public uint InstanceBufIndex;
        public uint InstanceStart;
    }

    /// <summary>Box.gltf → AssetDocument → AssetPrimitive → GPU buffers → 1 draw。ECSなしの静的最小経路。</summary>
    [Story("Examples/3D/GltfBox", Height = 320, Order = 125)]
    public static Widget GltfBox() => Frame(GpuSceneBase.View(256, 256, new StaticGltfScene("Box.gltf"), animated: false));

    /// <summary>静的primitiveを直接uploadし、1件のinstance bufferで描く。ECS/animation/skin/morphは使わない。</summary>
    private sealed class StaticGltfScene(string file) : GpuSceneBase
    {
        private GpuPrimitive _primitive = null!;
        private GpuBuffer _instances = null!;
        private GpuTexture _depth = null!;
        private GpuPipeline _pipeline = null!;

        protected override void OnInit()
        {
            string path = FindSample(file);
            AssetDocument doc = GltfStoryAssets.LoadDocument(path);
            AssetPrimitive source = doc.Meshes.FirstOrDefault()?.Primitives.FirstOrDefault()
                ?? throw new InvalidDataException($"glTF has no mesh primitive: {path}");

            _primitive = Track(GpuAssetFactory.Upload(source, Device));
            Vector4 baseColor = source.Material?.BaseColorFactor ?? new Vector4(0.86f, 0.34f, 0.30f, 1f);
            _instances = Track(Device.Malloc((ulong)SceneInstanceData.Stride, GpuMemoryKind.HostMapped));
            _instances.Span<SceneInstanceData>(1)[0] = new SceneInstanceData
            {
                World = Matrix4x4.Identity,
                BaseColor = baseColor,
            };

            _depth = Track(Device.CreateDepthTarget(W, H));
            GpuRasterDesc raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
            raster.DepthTest = true;
            raster.DepthWrite = true;
            _pipeline = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_lite"), raster));
        }

        protected override void OnRender(float time)
        {
            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(2.6f, 2.0f, -3.4f), new Vector3(0, 0.4f, 0), Vector3.UnitY);
            Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
            bool indexed = _primitive.IndexBuffer is not null;

            using GpuCommandBuffer command = Device.MainQueue.StartCommandRecording();
            command.BeginRendering(Target, _depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                .SetGraphicsPipeline(_pipeline)
                .SetRootArguments(new DrawArgs
                {
                    ViewProj = Matrix4x4.Transpose(view * projection),
                    VertexBufIndex = _primitive.VertexBuffer.BindlessIndex,
                    IndexBufIndex = indexed ? _primitive.IndexBuffer!.BindlessIndex : 0xFFFFFFFFu,
                    InstanceBufIndex = _instances.BindlessIndex,
                    InstanceStart = 0,
                })
                .Draw((uint)(indexed ? _primitive.IndexCount : _primitive.VertexCount), 1)
                .EndRendering()
                .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                .CopyTextureToBuffer(Target, OutBuffer);
            command.Finish();
            Device.MainQueue.SubmitAndWait(command);
        }
    }

    private static string FindSample(string file)
    {
        // Khronos samples are repository tools; support both repo-root cwd and bin launches.
        string[] candidates =
        [
            Path.Combine(Environment.CurrentDirectory, "tools", "khronos-samples", file),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "khronos-samples", file),
        ];
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"khronos-samples/{file} が見つかりません");
    }

    /// <summary>BoxAnimated.glb — ノード TRS アニメーションを SceneAnimationPlayer が毎フレーム
    /// sample → TransformPropagate → 再 Extract して描く (スキニングなしのアニメーション経路)。</summary>
    [Story("Examples/3D/GltfAnimated", Height = 320, Order = 126)]
    public static Widget GltfAnimated(StoryContext ctx) => ctx.Snap(Frame(GpuSceneBase.View(256, 256, new GltfScene("BoxAnimated.glb", animate: true))));

    /// <summary>khronos-samples の glTF を読み、SceneBuilder → SceneRenderExtractor → 描画。
    /// animate 時は毎フレーム anim[0] を周期 sample して instance を書き直す。</summary>
    private sealed class GltfScene(string file, bool animate = false) : GpuSceneBase
    {
        private Luxel.Ecs.World _world = null!;
        private SceneAssets _assets = null!;
        private SceneRenderExtractor _extractor = null!;
        private SceneAnimationPlayer? _player;
        private float _duration;
        private GpuTexture _depth = null!;
        private GpuPipeline _pipeline = null!;
        private ulong _frame;

        protected override bool RenderEveryFrame => animate;

        protected override void OnInit()
        {
            AssetDocument doc = GltfStoryAssets.LoadDocument(FindSample(file));
            // Khronos sample の material は白 — 見やすい色に上書き
            for (int i = 0; i < doc.Materials.Count; i++)
                doc.Materials[i].BaseColorFactor = i % 2 == 0
                    ? new Vector4(0.86f, 0.34f, 0.30f, 1f)
                    : new Vector4(0.30f, 0.62f, 0.90f, 1f);

            _world = new Luxel.Ecs.World();
            _assets = Track(SceneBuilder.Build(_world, doc, Device));
            TransformPropagateSystem.Run(_world);

            if (animate && doc.Animations.Count > 0)
            {
                _player = new SceneAnimationPlayer(_world, _assets, doc.Animations[0]);
                _duration = MathF.Max(0.01f, doc.Animations[0].Duration);
            }

            _extractor = Track(new SceneRenderExtractor(_world, _assets));
            _extractor.Extract(new ExtractContext(Device, frameIndex: _frame++));

            _depth = Track(Device.CreateDepthTarget(W, H));
            var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
            raster.DepthTest = true;
            raster.DepthWrite = true;
            _pipeline = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_lite"), raster));
        }

        protected override void OnRender(float time)
        {
            if (_player is not null)
            {
                _player.Sample(time % _duration);   // 周期は t % dur — snap の決定性
                TransformPropagateSystem.Run(_world);
                _extractor.Extract(new ExtractContext(Device, frameIndex: _frame++));
            }

            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(2.6f, 2.0f, -3.4f), new Vector3(0, 0.4f, 0), Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
            Matrix4x4 viewProj = view * proj;

            using var rg = new Luxel.Graphics.RenderGraph.RenderGraph(Device);
            BufferHandle hInsts = rg.ImportBuffer(_extractor.InstanceBuffer, "instances");
            // DrawList の primitive ごとに vertex/index を import し、pass 内で順に Draw
            var draws = new List<(BufferHandle V, BufferHandle I, ScenePrimitiveGpu Gpu, int Start, int Count)>();
            var pass = rg.AddPass("RenderGltf", PassQueue.Graphics).Read(hInsts).Write(hInsts);
            foreach ((AssetPrimitive prim, int start, int count) in _extractor.DrawList)
            {
                ScenePrimitiveGpu gpu = _assets.Primitives[prim];
                BufferHandle hV = rg.ImportBuffer(gpu.VertexBuffer, "verts");
                BufferHandle hI = rg.ImportBuffer(gpu.IndexBuffer, "indices");
                pass.Read(hV).Read(hI);
                draws.Add((hV, hI, gpu, start, count));
            }
            pass.Execute(ctx =>
            {
                ctx.Cmd.BeginRendering(Target, _depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                       .SetGraphicsPipeline(_pipeline);
                foreach ((BufferHandle hV, BufferHandle hI, ScenePrimitiveGpu gpu, int start, int count) in draws)
                {
                    bool indexed = gpu.IndexCount > 0;
                    ctx.Cmd.SetRootArguments(new DrawArgs
                    {
                        ViewProj = Matrix4x4.Transpose(viewProj),
                        VertexBufIndex = ctx.BindlessIndex(hV),
                        IndexBufIndex = indexed ? ctx.BindlessIndex(hI) : 0xFFFFFFFFu,
                        InstanceBufIndex = ctx.BindlessIndex(hInsts),
                        InstanceStart = (uint)start,
                    }).Draw((uint)(indexed ? gpu.IndexCount : gpu.VertexCount), (uint)count);
                }
                ctx.Cmd.EndRendering();
            });

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            rg.Execute(cmd);
            cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy)
               .CopyTextureToBuffer(Target, OutBuffer);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }
}
