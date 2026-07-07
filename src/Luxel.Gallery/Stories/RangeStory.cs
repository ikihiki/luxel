using System.Numerics;
using System.Runtime.InteropServices;
using Luxel;
using Luxel.AssetRuntime;
using Luxel.Ecs;
using Luxel.Framework;
using Luxel.RenderGraph;
using Luxel.UI;
using LuxelRange.Core;
using Microsoft.Extensions.DependencyInjection;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **3D capstone ②「LUXEL RANGE」(射的) — 縦切り 1** — 03 (CCD) + 04 (接触イベント) + 17 (OrbitCamera) の統合。
/// <see cref="RangeSim"/> (純ロジック) をホストし、<see cref="OrbitCamera"/> でアリーナを見回し、クリックで
/// CCD 弾を発射。薄板ターゲット命中で ContactBegin → スコア加算。描画は ECS → Render3DExtract → cube_forward。
/// 初期は物理停止 (最初のクリックまで) = snap 決定的。
/// メッシュアリーナ (05) / 動く的 (09) / パーティクル・音・UI・Title/Result は後続スライス。
/// </summary>
public static class RangeStories
{
    [Story("Apps/Game/Range", Width = 520, Height = 420, Order = 149)]
    public static Widget Range(StoryContext ctx)
        => ctx.Snap(VStack(8)[
            new StoryAppView<RangeScene>(RangeScene.W, RangeScene.H, (s, bctx) =>
                s.AddSingleton<Action<string>>(ctx.Log)),
            Muted("Drag: orbit camera / Click: shoot (CCD) — 薄板ターゲット命中で +100")
        ]);

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PbrDrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint IndexBufIndex;
        public uint InstanceBufIndex;
        public uint InstanceStart;
    }

    public sealed class RangeScene : GameScene, IStoryApp
    {
        public const uint W = 512, H = 320;   // 512*4 = 2048B 行 (256B 整列) — D3D12 CopyTextureToBuffer 要件

        private readonly Action<string> _log;

        private RangeSim _sim = null!;
        private Render3DExtractSystem? _extractor;
        private int _lastScore;

        // 軌道カメラ (的の前面から見下ろす初期姿勢)
        private OrbitCamera _cam = new(
            target: new Vector3(0, 0.8f, -6f), yaw: 0f, pitch: 0.40f, distance: 18f,
            fovYRadians: MathF.PI / 3.4f, aspect: (float)W / H);
        private float _dragX, _dragY, _dragTotal;
        private bool _dragging;
        private (float x, float y)? _shotQueued;

        private GpuBuffer? _vb, _fb;
        private GpuBuffer? _terrainVb, _terrainIb, _terrainInst;
        private int _terrainIndexCount;
        private GpuTexture? _target, _depth;
        private GpuPipeline? _pipeline, _pbrPipeline;
        private long _version, _seen;
        private bool _fbDirty = true;

        public RangeScene(SceneLoopServices loop, Action<string> log) : base(loop) => _log = log;

        // ---- IStoryApp ----
        public uint FbIndex => _fb?.BindlessIndex ?? 0;
        public bool FbReady => _version > 0;
        public bool ConsumeRendered()
        {
            if (_seen == _version) return false;
            _seen = _version;
            return true;
        }

        public void PointerDown(float x, float y) { _dragging = true; _dragX = x; _dragY = y; _dragTotal = 0; }

        public void PointerMove(float x, float y)
        {
            if (!_dragging) return;
            float dx = x - _dragX, dy = y - _dragY;
            _dragTotal += MathF.Abs(dx) + MathF.Abs(dy);
            _cam.Orbit(-dx * 0.008f, dy * 0.006f);
            _dragX = x; _dragY = y;
            _fbDirty = true;
        }

        public void PointerUp(float x, float y)
        {
            if (_dragging && _dragTotal < 4) _shotQueued = (x, y);   // ほぼ動かない = クリック = 発射
            _dragging = false;
        }

        public void Wheel(float x, float y, float d) { _cam.Dolly(1f - d * 0.001f, 5f, 20f); _fbDirty = true; }

        // ---- フレーム ----
        protected override double FixedDeltaSeconds => RangeSim.FixedDt;

        protected override void OnFixedUpdate(FixedUpdateContext ctx)
        {
            if (_sim is null || !_sim.Started) return;   // InitGpu (OnUpdate) 前は _sim 未生成
            _sim.StepOnce();
            TransformPropagateSystem.Run(_sim.World);
            _fbDirty = true;
            if (_sim.Score != _lastScore)
            {
                _lastScore = _sim.Score;
                _log($"score {_sim.Score}  (hits {_sim.TargetsHit}/{_sim.TargetCount}, ammo {_sim.AmmoLeft})");
            }
        }

        protected override void OnUpdate(UpdateContext ctx)
        {
            if (_fb is null) InitGpu();
            if (_shotQueued is (float sx, float sy)) { _shotQueued = null; Shoot(sx, sy); }
        }

        protected override void OnRender(RenderContext ctx)
        {
            if (_fb is null || _extractor is null || !_fbDirty) return;
            _fbDirty = false;
            _extractor.Extract();

            Matrix4x4 viewProj = _cam.ViewProjection;
            Matrix4x4 vpT = Matrix4x4.Transpose(viewProj);
            using var rg = new Luxel.RenderGraph.RenderGraph(Device);
            BufferHandle hVerts = rg.ImportBuffer(_vb!, "verts");
            BufferHandle hInsts = rg.ImportBuffer(_extractor.InstanceBuffer, "instances");
            BufferHandle hTV = rg.ImportBuffer(_terrainVb!, "terrainVerts");
            BufferHandle hTI = rg.ImportBuffer(_terrainIb!, "terrainIdx");
            BufferHandle hTInst = rg.ImportBuffer(_terrainInst!, "terrainInst");
            rg.AddPass("Range3D", PassQueue.Graphics)
              .Read(hVerts).Read(hInsts).Read(hTV).Read(hTI).Read(hTInst).Write(hInsts)   // Write 無しパスはデッドパスカリング
              .Execute(pctx =>
              {
                  // 1) 起伏メッシュ地形 (scene_pbr_lite)
                  pctx.Cmd.BeginRendering(_target!, _depth!, 0.05f, 0.06f, 0.09f, 1f, 1f)
                          .SetGraphicsPipeline(_pbrPipeline!)
                          .SetRootArguments(new PbrDrawArgs
                          {
                              ViewProj = vpT,
                              VertexBufIndex = pctx.BindlessIndex(hTV),
                              IndexBufIndex = pctx.BindlessIndex(hTI),
                              InstanceBufIndex = pctx.BindlessIndex(hTInst),
                              InstanceStart = 0,
                          })
                          .Draw((uint)_terrainIndexCount, 1);
                  // 2) 的/弾 (cube_forward)
                  pctx.Cmd.SetGraphicsPipeline(_pipeline!)
                          .SetRootArguments(new DrawArgs
                          {
                              ViewProj = vpT,
                              VertexBufIndex = pctx.BindlessIndex(hVerts),
                              InstanceBufIndex = pctx.BindlessIndex(hInsts),
                          })
                          .Draw((uint)Luxel.Assets.CubeMesh.VertexCount, (uint)_extractor.InstanceCount)
                          .EndRendering();
              });

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            rg.Execute(cmd);
            cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy)
               .CopyTextureToBuffer(_target!, _fb!);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
            _version++;
        }

        public override Task OnUnloadAsync()
        {
            _extractor?.Dispose();
            _sim?.Dispose();
            _pipeline?.Dispose();
            _pbrPipeline?.Dispose();
            _depth?.Dispose();
            _target?.Dispose();
            _vb?.Dispose();
            _fb?.Dispose();
            _terrainVb?.Dispose();
            _terrainIb?.Dispose();
            _terrainInst?.Dispose();
            _extractor = null; _pipeline = null; _pbrPipeline = null; _depth = null; _target = null; _vb = null; _fb = null;
            _terrainVb = null; _terrainIb = null; _terrainInst = null;
            return Task.CompletedTask;
        }

        private void InitGpu()
        {
            _vb = Device.Malloc((ulong)(Luxel.Assets.CubeMesh.Vertices.Length * Luxel.Assets.CubeMesh.VertexStride),
                GpuMemoryKind.HostMapped);
            Luxel.Assets.CubeMesh.Vertices.CopyTo(_vb.Span<Luxel.Assets.CubeMesh.Vertex>(Luxel.Assets.CubeMesh.Vertices.Length));
            _target = Device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
            _depth = Device.CreateDepthTarget(W, H);
            _fb = Device.Malloc((ulong)W * H * 4, GpuMemoryKind.DeviceLocal);
            var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
            raster.DepthTest = true;
            raster.DepthWrite = true;
            _pipeline = Device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster);
            _pbrPipeline = Device.CreateGraphicsPipeline(GpuShaderCode.Load("scene_pbr_lite"), raster);

            _sim = new RangeSim();
            TransformPropagateSystem.Run(_sim.World);   // 初期 (静止) シーンの GlobalTransform
            _extractor = new Render3DExtractSystem(_sim.World, Device);
            BuildTerrainBuffers();
            _fbDirty = true;
        }

        /// <summary>RangeSim と同じ頂点で地形メッシュの GPU バッファを作る (絵 = 当たり)。scene_pbr_lite 用。</summary>
        private void BuildTerrainBuffers()
        {
            Vector3[] pos = _sim.TerrainPositions, nrm = _sim.TerrainNormals;
            int[] idx = _sim.TerrainIndices;
            _terrainIndexCount = idx.Length;

            _terrainVb = Device.Malloc((ulong)(pos.Length * SceneBuilder.Vertex.Stride), GpuMemoryKind.HostMapped);
            var vspan = _terrainVb.Span<SceneBuilder.Vertex>(pos.Length);
            for (int i = 0; i < pos.Length; i++)
                vspan[i] = new SceneBuilder.Vertex { Position = pos[i], Normal = nrm[i], TexCoord0 = Vector2.Zero };

            _terrainIb = Device.Malloc((ulong)(idx.Length * sizeof(uint)), GpuMemoryKind.HostMapped);
            var ispan = _terrainIb.Span<uint>(idx.Length);
            for (int i = 0; i < idx.Length; i++) ispan[i] = (uint)idx[i];

            _terrainInst = Device.Malloc((ulong)SceneInstanceData.Stride, GpuMemoryKind.HostMapped);
            _terrainInst.Span<SceneInstanceData>(1)[0] = new SceneInstanceData
            {
                World = Matrix4x4.Identity,
                BaseColor = new Vector4(0.34f, 0.42f, 0.32f, 1f),   // アリーナ地形 (くすんだ緑)
            };
        }

        /// <summary>スクリーン座標から視線レイを作り、カメラ位置から CCD 弾を撃つ。</summary>
        private void Shoot(float px, float py)
        {
            if (!Matrix4x4.Invert(_cam.ViewProjection, out Matrix4x4 inv)) return;
            float nx = 2f * px / W - 1f;
            float ny = 1f - 2f * py / H;
            Vector4 near4 = Vector4.Transform(new Vector4(nx, ny, 0.05f, 1f), inv);
            Vector4 far4 = Vector4.Transform(new Vector4(nx, ny, 0.95f, 1f), inv);
            Vector3 dir = Vector3.Normalize(new Vector3(far4.X, far4.Y, far4.Z) / far4.W
                                          - new Vector3(near4.X, near4.Y, near4.Z) / near4.W);
            Vector3 spawn = _cam.Eye + dir * 0.5f;
            if (_sim.Fire(spawn, dir))
            {
                _fbDirty = true;
                _log($"shot — ammo {_sim.AmmoLeft}");
            }
        }
    }
}
