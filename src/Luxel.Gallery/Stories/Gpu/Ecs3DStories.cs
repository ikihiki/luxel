using System.Numerics;
using System.Runtime.InteropServices;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.Ecs;
using Luxel.RenderGraph;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// ECS + 3D forward + RenderGraph のデモ — World にキューブ entity を作り、
/// Render3DExtractSystem (IRenderExtractor) が ECS をクエリして instance バッファへ書き、
/// graphics パスが bindless で描く。docs の Docs/ThreeD / Docs/RenderGraph から参照される。
/// </summary>
public static class Ecs3DStories
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurArgs
    {
        public uint SrcIndex, DstIndex, Width, Height;
        public int DirX, DirY;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BloomArgs
    {
        public uint BaseIndex, BlurIndex, DstIndex, Width, Height;
        public float Intensity;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ShadowArgs
    {
        public Matrix4x4 LightVP;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MainArgs
    {
        public Matrix4x4 ViewProj;
        public Matrix4x4 LightVP;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint ShadowBufIndex;
        public uint ShadowW;
        public uint ShadowH;
        public uint Pad0, Pad1, Pad2;
    }

    /// <summary>5×5 グリッドのキューブ world + カメラ周回。ECS 抽出 → RenderGraph 1 パス。</summary>
    [Story("Demos/3D/EcsCubes", Height = 320, Order = 122)]
    public static Widget EcsCubes() => Frame(GpuView(256, 256, new EcsCubesScene()));

    /// <summary>world-space UI — Scene2D を bindless バッファへラスタライズし、3D 空間内の
    /// quad がピクセルシェーダでサンプリングする。Compute→PixelShader のバリアは RG が自動挿入。</summary>
    [Story("Demos/3D/WorldSpaceUI", Height = 320, Order = 123)]
    public static Widget WorldSpaceUi() => Frame(GpuView(256, 256, new WorldSpaceUiScene()));

    /// <summary>shadow map — ライト視点で R32Float RT に NDC z を書き、メイン視点パスが
    /// bindless バッファ経由で読み比較して影を落とす (床 + 浮遊キューブ 5 個)。</summary>
    [Story("Demos/3D/ShadowMap", Height = 320, Order = 124)]
    public static Widget ShadowMap(StoryContext ctx) => ctx.Snap(Frame(GpuView(256, 256, new ShadowMapScene(), animated: false)));

    /// <summary>3D forward + post-process bloom を 1 つの RenderGraph で —
    /// Render3D (graphics) → BlurH → BlurV → BloomCombine (compute)。intensity は knob。</summary>
    [Story("Demos/RenderGraph/Bloom3D", Height = 320, Order = 132)]
    public static Widget Bloom3D(StoryContext ctx)
    {
        Signal<float> intensity = ctx.Signal("intensity", 0.6f, "bloom 合成の強さ (0 = 無効)");
        return Frame(GpuView(256, 256, new Bloom3DScene(intensity)));
    }

    // ---- 共有下回り ----

    private static readonly Vector4[] Palette =
    [
        new(1.00f, 0.40f, 0.40f, 1f),
        new(0.40f, 0.95f, 0.55f, 1f),
        new(0.35f, 0.70f, 1.00f, 1f),
        new(1.00f, 0.90f, 0.35f, 1f),
        new(0.90f, 0.55f, 1.00f, 1f),
    ];

    /// <summary>n×n グリッドのキューブ entity を持つ World (LocalTransform / Color3D / MeshRef)。</summary>
    private static Luxel.Ecs.World CreateCubeGrid(int n)
    {
        var world = new Luxel.Ecs.World();
        for (int gz = 0; gz < n; gz++)
        {
            for (int gx = 0; gx < n; gx++)
            {
                int half = n / 2;
                Vector3 pos = new((gx - half) * 0.9f, n == 3 ? -0.5f : 0f, (gz - half) * 0.9f);
                Quaternion rot = Quaternion.CreateFromYawPitchRoll((gx + gz) * 0.35f, 0.25f, 0);
                world.CreateEntity(
                    new LocalTransform(
                        Matrix4x4.CreateScale(new Vector3(0.4f))
                      * Matrix4x4.CreateFromQuaternion(rot)
                      * Matrix4x4.CreateTranslation(pos)),
                    new Color3D(Palette[(gx + gz * n) % Palette.Length]),
                    new MeshRef(MeshRef.Cube));
            }
        }
        TransformPropagateSystem.Run(world);
        return world;
    }

    private static GpuBuffer CreateCubeVb(GpuDevice device)
    {
        ReadOnlySpan<CubeMesh.Vertex> verts = CubeMesh.Vertices;
        GpuBuffer vb = device.Malloc((ulong)(verts.Length * CubeMesh.VertexStride), GpuMemoryKind.HostMapped);
        verts.CopyTo(vb.Span<CubeMesh.Vertex>(verts.Length));
        return vb;
    }

    /// <summary>原点を見下ろす周回カメラ (angle=0 で (0, 2.5, -5.8))。</summary>
    private static Matrix4x4 OrbitViewProj(float angle)
    {
        var pos = new Vector3(MathF.Sin(angle) * 5.8f, 2.5f, -MathF.Cos(angle) * 5.8f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(pos, Vector3.Zero, Vector3.UnitY);
        Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
        return view * proj;
    }

    private static GpuRasterDesc DepthOn(GpuFormat format)
    {
        var raster = GpuRasterDesc.Default(format);
        raster.DepthTest = true;
        raster.DepthWrite = true;
        return raster;
    }

    /// <summary>ECS キューブ群を graphics 1 パスで描く (カメラは時間で周回)。</summary>
    private sealed class EcsCubesScene : GpuSceneBase
    {
        private GpuBuffer _vb = null!;
        private Render3DExtractSystem _extractor = null!;
        private GpuTexture _depth = null!;
        private GpuPipeline _pipeline = null!;

        protected override bool RenderEveryFrame => true;

        protected override void OnInit()
        {
            Luxel.Ecs.World world = CreateCubeGrid(5);
            _vb = Track(CreateCubeVb(Device));
            _extractor = Track(new Render3DExtractSystem(world, Device));
            _extractor.Extract();
            _depth = Track(Device.CreateDepthTarget(W, H));
            _pipeline = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"),
                DepthOn(GpuFormat.Rgba8Unorm)));
        }

        protected override void OnRender(float time)
        {
            Matrix4x4 viewProj = OrbitViewProj(time * 0.4f);
            using var rg = new Luxel.RenderGraph.RenderGraph(Device);   // グラフは 1 フレーム使い切り
            BufferHandle hVerts = rg.ImportBuffer(_vb, "verts");
            BufferHandle hInsts = rg.ImportBuffer(_extractor.InstanceBuffer, "instances");

            rg.AddPass("Render3D", PassQueue.Graphics)
              .Read(hVerts).Read(hInsts)
              .Write(hInsts)   // 「使用」の宣言 — Write が 1 つもないパスはデッドパスカリングされる
              .Execute(ctx =>
              {
                  // Slang/HLSL の既定行列レイアウトは column-major、System.Numerics.Matrix4x4 は
                  // row-major (各行が連続)。直接アップロードすると shader 側が転置として解釈するので、
                  // CPU 側で転置を入れて両者を整える。(per-instance 行列は shader 側で Load4 ×4 +
                  // float4x4(r0..r3) で「各引数=行」として構築するので転置不要。)
                  var args = new DrawArgs
                  {
                      ViewProj = Matrix4x4.Transpose(viewProj),
                      VertexBufIndex = ctx.BindlessIndex(hVerts),
                      InstanceBufIndex = ctx.BindlessIndex(hInsts),
                  };
                  ctx.Cmd.BeginRendering(Target, _depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                         .SetGraphicsPipeline(_pipeline)
                         .SetRootArguments(args)
                         .Draw((uint)CubeMesh.VertexCount, (uint)_extractor.InstanceCount)
                         .EndRendering();
              });

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            rg.Execute(cmd);
            cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy)
               .CopyTextureToBuffer(Target, OutBuffer);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }

    /// <summary>3D forward → base バッファへコピー → BlurH/BlurV (transient) → BloomCombine。</summary>
    private sealed class Bloom3DScene(Signal<float> intensity) : GpuSceneBase
    {
        private GpuBuffer _vb = null!, _base = null!;
        private Render3DExtractSystem _extractor = null!;
        private GpuTexture _depth = null!;
        private GpuPipeline _pl3d = null!, _plBlur = null!, _plBloom = null!;

        protected override bool RenderEveryFrame => true;   // knob (intensity) を毎フレーム反映

        protected override void OnInit()
        {
            Luxel.Ecs.World world = CreateCubeGrid(5);
            _vb = Track(CreateCubeVb(Device));
            _extractor = Track(new Render3DExtractSystem(world, Device));
            _extractor.Extract();
            _depth = Track(Device.CreateDepthTarget(W, H));
            _base = Track(Device.Malloc((ulong)(W * H * 4), GpuMemoryKind.HostMapped));
            _pl3d = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"),
                DepthOn(GpuFormat.Rgba8Unorm)));
            _plBlur = Track(Device.CreateComputePipeline(GpuShaderCode.Load("compute_blur")));
            _plBloom = Track(Device.CreateComputePipeline(GpuShaderCode.Load("compute_bloom_combine")));
        }

        protected override void OnRender(float time)
        {
            ulong fbBytes = (ulong)(W * H * 4);
            Matrix4x4 viewProj = OrbitViewProj(time * 0.15f);
            using var rg = new Luxel.RenderGraph.RenderGraph(Device);
            BufferHandle hVerts = rg.ImportBuffer(_vb, "verts");
            BufferHandle hInsts = rg.ImportBuffer(_extractor.InstanceBuffer, "instances");
            BufferHandle hBase = rg.ImportBuffer(_base, "base3d");
            BufferHandle hFinal = rg.ImportBuffer(OutBuffer, "final");
            BufferHandle hTmp = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "tmp");
            BufferHandle hBlur = rg.CreateBuffer(new BufferDesc(fbBytes, GpuMemoryKind.HostMapped), "blur");

            // Write(hBase) は CopyDest 経由 — compute から見ると Copy→Compute の barrier が要るので明示
            rg.AddPass("Render3D", PassQueue.Graphics)
              .Read(hVerts).Read(hInsts).Write(hBase, ResourceUsage.CopyDest)
              .Execute(ctx =>
              {
                  var args = new DrawArgs
                  {
                      ViewProj = Matrix4x4.Transpose(viewProj),
                      VertexBufIndex = ctx.BindlessIndex(hVerts),
                      InstanceBufIndex = ctx.BindlessIndex(hInsts),
                  };
                  ctx.Cmd.BeginRendering(Target, _depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                         .SetGraphicsPipeline(_pl3d)
                         .SetRootArguments(args)
                         .Draw((uint)CubeMesh.VertexCount, (uint)_extractor.InstanceCount)
                         .EndRendering()
                         .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                         .CopyTextureToBuffer(Target, _base);
              });

            rg.AddPass("BlurH", PassQueue.Compute).Read(hBase).Write(hTmp)
              .Execute(c => c.Cmd.SetComputePipeline(_plBlur).SetRootArguments(new BlurArgs
              {
                  SrcIndex = c.BindlessIndex(hBase),
                  DstIndex = c.BindlessIndex(hTmp),
                  Width = W,
                  Height = H,
                  DirX = 1,
                  DirY = 0,
              }).Dispatch((W + 7) / 8, (H + 7) / 8));
            rg.AddPass("BlurV", PassQueue.Compute).Read(hTmp).Write(hBlur)
              .Execute(c => c.Cmd.SetComputePipeline(_plBlur).SetRootArguments(new BlurArgs
              {
                  SrcIndex = c.BindlessIndex(hTmp),
                  DstIndex = c.BindlessIndex(hBlur),
                  Width = W,
                  Height = H,
                  DirX = 0,
                  DirY = 1,
              }).Dispatch((W + 7) / 8, (H + 7) / 8));
            rg.AddPass("BloomCombine", PassQueue.Compute)
              .Read(hBase).Read(hBlur).Write(hFinal)
              .Execute(c => c.Cmd.SetComputePipeline(_plBloom).SetRootArguments(new BloomArgs
              {
                  BaseIndex = c.BindlessIndex(hBase),
                  BlurIndex = c.BindlessIndex(hBlur),
                  DstIndex = c.BindlessIndex(hFinal),
                  Width = W,
                  Height = H,
                  Intensity = MathF.Max(0f, intensity.Value),
              }).Dispatch((W + 7) / 8, (H + 7) / 8));

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            rg.Execute(cmd);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }

    /// <summary>2D UI をバッファへ 1 回ラスタライズ → 3D 内の傾いた quad がサンプリング。
    /// 前景にキューブ群、quad は時間でゆっくり首を振る。</summary>
    private sealed class WorldSpaceUiScene : GpuSceneBase
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct UiQuadArgs
        {
            public Matrix4x4 Mvp;
            public uint VertexBufIndex;
            public uint UiBufferIndex;
            public uint UiWidth;
            public uint UiHeight;
        }

        private const uint UiW = 256, UiH = 256;

        private GpuBuffer _uiBuf = null!, _cubeVb = null!, _quadVb = null!;
        private Render3DExtractSystem _extractor = null!;
        private GpuTexture _depth = null!;
        private GpuPipeline _plCube = null!, _plQuad = null!;

        protected override bool RenderEveryFrame => true;

        protected override void OnInit()
        {
            // 2D UI シーン → bindless バッファへラスタライズ (1 回きり)
            using var raster = new Luxel.Graphics.TwoD.Rasterizer2D(Device);
            var ui = new Luxel.Graphics.TwoD.Scene2D();
            ui.FillRoundedRect(Luxel.Graphics.TwoD.Color2D.Rgba(245, 240, 230, 255), 0, 0, UiW, UiH, 0);
            ui.FillRoundedRect(Luxel.Graphics.TwoD.Color2D.Rgba(60, 130, 240, 255), 20, 20, 100, 40, 8);
            ui.FillRoundedRect(Luxel.Graphics.TwoD.Color2D.Rgba(230, 80, 100, 255), 140, 20, 100, 40, 8);
            ui.FillRoundedRect(Luxel.Graphics.TwoD.Color2D.Rgba(40, 200, 120, 255), 20, 80, 220, 60, 12);
            ui.FillRoundedRect(Luxel.Graphics.TwoD.Color2D.Rgba(180, 90, 220, 255), 20, 160, 100, 80, 14);
            ui.FillRoundedRect(Luxel.Graphics.TwoD.Color2D.Rgba(255, 200, 60, 255), 140, 160, 100, 80, 14);
            using Luxel.Graphics.TwoD.EncodedScene encoded = raster.Encode(ui);
            _uiBuf = Track(Device.Malloc((ulong)(UiW * UiH * 4), GpuMemoryKind.HostMapped));
            using (GpuCommandBuffer c = Device.MainQueue.StartCommandRecording())
            {
                raster.Render(c, encoded, Luxel.Graphics.TwoD.Camera2D.Pixels, UiW, UiH, _uiBuf);
                c.Finish();
                Device.MainQueue.SubmitAndWait(c);
            }

            Luxel.Ecs.World world = CreateCubeGrid(3);
            _cubeVb = Track(CreateCubeVb(Device));
            _extractor = Track(new Render3DExtractSystem(world, Device));
            _extractor.Extract();
            ReadOnlySpan<UiQuadMesh.Vertex> quadVerts = UiQuadMesh.Vertices;
            _quadVb = Track(Device.Malloc((ulong)(quadVerts.Length * UiQuadMesh.VertexStride), GpuMemoryKind.HostMapped));
            quadVerts.CopyTo(_quadVb.Span<UiQuadMesh.Vertex>(quadVerts.Length));

            _depth = Track(Device.CreateDepthTarget(W, H));
            _plCube = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"),
                DepthOn(GpuFormat.Rgba8Unorm)));
            _plQuad = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("ui_quad_3d"),
                DepthOn(GpuFormat.Rgba8Unorm)));
        }

        protected override void OnRender(float time)
        {
            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(2.4f, 1.8f, -4.0f),
                new Vector3(0, 0.2f, 0.5f), Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
            Matrix4x4 viewProj = view * proj;
            // UI quad: 背景に立てた板。時間でゆっくり首を振る
            Matrix4x4 uiWorld =
                Matrix4x4.CreateScale(4.0f, 4.0f, 1f)
              * Matrix4x4.CreateRotationY(-0.18f + MathF.Sin(time * 0.6f) * 0.15f)
              * Matrix4x4.CreateTranslation(0f, 0.5f, 2.5f);
            Matrix4x4 uiMvp = uiWorld * viewProj;

            using var rg = new Luxel.RenderGraph.RenderGraph(Device);
            BufferHandle hUi = rg.ImportBuffer(_uiBuf, "uiBuf");
            BufferHandle hCubeVb = rg.ImportBuffer(_cubeVb, "cubeVb");
            BufferHandle hCubeInst = rg.ImportBuffer(_extractor.InstanceBuffer, "cubeInst");
            BufferHandle hQuadVb = rg.ImportBuffer(_quadVb, "quadVb");
            BufferHandle hRead = rg.ImportBuffer(OutBuffer, "readback");

            rg.AddPass("Render3D", PassQueue.Graphics)
              .Read(hCubeVb).Read(hCubeInst).Read(hQuadVb)
              .Read(hUi, ResourceUsage.SampledInPixelShader)
              .Write(hRead, ResourceUsage.CopyDest)
              .Execute(ctx =>
              {
                  var cubeArgs = new DrawArgs
                  {
                      ViewProj = Matrix4x4.Transpose(viewProj),
                      VertexBufIndex = ctx.BindlessIndex(hCubeVb),
                      InstanceBufIndex = ctx.BindlessIndex(hCubeInst),
                  };
                  var quadArgs = new UiQuadArgs
                  {
                      Mvp = Matrix4x4.Transpose(uiMvp),
                      VertexBufIndex = ctx.BindlessIndex(hQuadVb),
                      UiBufferIndex = ctx.BindlessIndex(hUi),
                      UiWidth = UiW,
                      UiHeight = UiH,
                  };
                  ctx.Cmd.BeginRendering(Target, _depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                         // UI quad 先 (背景)、深度書込で後ろに沈める
                         .SetGraphicsPipeline(_plQuad)
                         .SetRootArguments(quadArgs)
                         .Draw((uint)UiQuadMesh.VertexCount, 1)
                         // キューブ群を前景
                         .SetGraphicsPipeline(_plCube)
                         .SetRootArguments(cubeArgs)
                         .Draw((uint)CubeMesh.VertexCount, (uint)_extractor.InstanceCount)
                         .EndRendering()
                         .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                         .CopyTextureToBuffer(Target, OutBuffer);
              });

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            rg.Execute(cmd);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }

    /// <summary>ライト視点 R32Float → bindless バッファ → メインパスで比較 (静的ワンショット)。</summary>
    private sealed class ShadowMapScene : GpuSceneBase
    {
        private const uint ShadowW = 256, ShadowH = 256;

        private GpuBuffer _vb = null!, _shadowBuf = null!;
        private Render3DExtractSystem _extractor = null!;
        private GpuTexture _shadowRt = null!, _shadowDepth = null!, _mainDepth = null!;
        private GpuPipeline _plShadow = null!, _plMain = null!;

        protected override void OnInit()
        {
            // 床 (薄く大きなキューブ) + 高さ違いの浮遊キューブ 5 個
            var world = new Luxel.Ecs.World();
            world.CreateEntity(
                new LocalTransform(Matrix4x4.CreateScale(6f, 0.05f, 6f)
                                 * Matrix4x4.CreateTranslation(0, -0.6f, 0)),
                new Color3D(new Vector4(0.85f, 0.82f, 0.78f, 1f)),
                new MeshRef(MeshRef.Cube));
            Vector3[] positions =
            [
                new(-1.2f, 0.2f, -0.5f),
                new(0.0f, 0.5f, +0.5f),
                new(+1.2f, 0.0f, -0.6f),
                new(-0.6f, 0.9f, +1.0f),
                new(+0.8f, 0.7f, +1.4f),
            ];
            for (int i = 0; i < positions.Length; i++)
            {
                Quaternion rot = Quaternion.CreateFromYawPitchRoll(i * 0.5f, 0.25f, 0.15f);
                world.CreateEntity(
                    new LocalTransform(Matrix4x4.CreateScale(0.45f)
                                     * Matrix4x4.CreateFromQuaternion(rot)
                                     * Matrix4x4.CreateTranslation(positions[i])),
                    new Color3D(Palette[i % Palette.Length]),
                    new MeshRef(MeshRef.Cube));
            }
            TransformPropagateSystem.Run(world);

            _vb = Track(CreateCubeVb(Device));
            _extractor = Track(new Render3DExtractSystem(world, Device));
            _extractor.Extract();

            _shadowRt = Track(Device.CreateRenderTarget(ShadowW, ShadowH, GpuFormat.R32Float));
            _shadowDepth = Track(Device.CreateDepthTarget(ShadowW, ShadowH));
            _mainDepth = Track(Device.CreateDepthTarget(W, H));
            _shadowBuf = Track(Device.Malloc((ulong)(ShadowW * ShadowH * 4), GpuMemoryKind.HostMapped));

            _plShadow = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("shadow_pass"),
                DepthOn(GpuFormat.R32Float)));
            _plMain = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_with_shadow"),
                DepthOn(GpuFormat.Rgba8Unorm)));
        }

        protected override void OnRender(float time)
        {
            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(2.8f, 2.0f, -4.0f), Vector3.Zero, Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
            Matrix4x4 viewProj = view * proj;
            // ライトカメラ (ortho、上から斜めに) — シーンを覆う十分な範囲
            Vector3 lightDir = Vector3.Normalize(new Vector3(0.5f, 0.95f, 0.25f));
            Matrix4x4 lightVP = Matrix4x4.CreateLookAt(lightDir * 6.0f, Vector3.Zero, Vector3.UnitY)
                              * Matrix4x4.CreateOrthographic(6.5f, 6.5f, 0.5f, 12f);

            using var rg = new Luxel.RenderGraph.RenderGraph(Device);
            BufferHandle hVerts = rg.ImportBuffer(_vb, "verts");
            BufferHandle hInsts = rg.ImportBuffer(_extractor.InstanceBuffer, "instances");
            BufferHandle hShadow = rg.ImportBuffer(_shadowBuf, "shadowBuf");
            BufferHandle hRead = rg.ImportBuffer(OutBuffer, "readback");

            rg.AddPass("ShadowPass", PassQueue.Graphics)
              .Read(hVerts).Read(hInsts)
              .Write(hShadow, ResourceUsage.CopyDest)
              .Execute(ctx =>
              {
                  var args = new ShadowArgs
                  {
                      LightVP = Matrix4x4.Transpose(lightVP),
                      VertexBufIndex = ctx.BindlessIndex(hVerts),
                      InstanceBufIndex = ctx.BindlessIndex(hInsts),
                  };
                  ctx.Cmd.BeginRendering(_shadowRt, _shadowDepth, 1f, 0, 0, 0, 1f)
                         .SetGraphicsPipeline(_plShadow)
                         .SetRootArguments(args)
                         .Draw((uint)CubeMesh.VertexCount, (uint)_extractor.InstanceCount)
                         .EndRendering()
                         .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                         .CopyTextureToBuffer(_shadowRt, _shadowBuf);
              });

            rg.AddPass("MainPass", PassQueue.Graphics)
              .Read(hVerts).Read(hInsts).Read(hShadow)
              .Write(hRead, ResourceUsage.CopyDest)
              .Execute(ctx =>
              {
                  var args = new MainArgs
                  {
                      ViewProj = Matrix4x4.Transpose(viewProj),
                      LightVP = Matrix4x4.Transpose(lightVP),
                      VertexBufIndex = ctx.BindlessIndex(hVerts),
                      InstanceBufIndex = ctx.BindlessIndex(hInsts),
                      ShadowBufIndex = ctx.BindlessIndex(hShadow),
                      ShadowW = ShadowW,
                      ShadowH = ShadowH,
                  };
                  ctx.Cmd.BeginRendering(Target, _mainDepth, 0.10f, 0.12f, 0.18f, 1f, 1f)
                         .SetGraphicsPipeline(_plMain)
                         .SetRootArguments(args)
                         .Draw((uint)CubeMesh.VertexCount, (uint)_extractor.InstanceCount)
                         .EndRendering()
                         .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                         .CopyTextureToBuffer(Target, OutBuffer);
              });

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            rg.Execute(cmd);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }
}
