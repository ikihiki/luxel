using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.AssetsGpu;
using Luxel.Controls;
using Luxel.Graphics.RenderGraph;
using Luxel.Resources;
using Luxel.Resources.Browser;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Resources.Gallery.Stories.ResourceStoryKit;

namespace Luxel.Resources.Gallery.Stories;

/// <summary>
/// **morph target (ブレンドシェイプ)** — 頂点の位置/法線デルタを重み付きで加算して形を変える。
/// 平面グリッドに「中央が盛り上がる」morph target を 1 つ持たせ、重み 0.85 で描く。デルタ upload
/// (<see cref="SceneBuilder"/>) → <see cref="MorphWeights"/> component → <c>scene_pbr_morph</c> シェーダの経路。
/// アセットは手続き的に構築 (khronos-samples に morph 例が無いため)。glTF ローダの morph パースは単体テストで担保。
/// </summary>
public static class GltfMorphStories
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MorphDrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint IndexBufIndex;
        public uint InstanceBufIndex;
        public uint InstanceStart;
        public uint MorphBufIndex;
        public uint WeightBufIndex;
        public uint TargetCount;
        public uint VertexCount;
    }

    [Story("Examples/Resources/Gltf/MorphWeights", Height = 320, Order = 128)]
    public static Widget GltfMorph(StoryContext ctx)
    {
        AssetDocument generated = CreateDocument();
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaultHandles core = OperatingSystem.IsBrowser()
            ? builder.AddBrowserCore()
            : ResourceSystemDefaults.AddCore(builder);
        builder.Steps.Add<AssetDocumentSeed, AssetDocument>(new DocumentIdentityStep())
            .RunOn(core.CpuDomain).ManagedBy(core.CpuManager).Register();
        ResourceSystem resources = builder.Build();
        ResourceScope scope = resources.CreateScope("gpu-story/generated-document");
        ResourceHandle<AssetDocument> document = scope.Create<AssetDocumentSeed, AssetDocument>(
            "generated.gltf", new AssetDocumentSeed(generated));
        MorphScene? scene = null;
        int sceneVersion = -1;

        Widget view = GpuView(256, 256, (device, surface, time) =>
        {
            ResourceState snapshot = document.State;
            if (!snapshot.HasValue)
                return snapshot.Status == ResourceStatus.Failed
                    ? GpuViewRenderResult.Failed
                    : GpuViewRenderResult.Loading;

            if (scene is null || sceneVersion != snapshot.Version)
            {
                scene?.Dispose();
                scene = new MorphScene(document.Value);
                sceneVersion = snapshot.Version;
            }

            return scene.Render(device, surface, time);
        }, animated: false, dispose: () =>
        {
            scene?.Dispose();
            document.Dispose();
            scope.Dispose();
            resources.Dispose();
        });
        return ctx.Snap(Frame(view));
    }

    private sealed record AssetDocumentSeed(AssetDocument Document);

    private sealed class DocumentIdentityStep : IResourceStep<AssetDocumentSeed, AssetDocument>
    {
        public Task<AssetDocument> RunAsync(AssetDocumentSeed input, ResourceUri uri, LoadContext context)
            => Task.FromResult(input.Document);
    }

    private static AssetDocument CreateDocument()
    {
        const int n = 16;
        const float size = 4f;
        int vc = (n + 1) * (n + 1);
        var pos = new Vector3[vc];
        var nrm = new Vector3[vc];
        var uv = new Vector2[vc];
        var dPos = new Vector3[vc];
        var dNrm = new Vector3[vc];
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                int i = z * (n + 1) + x;
                float wx = x / (float)n * size - size / 2, wz = z / (float)n * size - size / 2;
                pos[i] = new Vector3(wx, 0, wz);
                nrm[i] = Vector3.UnitY;
                uv[i] = new Vector2(x / (float)n, z / (float)n);
                const float amp = 1.6f, sigma = 0.9f;
                float r2 = wx * wx + wz * wz;
                float h = amp * MathF.Exp(-r2 / (2 * sigma * sigma));
                dPos[i] = new Vector3(0, h, 0);
                float dhx = h * (-wx / (sigma * sigma)), dhz = h * (-wz / (sigma * sigma));
                Vector3 bumpN = Vector3.Normalize(new Vector3(-dhx, 1, -dhz));
                dNrm[i] = bumpN - Vector3.UnitY;
            }
        var idx = new List<uint>();
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                uint a = (uint)(z * (n + 1) + x), b = a + 1, c = a + (uint)(n + 1), d = c + 1;
                idx.AddRange([a, c, b, b, c, d]);
            }

        var material = new AssetMaterial { BaseColorFactor = new Vector4(0.85f, 0.55f, 0.30f, 1f) };
        var primitive = new AssetPrimitive
        {
            Attributes = new AssetVertexBuffer { Positions = pos, Normals = nrm, TexCoord0 = uv },
            Indices = idx.ToArray(),
            MorphTargets = [new AssetMorphTarget { DeltaPositions = dPos, DeltaNormals = dNrm }],
            Material = material,
        };
        var mesh = new AssetMesh();
        mesh.Primitives.Add(primitive);
        var node = new AssetNode { Mesh = mesh, Weights = [0.85f] };
        var scene = new AssetScene();
        scene.Roots.Add(node);
        var document = new AssetDocument { DefaultScene = scene };
        document.Materials.Add(material);
        document.Meshes.Add(mesh);
        document.Nodes.Add(node);
        document.Scenes.Add(scene);
        return document;
    }

    private sealed class MorphScene(AssetDocument document) : GpuSceneBase
    {
        private GpuTexture _depth = null!;
        private GpuPipeline _pipeline = null!;
        private SceneRenderExtractor _extractor = null!;
        private ScenePrimitiveGpu _prim = null!;
        private RenderBuffer<float> _weights = null!;

        protected override void OnInit()
        {
            AssetPrimitive prim = document.Meshes[0].Primitives[0];
            var world = new Luxel.Ecs.World();
            SceneAssets assets = Track(SceneBuilder.Build(world, document, Device));
            TransformPropagateSystem.Run(world);

            _extractor = Track(new SceneRenderExtractor(world, assets));
            _extractor.Extract(new ExtractContext(Device, frameIndex: 0));
            _prim = assets.Primitives[prim];

            // MorphWeights component → weight バッファ
            float[] w = [0.85f];
            world.Query<MorphWeights>().ForEachEntity((ref MorphWeights mw, Entity _) => w = mw.Weights);
            _weights = Track(new RenderBuffer<float>(Device, Math.Max(1, w.Length), "morphWeights"));
            for (int i = 0; i < w.Length; i++) _weights.Data[i] = w[i];
            _weights.MarkDirty();
            _weights.FlushImmediate();

            _depth = Track(Device.CreateDepthTarget(W, H));
            var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
            raster.DepthTest = true;
            raster.DepthWrite = true;
            _pipeline = Track(Device.CreateGraphicsPipeline(ResourceStoryShaders.Load("scene_pbr_morph"), raster));
        }

        protected override void OnRender(float time)
        {
            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(3.5f, 3.2f, 4.5f), new Vector3(0, 0.4f, 0), Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.5f, 1f, 0.1f, 100f);
            Matrix4x4 viewProj = view * proj;

            using var rg = new Luxel.Graphics.RenderGraph.RenderGraph(Device);
            BufferHandle hV = rg.ImportBuffer(_prim.VertexBuffer, "verts");
            BufferHandle hI = rg.ImportBuffer(_prim.IndexBuffer, "indices");
            BufferHandle hInst = rg.ImportBuffer(_extractor.InstanceBuffer, "instances");
            BufferHandle hMorph = rg.ImportBuffer(_prim.MorphBuffer!, "morph");
            BufferHandle hWeight = rg.ImportBuffer(_weights.Buffer, "weights");

            var pass = rg.AddPass("RenderMorph", PassQueue.Graphics)
                .Read(hV).Read(hI).Read(hInst).Read(hMorph).Read(hWeight).Write(hInst);   // Write で liveness
            pass.Execute(ctx =>
            {
                bool indexed = _prim.IndexCount > 0;
                ctx.Cmd.BeginRendering(Target, _depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                       .SetGraphicsPipeline(_pipeline)
                       .SetRootArguments(new MorphDrawArgs
                       {
                           ViewProj = Matrix4x4.Transpose(viewProj),
                           VertexBufIndex = ctx.BindlessIndex(hV),
                           IndexBufIndex = indexed ? ctx.BindlessIndex(hI) : 0xFFFFFFFFu,
                           InstanceBufIndex = ctx.BindlessIndex(hInst),
                           InstanceStart = 0,
                           MorphBufIndex = ctx.BindlessIndex(hMorph),
                           WeightBufIndex = ctx.BindlessIndex(hWeight),
                           TargetCount = (uint)_prim.MorphTargetCount,
                           VertexCount = (uint)_prim.VertexCount,
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
