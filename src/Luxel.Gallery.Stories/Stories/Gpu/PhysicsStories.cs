using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;   // Store.CreateEntity の 4 成分以上のオーバーロード
using Luxel.AssetRuntime;
// Friflo にも Signal<TEvent> があるため、UI の Signal は完全修飾で書く
using Luxel.Ecs;
using Luxel.Physics;
using Luxel.Graphics.RenderGraph;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// 物理 (Luxel.Physics = BepuPhysics v2) のデモ — Collider + RigidBody を持つ entity を
/// PhysicsStepSystem が毎フレーム進めて LocalTransform を書き、既存の
/// TransformPropagate → Render3DExtract → cube_forward がそのまま描く (シェーダ変更ゼロ)。
/// 単スレッド固定タイムステップなので snap でも決定的。
/// </summary>
public static class PhysicsStories
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    /// <summary>箱タワーが崩れて積み上がる — 物理パイプラインの最小構成。</summary>
    [Story("Examples/3D/PhysicsFalling", Height = 320, Order = 127)]
    public static Widget PhysicsFalling(StoryContext ctx) => ctx.Snap(Frame(GpuSceneBase.View(256, 256, new PhysicsScene())));

    /// <summary>knob で重力/跳ね返りを実行時に変更、reset トグルで決定的な初期状態へ再構築。</summary>
    [Story("Examples/3D/PhysicsPlayground", Height = 320, Order = 128)]
    public static Widget PhysicsPlayground(StoryContext ctx)
    {
        Luxel.UI.Signal<float> gravity = ctx.Signal("gravity", 9.8f, "下向き重力の強さ (m/s²)");
        Luxel.UI.Signal<float> bounciness = ctx.Signal("bounciness", 2f, "接触の反発上限 (MaximumRecoveryVelocity, m/s)");
        Luxel.UI.Signal<bool> reset = ctx.Signal("reset", false, "トグルするとシーンを初期状態へ再構築");
        return Frame(GpuSceneBase.View(256, 256, new PhysicsScene(gravity, bounciness, reset)));
    }

    private static readonly Vector4[] Palette =
    [
        new(1.00f, 0.40f, 0.40f, 1f),
        new(0.40f, 0.95f, 0.55f, 1f),
        new(0.35f, 0.70f, 1.00f, 1f),
        new(1.00f, 0.90f, 0.35f, 1f),
        new(0.90f, 0.55f, 1.00f, 1f),
    ];

    /// <summary>reset で作り直す資源の入れ物 — Track はこのスロットを 1 回だけ登録し、
    /// 差し替え時はスロットが旧値を破棄する (二重 Dispose もリークも起きない)。</summary>
    private sealed class Slot<T> : IDisposable where T : class, IDisposable
    {
        private T? _value;
        public T Value
        {
            get => _value!;
            set { _value?.Dispose(); _value = value; }
        }
        public void Dispose() { _value?.Dispose(); _value = null; }
    }

    /// <summary>床 + 箱タワー 3×3×4 の物理シーン。knob (nullable) があれば毎フレーム反映。</summary>
    private sealed class PhysicsScene(
        Luxel.UI.Signal<float>? gravity = null,
        Luxel.UI.Signal<float>? bounciness = null,
        Luxel.UI.Signal<bool>? reset = null) : GpuSceneBase
    {
        private Luxel.Ecs.World _world = null!;
        private readonly Slot<PhysicsWorld> _physicsSlot = new();
        private readonly Slot<Render3DExtractSystem> _extractorSlot = new();
        private PhysicsWorld _physics => _physicsSlot.Value;
        private Render3DExtractSystem _extractor => _extractorSlot.Value;
        private PhysicsStepSystem _step = null!;
        private GpuBuffer _vb = null!;
        private GpuTexture _depth = null!;
        private GpuPipeline _pipeline = null!;
        private float _lastTime = float.NaN;
        private bool _lastReset;

        protected override bool RenderEveryFrame => true;

        protected override void OnInit()
        {
            Track(_physicsSlot);
            Track(_extractorSlot);
            BuildSimulation();
            _vb = Track(Device.Malloc((ulong)(Luxel.Assets.CubeMesh.Vertices.Length * Luxel.Assets.CubeMesh.VertexStride),
                GpuMemoryKind.HostMapped));
            Luxel.Assets.CubeMesh.Vertices.CopyTo(_vb.Span<Luxel.Assets.CubeMesh.Vertex>(Luxel.Assets.CubeMesh.Vertices.Length));
            _depth = Track(Device.CreateDepthTarget(W, H));
            var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
            raster.DepthTest = true;
            raster.DepthWrite = true;
            _pipeline = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster));
        }

        /// <summary>World + PhysicsWorld を (再) 構築する。配置はシード無しハードコード = 決定的。</summary>
        private void BuildSimulation()
        {
            _world = new Luxel.Ecs.World();
            _physicsSlot.Value = new PhysicsWorld();   // 旧値はスロットが破棄
            _step = new PhysicsStepSystem(_world, _physics);

            // 床 (静的な薄い箱)。4 成分以上は Friflo の Store を直接使う (World ラッパは 3 まで)
            _world.Store.CreateEntity(
                new LocalTransform(Matrix4x4.CreateScale(8f, 0.2f, 8f) * Matrix4x4.CreateTranslation(0, -1.6f, 0)),
                new Color3D(new Vector4(0.85f, 0.82f, 0.78f, 1f)),
                new MeshRef(MeshRef.Cube),
                Collider.Box(8f, 0.2f, 8f),
                new StaticBody());

            // 箱タワー 3×3×4 — わずかな固定オフセット/回転で崩れを演出 (シード無し = 決定的)
            for (int layer = 0; layer < 4; layer++)
            {
                for (int gz = 0; gz < 3; gz++)
                {
                    for (int gx = 0; gx < 3; gx++)
                    {
                        int i = layer * 9 + gz * 3 + gx;
                        float jitter = (i % 5 - 2) * 0.04f;   // 決定的な散らし
                        var pos = new Vector3((gx - 1) * 0.62f + jitter, -0.9f + layer * 0.62f, (gz - 1) * 0.62f - jitter);
                        Quaternion rot = Quaternion.CreateFromYawPitchRoll(i * 0.11f, 0, 0);
                        _world.Store.CreateEntity(
                            new LocalTransform(Matrix4x4.CreateScale(0.55f)
                                             * Matrix4x4.CreateFromQuaternion(rot)
                                             * Matrix4x4.CreateTranslation(pos)),
                            new Color3D(Palette[i % Palette.Length]),
                            new MeshRef(MeshRef.Cube),
                            Collider.Box(0.55f, 0.55f, 0.55f),
                            RigidBody.Dynamic());
                    }
                }
            }
            TransformPropagateSystem.Run(_world);

            _extractorSlot.Value = new Render3DExtractSystem(_world, Device);
            _extractor.Extract();
        }

        protected override void OnRender(float time)
        {
            // reset トグル: World/PhysicsWorld を破棄して決定的な初期状態へ
            if (reset is not null && reset.Value != _lastReset)
            {
                _lastReset = reset.Value;
                BuildSimulation();
                _lastTime = time;
            }
            if (gravity is not null) _physics.Gravity = new Vector3(0, -MathF.Max(0, gravity.Value), 0);
            if (bounciness is not null) _physics.Bounciness = MathF.Max(0, bounciness.Value);

            // dt = 累積秒の差分 (NaN/巻き戻りは 0 — Dispose→再 Init 対応)。snap は 1/60 固定刻み = 決定的
            float dt = float.IsNaN(_lastTime) || time < _lastTime ? 0f : time - _lastTime;
            _lastTime = time;
            _step.Run(dt);
            TransformPropagateSystem.Run(_world);
            _extractor.Extract();

            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(3.4f, 2.2f, -4.6f), new Vector3(0, -0.4f, 0), Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
            Matrix4x4 viewProj = view * proj;

            using var rg = new Luxel.Graphics.RenderGraph.RenderGraph(Device);
            BufferHandle hVerts = rg.ImportBuffer(_vb, "verts");
            BufferHandle hInsts = rg.ImportBuffer(_extractor.InstanceBuffer, "instances");
            rg.AddPass("Render3D", PassQueue.Graphics)
              .Read(hVerts).Read(hInsts)
              .Write(hInsts)   // 「使用」の宣言 — Write が無いパスはデッドパスカリングされる
              .Execute(ctx =>
              {
                  var args = new DrawArgs
                  {
                      ViewProj = Matrix4x4.Transpose(viewProj),
                      VertexBufIndex = ctx.BindlessIndex(hVerts),
                      InstanceBufIndex = ctx.BindlessIndex(hInsts),
                  };
                  ctx.Cmd.BeginRendering(Target, _depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                         .SetGraphicsPipeline(_pipeline)
                         .SetRootArguments(args)
                         .Draw((uint)Luxel.Assets.CubeMesh.VertexCount, (uint)_extractor.InstanceCount)
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
}
