using System.Numerics;
using System.Runtime.InteropServices;
using Friflo.Engine.ECS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Luxel;
using Luxel.Ecs;
using Luxel.Framework;
using Luxel.Input;
using Luxel.RenderGraph;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Phase = Luxel.Framework.Phase;
using World = Luxel.Ecs.World;

namespace Luxel.Samples;

/// <summary>
/// Sample 91 (FW-M3): Luxel.Framework で書いた WASD cube demo。
/// MainScene は <see cref="GameScene"/> を継承し、標準 Phase の virtual メソッドを override。
/// PreRender phase の重い ECS 処理 (TransformPropagate / Render3DExtract) は World 側の System として登録。
/// </summary>
public static class Sample91Framework
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 91: Luxel.Framework で WASD cube demo ===");

        var host = LuxelHostBuilder.Create()
            .UseGpu(createDevice)
            .ConfigureServices(s =>
            {
                s.AddSingleton<FakeInputSource>();
                s.AddSingleton<IInputSource>(sp => sp.GetRequiredService<FakeInputSource>());
                s.AddSingleton<MainScene>();
            })
            .AddScene<MainScene>()
            .ConfigureServices(s => s.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(3)))
            .Build();

        var scene = host.Services.GetRequiredService<MainScene>();

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        host.RunAsync(cts.Token).GetAwaiter().GetResult();

        Console.WriteLine($"  frames rendered: {scene.FrameCount}, cube moved: {scene.CubeMoved}");
        bool ok = scene.FrameCount > 3 && scene.CubeMoved;
        Console.WriteLine(ok ? "OK: Framework の Host + Scene + phase system が動作" : "FAILED");
        return ok ? 0 : 1;
    }

    /// <summary>コンストラクタ注入で依存を受け取るユーザーシーン。</summary>
    public sealed class MainScene : GameScene
    {
        private readonly GpuDevice _device;
        private readonly World _world;
        private readonly InputStack _stack;
        private readonly FakeInputSource _fake;

        public int FrameCount { get; private set; }
        public bool CubeMoved { get; private set; }

        private Entity _cube;
        private GpuBuffer _vbuf = null!;
        private Vector3 _pos;
        private Axis2DAction _move = null!;
        private Render3DExtractSystem _extractor = null!;
        private GpuPipeline _pipeline = null!;
        private GpuTexture _rt = null!, _depth = null!;
        private GpuBuffer _readback = null!;

        public MainScene(SceneLoopServices loop, World world, InputStack stack, FakeInputSource fake)
            : base(loop)
        {
            _device = loop.Device; _world = world; _stack = stack; _fake = fake;

            // === World に標準 System を登録 ─ GameScene が Phase 名で自動 RunPhase する ===
            AddWorld(_world);
            _world.AddSystem(Phase.PreRender.Name, () => TransformPropagateSystem.Run(_world));
            _extractor = new Render3DExtractSystem(_world, _device);
            _world.AddSystem(Phase.PreRender.Name, _extractor);

            // === Input ===
            var inputCtx = new InputContext("gameplay");
            _move = inputCtx.Add(new Axis2DAction("Move"));
            _move.ButtonQuads.Add((KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D));
            _stack.Push(inputCtx);

            // === Cube entity ===
            _cube = _world.CreateEntity(
                new Luxel.Ecs.LocalTransform(Matrix4x4.Identity),
                new Luxel.Ecs.Color3D(new Vector4(0.30f, 0.70f, 0.95f, 1f)),
                new Luxel.Ecs.MeshRef(0));

            var verts = CubeMesh.Vertices.ToArray();
            _vbuf = _device.Malloc((ulong)(verts.Length * CubeMesh.VertexStride), GpuMemoryKind.HostMapped);
            verts.CopyTo(_vbuf.Span<CubeMesh.Vertex>(verts.Length));

            const uint W = 256, H = 256;
            _rt = _device.CreateRenderTarget(W, H, GpuFormat.Rgba8Unorm);
            _depth = _device.CreateDepthTarget(W, H);
            _readback = _device.Malloc(W * H * 4, GpuMemoryKind.HostMapped);
            var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
            raster.DepthTest = true; raster.DepthWrite = true;
            _pipeline = _device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster);
        }

        // ==================== 標準 Phase フック ====================

        protected override void OnEarlyUpdate(EarlyUpdateContext ctx)
        {
            if (ctx.Time.Frame == 0) _fake.PressKey(KeyCode.W);
            else if (ctx.Time.Frame == 3) _fake.ReleaseKey(KeyCode.W);
        }

        protected override void OnUpdate(UpdateContext ctx)
        {
            var v = _move.Value.Value;
            if (v.LengthSquared() > 0)
            {
                _pos += new Vector3(v.X, 0, v.Y) * 0.3f;
                CubeMoved = true;
            }
            _cube.RemoveComponent<Luxel.Ecs.LocalTransform>();
            _cube.AddComponent(new Luxel.Ecs.LocalTransform(Matrix4x4.CreateTranslation(_pos)));
        }

        protected override void OnRender(RenderContext ctx)
        {
            var rg = ctx.RenderGraph;
            BufferHandle hV = rg.ImportBuffer(_vbuf, "verts");
            BufferHandle hInst = rg.ImportBuffer(_extractor.InstanceBuffer, "insts");
            rg.AddPass("Render3D", PassQueue.Graphics).Read(hV).Read(hInst).Write(hInst)
              .Execute(pctx =>
              {
                  var view = Matrix4x4.CreateLookAt(new Vector3(0, 3, -5), Vector3.Zero, Vector3.UnitY);
                  var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
                  var args = new DrawArgs
                  {
                      ViewProj = Matrix4x4.Transpose(view * proj),
                      VertexBufIndex = pctx.BindlessIndex(hV),
                      InstanceBufIndex = pctx.BindlessIndex(hInst),
                  };
                  pctx.Cmd.BeginRendering(_rt, _depth, 0.05f, 0.06f, 0.09f, 1f, 1f)
                          .SetGraphicsPipeline(_pipeline)
                          .SetRootArguments(args)
                          .Draw((uint)CubeMesh.VertexCount, (uint)_extractor.InstanceCount)
                          .EndRendering();
                  pctx.Cmd.Barrier(GpuStage.ColorOutput, GpuStage.Copy).CopyTextureToBuffer(_rt, _readback);
              });
            FrameCount++;
        }

        public override Task OnUnloadAsync()
        {
            _pipeline?.Dispose(); _rt?.Dispose(); _depth?.Dispose();
            _readback?.Dispose(); _vbuf?.Dispose(); _extractor?.Dispose();
            return Task.CompletedTask;
        }
    }
}
