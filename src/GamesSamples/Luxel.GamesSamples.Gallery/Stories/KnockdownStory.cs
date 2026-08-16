using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;   // Store.CreateEntity の 4 成分以上のオーバーロード
using Luxel.AssetRuntime;
using Luxel.Ecs;
using Luxel.Framework.Game;
using Luxel.Physics;
using Luxel.Graphics.RenderGraph;
using Luxel.Graphics.RenderSystem;
using Luxel.UI;
using Microsoft.Extensions.DependencyInjection;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **3D ショーケースゲーム: KNOCKDOWN (射的)** — 3D スタックを実 Framework アプリで:
/// - <b>物理</b>: Luxel.Physics (BepuPhysics v2)。箱タワー + 発射体、GameScene の FixedUpdate 固定刻み
/// - <b>3D 描画</b>: ECS (LocalTransform/Color3D/MeshRef) → TransformPropagate → Render3DExtract →
///   RenderGraph の cube_forward パス → Target texture → framebuffer へコピー (シェーダ変更ゼロ)
/// - <b>Framework</b>: IGameScene のフェーズでシミュレーション、IRenderFeature で描画。GPU 資源は最初のフレームで遅延生成
/// - <b>Storybook Platform</b>: <see cref="StoryAppView{TScene}"/> — GPU ホスト借用、入力転送
/// 操作: ドラッグ = カメラ軌道、クリック = 弾を発射 (最初のクリックでシミュレーション開始)。
/// 初期状態は物理を止めた静止タワー = snap 決定的。
/// </summary>
[StoryMeta("Apps/Game")]
public static class KnockdownStories
{
    [Story]
    public static StoryResult Knockdown(StoryContext ctx)
        => ctx.Snap(VStack(8)[
            new StoryAppView<KnockdownScene>(KnockdownScene.W, KnockdownScene.H, (s, bctx) =>
                s.AddSingleton<Action<string>>(ctx.Log)),
            Muted("Drag: orbit camera / Click: shoot — Physics (Bepu) + ECS 3D + RenderGraph")
        ]);

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    private static readonly Vector4[] Palette =
    [
        new(1.00f, 0.40f, 0.40f, 1f),
        new(0.40f, 0.95f, 0.55f, 1f),
        new(0.35f, 0.70f, 1.00f, 1f),
        new(1.00f, 0.90f, 0.35f, 1f),
        new(0.90f, 0.55f, 1.00f, 1f),
    ];

    public sealed class KnockdownScene : StorySceneBase
    {
        public const uint W = 512, H = 320;   // 幅 512 = 行 2048B (256B 整列) — D3D12 の CopyTextureToBuffer 要件
        private const int TowerCols = 3, TowerRows = 5;
        private const int TowerCount = TowerCols * TowerCols * TowerRows;
        private const int MaxShots = 24;   // 弾はエンティティ削除しない — 上限で場ごと再構築

        private readonly Action<string> _log;

        // シミュレーション (再構築可能な一式)
        private Luxel.Ecs.World _world = null!;
        private PhysicsWorld? _physics;
        private PhysicsStepSystem _step = null!;
        private Render3DExtractSystem? _extractor;
        private readonly List<Entity> _towerBoxes = new();
        private int _shots, _knocked;
        private bool _running;          // 最初のクリックまで物理停止 = 初期絵が決定的

        // カメラ (軌道)
        private float _camYaw = -0.6f, _camPitch = 0.34f, _camDist = 8.2f;
        private static readonly Vector3 CamTarget = new(0, -0.2f, 0);
        private float _dragX, _dragY, _dragTotal;
        private bool _dragging;
        private (float x, float y)? _shotQueued;

        // GPU (最初のフレームで遅延生成)
        private GpuBuffer? _vb, _fb;
        private GpuTexture? _target, _depth;
        private GpuPipeline? _pipeline;
        private bool _fbDirty = true;

        public KnockdownScene(GpuDevice device, Action<string> log) : base(device) => _log = log;

        // ---- IStoryApp ----

        public override uint FbIndex => _fb?.BindlessIndex ?? 0;

        public override void PointerDown(float x, float y)
        {
            _dragging = true;
            _dragX = x; _dragY = y; _dragTotal = 0;
        }

