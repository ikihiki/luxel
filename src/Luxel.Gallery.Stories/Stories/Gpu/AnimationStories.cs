using System.Numerics;
using System.Runtime.InteropServices;
using Luxel.Animation;
using Luxel.Animation.ThreeD;
using Luxel.Animation.TwoD;
using Luxel.Animation.UI;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.Ecs;
using Luxel.RenderGraph;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// アニメーションシステムのデモ — Curve × Tween の 2 段分解、コード DSL (Sequence/Parallel)、
/// AnimationClip (CSS @keyframes 由来含む)、StateMachine、AnimationGraph (BlendNode)。
/// すべて絶対時刻モデル (FixedFrameClock) — 時間はストーリーの累積秒から決める (snap 決定的)。
/// docs の Reference/Guides/Animation から参照される。
/// </summary>
public static class AnimationStories
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public Matrix4x4 ViewProj;
        public uint VertexBufIndex;
        public uint InstanceBufIndex;
        public uint Pad0, Pad1;
    }

    /// <summary>コード DSL: Sequence(Parallel(slide+fade), Parallel(slide+fade))。
    /// Signal へ SignalAnimationTarget 経由で書き、ループ毎に Play し直す。</summary>
    [Story("Examples/Animation/Tween", Height = 300, Order = 140)]
    public static Widget Tween()
    {
        var xA = new Signal<float>(-150f);
        var oA = new Signal<float>(0f);
        var xB = new Signal<float>(300f);
        var oB = new Signal<float>(0f);
        var clock = new FixedFrameClock { FrameRate = 60f };
        AnimationPlayer? player = null;
        int lastFrame = int.MaxValue;
        const float loop = 1.6f;   // 本編 0.8s + 余韻

        return Frame(Canvas2D(256, 128, animate: (s, t) =>
        {
            int frame = (int)(t % loop * 60);
            if (frame < lastFrame)
            {
                // 周回頭で組み直し (絶対時刻モデル — track は Play 時に開始時刻が固定される)
                player = new AnimationPlayer();
                clock.Frame = 0;
                player.Update(clock);
                Animate.Sequence(
                    Animate.Parallel(
                        Animate.Tween(SignalAnimationTarget.For(xA), -150f, 30f, 0.4f)
                               .WithCurve(CubicBezierCurve.EaseOut),
                        Animate.Tween(SignalAnimationTarget.For(oA), 0f, 1f, 0.4f)),
                    Animate.Parallel(
                        Animate.Tween(SignalAnimationTarget.For(xB), 300f, 130f, 0.4f)
                               .WithCurve(CubicBezierCurve.EaseOut),
                        Animate.Tween(SignalAnimationTarget.For(oB), 0f, 1f, 0.4f))
                ).Play(player, clock);
            }
            lastFrame = frame;
            clock.Frame = frame;
            player!.Update(clock);

            byte aA = (byte)(Math.Clamp(oA.Peek(), 0f, 1f) * 255);
            byte aB = (byte)(Math.Clamp(oB.Peek(), 0f, 1f) * 255);
            s.FillRoundedRect(Color2D.Rgba(60, 130, 240, aA), xA.Peek(), 20, 80, 40, 8);
            s.FillRoundedRect(Color2D.Rgba(230, 80, 100, aB), xB.Peek(), 68, 80, 40, 8);
        }));
    }

    /// <summary>CSS @keyframes → AnimationClip → RetainedCanvas ノードへ適用 (ループ再生)。</summary>
    [Story("Examples/Animation/CssKeyframes", Height = 300, Order = 141)]
    public static Widget CssKeyframes(StoryContext ctx) => ctx.Snap(Frame(GpuView(256, 128, new CssClipScene())));

    /// <summary>StateMachine (idle ⇄ jump、crossfade 0.15s)。ボタンで Trigger を送る —
    /// press でジャンプ (黄)、done で idle (青) へ戻る。</summary>
    [Story("Examples/Animation/StateMachine", Height = 340, Order = 142)]
    public static Widget StateMachineDemo(StoryContext ctx)
    {
        var scene = new StateMachineScene();
        return Frame(VStack(8)[
            HStack(8)[
                Button(_ => { scene.Trigger("press"); ctx.Log("trigger: press → jump"); }, "press"),
                Button(_ => { scene.Trigger("done"); ctx.Log("trigger: done → idle"); }, "done")],
            GpuView(256, 128, scene)]);
    }

    /// <summary>AnimationClip (translation + rotation) を EcsAnimationTarget で
    /// LocalTransform へ書き、毎フレーム propagate → extract → 描画。</summary>
    [Story("Examples/Animation/EcsClip", Height = 320, Order = 143)]
    public static Widget EcsClip() => Frame(GpuView(256, 256, new EcsClipScene()));

    /// <summary>AnimationGraph: BlendNode(上下振動, 左右振動)。weight は knob —
    /// 0 で上下のみ、1 で左右のみ、中間で混合。</summary>
    [Story("Examples/Animation/Graph", Height = 320, Order = 144)]
    public static Widget Graph(StoryContext ctx)
    {
        Signal<float> weight = ctx.Signal("weight", 0.5f, "Blend: 0 = 上下振動, 1 = 左右振動");
        return Frame(GpuView(256, 256, new GraphScene(weight)));
    }

    // ---- 2D (RetainedCanvas をオフスクリーンで所有する) シーン ----

    /// <summary>CSS @keyframes を CssKeyframesImporter でパースし、card ノードで再生。</summary>
    private sealed class CssClipScene : GpuSceneBase
    {
        private const string Css = """
            @keyframes slideAndFade {
              0%   { opacity: 0; transform: translateX(-120px); background-color: rgba(60,130,240,255); }
              50%  { opacity: 1; transform: translateX(0px);    }
              100% { opacity: 1; transform: translateX(80px);   background-color: rgba(230,80,100,255); }
            }
            """;

        private RetainedCanvas _canvas = null!;
        private IRasterScene2D _rasterScene = null!;
        private RetainedCanvasAnimationTarget _target = null!;
        private AnimationClip _clip = null!;
        private readonly FixedFrameClock _clock = new() { FrameRate = 60f };
        private AnimationPlayer? _player;
        private int _lastFrame;

        protected override bool NeedsColorTarget => false;
        protected override bool RenderEveryFrame => true;

        protected override void OnInit()
        {
            var raster = Track(new GpuDeviceRasterizer2D(Device));
            _canvas = Track(new RetainedCanvas());
            _rasterScene = Track(raster.CreateScene(_canvas));
            var card = _canvas.AddChild(_canvas.Root);
            card.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 80, 50, 10);
            card.Transform = Affine2D.Translate(0, 40);
            card.Color = Color2D.Rgba(60, 130, 240, 255);
            card.Opacity = 0f;

            _clip = CssKeyframesImporter.Parse(Css, targetPrefix: "card", durationSec: 1.0f,
                                               warnings: new List<string>());
            _target = new RetainedCanvasAnimationTarget().Bind("card", card);
            _player = null;
            _lastFrame = int.MaxValue;
        }

        protected override void OnRender(float time)
        {
            // 位相 +0.4s: 起動直後 (snap 含む) からカードが画面内に見える
            int frame = (int)((time + 0.4f) % 1.5f * 60);   // 1.0s 再生 + 0.5s 余韻でループ
            if (frame < _lastFrame || _player is null)
            {
                _player = new AnimationPlayer();
                _clock.Frame = 0;
                _player.Update(_clock);
                Animate.Clip(_clip, _target).Play(_player, _clock);
            }
            _lastFrame = frame;
            _clock.Frame = frame;
            _player.Update(_clock);

            OutBuffer.Span<byte>((int)(W * H * 4)).Clear();
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            _rasterScene.Render(Camera2D.Pixels, new GpuRasterTarget2D(cmd, OutBuffer, W, H));
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }

    /// <summary>idle ⇄ jump の StateMachine。Trigger はキュー経由 (入力イベント → 次フレーム反映)。</summary>
    private sealed class StateMachineScene : GpuSceneBase
    {
        private RetainedCanvas _canvas = null!;
        private IRasterScene2D _rasterScene = null!;
        private StateMachine _sm = null!;
        private readonly FixedFrameClock _clock = new() { FrameRate = 60f };
        private readonly Queue<string> _pending = new();
        private bool _started;

        protected override bool NeedsColorTarget => false;
        protected override bool RenderEveryFrame => true;

        public void Trigger(string ev) => _pending.Enqueue(ev);

        protected override void OnInit()
        {
            var idleClip = new AnimationClip("idle", new TrackBase[]
            {
                Tracks.Float("card/translationY", InterpolationKind.Linear,
                    new Keyframe<float>[] { new(0.0f, 40f), new(1.0f, 40f) }),
                Tracks.Color("card/color", InterpolationKind.Linear, new Keyframe<uint>[]
                {
                    new(0.0f, Color2D.Rgba(60, 130, 240, 255)),
                    new(1.0f, Color2D.Rgba(60, 130, 240, 255)),
                }),
            });
            var jumpClip = new AnimationClip("jump", new TrackBase[]
            {
                Tracks.Float("card/translationY", InterpolationKind.Linear, new Keyframe<float>[]
                {
                    new(0.0f, 40f), new(0.15f, 0f), new(0.30f, 40f), new(0.60f, 40f),
                }),
                Tracks.Color("card/color", InterpolationKind.Linear, new Keyframe<uint>[]
                {
                    new(0.0f, Color2D.Rgba(255, 200, 60, 255)),
                    new(0.6f, Color2D.Rgba(255, 200, 60, 255)),
                }),
            });

            var raster = Track(new GpuDeviceRasterizer2D(Device));
            _canvas = Track(new RetainedCanvas());
            _rasterScene = Track(raster.CreateScene(_canvas));
            var card = _canvas.AddChild(_canvas.Root);
            card.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 80, 50, 10);
            card.Transform = Affine2D.Translate(88, 40);
            card.Color = Color2D.Rgba(60, 130, 240, 255);
            card.Opacity = 1f;

            var target = new RetainedCanvasAnimationTarget().Bind("card", card);
            var idle = new State("idle", new ClipNode(idleClip));
            var jump = new State("jump", new ClipNode(jumpClip));
            idle.AddTransition("press", jump, crossfadeSec: 0.15f);
            jump.AddTransition("done", idle, crossfadeSec: 0.15f);
            _sm = new StateMachine(target).AddState(idle).AddState(jump).SetInitial(idle);
            _started = false;
            _pending.Clear();
        }

        protected override void OnRender(float time)
        {
            _clock.Frame = (int)(time * 60);
            if (!_started)
            {
                _sm.Start(_clock);
                _started = true;
            }
            while (_pending.TryDequeue(out string? ev)) _sm.Trigger(ev, _clock);
            _sm.Tick(_clock);

            OutBuffer.Span<byte>((int)(W * H * 4)).Clear();
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            _rasterScene.Render(Camera2D.Pixels, new GpuRasterTarget2D(cmd, OutBuffer, W, H));
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }

    // ---- 3D (ECS + AnimationGraph) シーン ----

    /// <summary>キューブ entity 1 個 + clip/graph を毎フレーム評価して描く共通下回り。</summary>
    private abstract class AnimatedCubeScene : GpuSceneBase
    {
        protected Luxel.Ecs.World World = null!;
        protected EcsAnimationTarget EcsTarget = null!;
        private GpuBuffer _vb = null!;
        private Render3DExtractSystem _extractor = null!;
        private GpuTexture _depth = null!;
        private GpuPipeline _pipeline = null!;

        protected override bool RenderEveryFrame => true;

        protected override void OnInit()
        {
            World = new Luxel.Ecs.World();
            var cube = World.Create();
            World.Set(cube, new LocalTransform(Matrix4x4.CreateScale(0.6f)));
            World.Set(cube, new Color3D(new Vector4(0.4f, 0.85f, 0.55f, 1f)));
            World.Set(cube, new MeshRef(MeshRef.Cube));
            TransformPropagateSystem.Run(World);
            EcsTarget = new EcsAnimationTarget(World).Bind("cube", cube);

            ReadOnlySpan<CubeMesh.Vertex> verts = CubeMesh.Vertices;
            _vb = Track(Device.Malloc((ulong)(verts.Length * CubeMesh.VertexStride), GpuMemoryKind.HostMapped));
            verts.CopyTo(_vb.Span<CubeMesh.Vertex>(verts.Length));
            _extractor = Track(new Render3DExtractSystem(World, Device));
            _depth = Track(Device.CreateDepthTarget(W, H));
            var raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
            raster.DepthTest = true;
            raster.DepthWrite = true;
            _pipeline = Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("cube_forward"), raster));
            OnSceneInit();
        }

        protected abstract void OnSceneInit();

        /// <summary>アニメを評価して LocalTransform を更新する (派生が graph/clip を Tick)。</summary>
        protected abstract void Evaluate(float time);

        protected override void OnRender(float time)
        {
            Evaluate(time);
            TransformPropagateSystem.Run(World);
            _extractor.Extract();

            Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0f, 1.5f, -3.5f), Vector3.Zero, Vector3.UnitY);
            Matrix4x4 proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 3, 1f, 0.1f, 100f);
            Matrix4x4 viewProj = view * proj;

            using var rg = new Luxel.RenderGraph.RenderGraph(Device);
            BufferHandle hVerts = rg.ImportBuffer(_vb, "verts");
            BufferHandle hInsts = rg.ImportBuffer(_extractor.InstanceBuffer, "instances");
            BufferHandle hRead = rg.ImportBuffer(OutBuffer, "readback");
            rg.AddPass("Render3D", PassQueue.Graphics)
              .Read(hVerts).Read(hInsts).Write(hRead, ResourceUsage.CopyDest)
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

    /// <summary>translation (上下) + rotation (Y 一回転) の AnimationClip をループ評価。</summary>
    private sealed class EcsClipScene : AnimatedCubeScene
    {
        private AnimationGraph _graph = null!;
        private float _duration;

        protected override void OnSceneInit()
        {
            var translation = Tracks.Vector3("cube/translation", InterpolationKind.Linear,
                new Keyframe<Vector3>[]
                {
                    new(0.00f, new Vector3(0, -0.5f, 0)),
                    new(0.75f, new Vector3(0, +0.8f, 0)),
                    new(1.50f, new Vector3(0, -0.5f, 0)),
                });
            var rotation = Tracks.Quaternion("cube/rotation", InterpolationKind.Linear,
                new Keyframe<Quaternion>[]
                {
                    new(0.00f, Quaternion.Identity),
                    new(0.50f, Quaternion.CreateFromYawPitchRoll(MathF.PI * 0.66f, 0.2f, 0)),
                    new(1.00f, Quaternion.CreateFromYawPitchRoll(MathF.PI * 1.33f, 0.4f, 0)),
                    new(1.50f, Quaternion.CreateFromYawPitchRoll(MathF.PI * 2.00f, 0.6f, 0)),
                });
            var clip = new AnimationClip("CubeMotion", new TrackBase[] { translation, rotation });
            _duration = clip.Duration;
            _graph = new AnimationGraph(new ClipNode(clip), EcsTarget) { Loop = true };
        }

        protected override void Evaluate(float time)
        {
            _graph.Reset(0f);
            _graph.Tick(time % _duration);
        }
    }

    /// <summary>BlendNode(上下, 左右) — weight knob を毎フレーム反映。</summary>
    private sealed class GraphScene(Signal<float> weight) : AnimatedCubeScene
    {
        private AnimationGraph _graph = null!;
        private BlendNode _blend = null!;

        protected override void OnSceneInit()
        {
            var vertical = new AnimationClip("Vertical", new TrackBase[]
            {
                Tracks.Vector3("cube/translation", InterpolationKind.Linear, new Keyframe<Vector3>[]
                {
                    new(0.00f, new Vector3(0f, -0.8f, 0f)),
                    new(0.50f, new Vector3(0f, +0.8f, 0f)),
                    new(1.00f, new Vector3(0f, -0.8f, 0f)),
                }),
            });
            var horizontal = new AnimationClip("Horizontal", new TrackBase[]
            {
                Tracks.Vector3("cube/translation", InterpolationKind.Linear, new Keyframe<Vector3>[]
                {
                    new(0.00f, new Vector3(-0.8f, 0f, 0f)),
                    new(0.50f, new Vector3(+0.8f, 0f, 0f)),
                    new(1.00f, new Vector3(-0.8f, 0f, 0f)),
                }),
            });
            _blend = new BlendNode(new ClipNode(vertical), new ClipNode(horizontal), weight: 0.5f);
            _graph = new AnimationGraph(_blend, EcsTarget) { Loop = true };
        }

        protected override void Evaluate(float time)
        {
            _blend.Weight = Math.Clamp(weight.Peek(), 0f, 1f);
            _graph.Reset(0f);
            _graph.Tick(time % 1.0f);
        }
    }
}