        public override void PointerMove(float x, float y)
        {
            if (!_dragging) return;
            float dx = x - _dragX, dy = y - _dragY;
            _dragTotal += MathF.Abs(dx) + MathF.Abs(dy);
            _camYaw -= dx * 0.008f;
            _camPitch = Math.Clamp(_camPitch + dy * 0.006f, 0.05f, 1.2f);
            _dragX = x; _dragY = y;
            _fbDirty = true;
        }

        public override void PointerUp(float x, float y)
        {
            if (_dragging && _dragTotal < 4) _shotQueued = (x, y);   // ほぼ動いていない = クリック = 発射
            _dragging = false;
        }

        public override void Wheel(float x, float y, float d)
        {
            _camDist = Math.Clamp(_camDist - d * 0.01f, 4f, 14f);
            _fbDirty = true;
        }

        // ---- フレーム ----

        // FixedUpdate = 物理刻み。GameScene の蓄積器に一本化 (手書き accumulator を廃止)。
        public override void FixedUpdate(in FixedUpdateContext ctx)
        {
            if (!_running || _physics is null) return;   // 最初の発射まで静止 (初期絵が決定的)
            _step.StepFixedOnce();
            TransformPropagateSystem.Run(_world);
            _fbDirty = true;
            CountKnocked();
        }

        public override void Update(in UpdateContext ctx)
        {
            if (_fb is null) InitGpu();

            if (_shotQueued is (float sx, float sy))
            {
                _shotQueued = null;
                Shoot(sx, sy);
            }
        }

        protected override void AddRenderPasses(RenderFeatureContext context)
        {
            if (_fb is null || _extractor is null || !_fbDirty) return;
            _fbDirty = false;
            _extractor.Extract();

            Vector3 eye = CamTarget + new Vector3(
                MathF.Cos(_camPitch) * MathF.Sin(_camYaw),
                MathF.Sin(_camPitch),
                MathF.Cos(_camPitch) * MathF.Cos(_camYaw)) * _camDist;
            Matrix4x4 view = Matrix4x4.CreateLookAt(eye, CamTarget, Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.4f, (float)W / H, 0.1f, 100f);
            Matrix4x4 viewProj = view * proj;

            BufferHandle hVerts = context.Graph.ImportBuffer(_vb!, "verts");
            BufferHandle hInsts = context.Graph.ImportBuffer(_extractor.InstanceBuffer, "instances");
            BufferHandle output = context.Graph.ImportBuffer(_fb, "knockdown-framebuffer");
            TextureHandle color = context.Graph.ImportTexture(_target!, "knockdown-color");
            TextureHandle depth = context.Graph.ImportTexture(_depth!, "knockdown-depth");
            context.Graph.AddPass("Knockdown3D", PassQueue.Graphics)
                .Read(hVerts).Read(hInsts)
                .Write(color).Write(depth).Write(output)
                .Execute(pctx =>
                {
                    var args = new DrawArgs
                    {
                        ViewProj = Matrix4x4.Transpose(viewProj),
                        VertexBufIndex = pctx.BindlessIndex(hVerts),
                        InstanceBufIndex = pctx.BindlessIndex(hInsts),
                    };
                    pctx.Cmd.BeginRendering(_target!, _depth!, 0.05f, 0.06f, 0.09f, 1f, 1f)
                        .SetGraphicsPipeline(_pipeline!)
                        .SetRootArguments(args)
                        .Draw((uint)Luxel.Assets.CubeMesh.VertexCount, (uint)_extractor.InstanceCount)
                        .EndRendering()
                        .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                        .CopyTextureToBuffer(_target!, _fb!);
                    MarkRendered();
                });
        }

        protected override ValueTask DisposeAsync()
        {
            _extractor?.Dispose();
            _physics?.Dispose();
            _pipeline?.Dispose();
            _depth?.Dispose();
            _target?.Dispose();
            _vb?.Dispose();
            _fb?.Dispose();
            _extractor = null; _physics = null; _pipeline = null;
            _depth = null; _target = null; _vb = null; _fb = null;
            return ValueTask.CompletedTask;
        }

        // ---- 初期化 / 再構築 ----

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
            BuildSimulation();
        }

        /// <summary>World + PhysicsWorld を (再) 構築する。配置はハードコード = 決定的。
        /// 物理は最初のクリックまで停止 (_running=false) — 初期絵が snap 決定的になる。</summary>
        private void BuildSimulation()
        {
            _extractor?.Dispose();
            _physics?.Dispose();
            _world = new Luxel.Ecs.World();
            _physics = new PhysicsWorld();
            _step = new PhysicsStepSystem(_world, _physics);
            _towerBoxes.Clear();
            _shots = 0;
            _knocked = 0;
            _running = false;

            // 台座 (静的、少し高い位置 — 落とされた箱は奈落へ)
            _world.Store.CreateEntity(
                new LocalTransform(Matrix4x4.CreateScale(5.2f, 0.35f, 5.2f) * Matrix4x4.CreateTranslation(0, -1.8f, 0)),
                new Color3D(new Vector4(0.80f, 0.78f, 0.74f, 1f)),
                new MeshRef(MeshRef.Cube),
                Collider.Box(5.2f, 0.35f, 5.2f),
                new StaticBody());

            // 箱タワー 3×3×5 (整列 = クリックまで静止して見える)
            for (int layer = 0; layer < TowerRows; layer++)
                for (int gz = 0; gz < TowerCols; gz++)
                    for (int gx = 0; gx < TowerCols; gx++)
                    {
                        int i = layer * 9 + gz * 3 + gx;
                        var pos = new Vector3((gx - 1) * 0.66f, -1.29f + layer * 0.64f, (gz - 1) * 0.66f);
                        Entity e = _world.Store.CreateEntity(
                            new LocalTransform(Matrix4x4.CreateScale(0.6f) * Matrix4x4.CreateTranslation(pos)),
                            new Color3D(Palette[(layer + gx + gz) % Palette.Length]),
                            new MeshRef(MeshRef.Cube),
                            Collider.Box(0.6f, 0.6f, 0.6f),
                            RigidBody.Dynamic());
                        _towerBoxes.Add(e);
                    }

            TransformPropagateSystem.Run(_world);
            _extractor = new Render3DExtractSystem(_world, Device);
            _fbDirty = true;
        }

        // ---- ゲーム ----

        /// <summary>スクリーン座標から視線レイを作り、カメラ位置から弾 (動的な小箱) を撃つ。</summary>
        private void Shoot(float px, float py)
        {
            if (_shots >= MaxShots)
            {
                _log($"reload — rebuilt (knocked {_knocked}/{TowerCount})");
                BuildSimulation();
                return;
            }
            _shots++;
            _running = true;   // 最初の発射でシミュレーション開始

            Vector3 eye = CamTarget + new Vector3(
                MathF.Cos(_camPitch) * MathF.Sin(_camYaw),
                MathF.Sin(_camPitch),
                MathF.Cos(_camPitch) * MathF.Cos(_camYaw)) * _camDist;
            Matrix4x4 view = Matrix4x4.CreateLookAt(eye, CamTarget, Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3.4f, (float)W / H, 0.1f, 100f);
            if (!Matrix4x4.Invert(view * proj, out Matrix4x4 inv)) return;

            // NDC (y 上向き) → ワールドレイ
            float nx = 2f * px / W - 1f;
            float ny = 1f - 2f * py / H;
            Vector4 near4 = Vector4.Transform(new Vector4(nx, ny, 0.05f, 1f), inv);
            Vector4 far4 = Vector4.Transform(new Vector4(nx, ny, 0.95f, 1f), inv);
            Vector3 dir = Vector3.Normalize(new Vector3(far4.X, far4.Y, far4.Z) / far4.W
                                          - new Vector3(near4.X, near4.Y, near4.Z) / near4.W);

            Vector3 spawn = eye + dir * 0.8f;
            _world.Store.CreateEntity(
                new LocalTransform(Matrix4x4.CreateScale(0.38f) * Matrix4x4.CreateTranslation(spawn)),
                new Color3D(new Vector4(0.95f, 0.96f, 1f, 1f)),
                new MeshRef(MeshRef.Cube),
                Collider.Box(0.38f, 0.38f, 0.38f),
                RigidBody.Dynamic(mass: 2.2f, initialVelocity: dir * 16f));
            _fbDirty = true;
            _log($"shot {_shots}/{MaxShots}");
        }

        /// <summary>台座から落ちた箱を数える (y < -3)。増えたらログ、全滅で再構築。</summary>
        private void CountKnocked()
        {
            int n = 0;
            foreach (Entity e in _towerBoxes)
                if (e.GetComponent<GlobalTransform>().Matrix.Translation.Y < -3f) n++;
            if (n != _knocked)
            {
                _knocked = n;
                _log($"knocked {_knocked}/{TowerCount}");
                if (_knocked >= TowerCount)
                {
                    _log("tower cleared! — rebuilt");
                    BuildSimulation();
                }
            }
        }
    }
}
