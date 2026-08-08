using System.Numerics;
using Luxel.Animation;
using Luxel.Animation.ThreeD;
using Luxel.Animation.TwoD;
using Luxel.Animation.UI;
using Luxel.Ecs;
using Luxel.Controls;
using Luxel.Graphics;
using Luxel.Graphics.TwoD;
using Luxel.Mathematics;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// アニメーションシステムのデモ — Curve × Tween の 2 段分解、コード DSL (Sequence/Parallel)、
/// AnimationClip (CSS @keyframes 由来含む)、StateMachine、AnimationGraph (BlendNode)。
/// すべて絶対時刻モデル (FixedFrameClock) — 時間はストーリーの累積秒から決める (snap 決定的)。
/// </summary>
public static class AnimationStories
{
    /// <summary>CoreUI-local adapter for stateful scenes rendered through a browser-safe GpuView.</summary>
    private abstract class AnimationSceneBase : IDisposable
    {
        protected GpuDevice Device { get; private set; } = null!;
        protected GpuTexture Target => Surface.ColorTarget;
        protected GpuBuffer OutBuffer => Surface.Framebuffer;
        protected uint W => Surface.Width;
        protected uint H => Surface.Height;
        protected uint StridePixels => Surface.StridePixels;
        protected GpuViewSurface Surface { get; private set; } = null!;

        private readonly List<IDisposable> _resources = [];
        private GpuViewSurface? _generation;
        private bool _rendered;

        internal static Widget View(float width, float height, AnimationSceneBase scene, bool animated = true)
            => GpuView(width, height,
                (device, surface, time) => scene.Render(device, surface, time),
                animated: animated, dispose: scene.Dispose);

        protected T Track<T>(T resource) where T : IDisposable
        {
            _resources.Add(resource);
            return resource;
        }

        private GpuViewRenderResult Render(GpuDevice device, GpuViewSurface surface, float time)
        {
            if (!ReferenceEquals(_generation, surface))
            {
                DisposeResources();
                Device = device;
                Surface = surface;
                _generation = surface;
                _rendered = false;
                OnInit();
            }
            if (RenderEveryFrame || !_rendered)
            {
                _rendered = true;
                OnRender(time);
            }
            return GpuViewRenderResult.Ready;
        }

        protected abstract void OnInit();
        protected abstract void OnRender(float time);
        protected virtual bool RenderEveryFrame => false;

        public void Dispose()
        {
            DisposeResources();
            _generation = null;
        }

        private void DisposeResources()
        {
            for (int i = _resources.Count - 1; i >= 0; i--) _resources[i].Dispose();
            _resources.Clear();
        }
    }

    /// <summary>すべての組み込みcurveと主要presetを同じ入力時刻で比較する。</summary>
    [Story("Examples/Animation/Curves", Height = 720, Order = 140)]
    public static Widget Curves()
    {
        (string Name, ICurve Curve, uint Color)[] curves =
        [
            ("Linear", LinearCurve.Instance, Color2D.Rgba(70, 150, 245, 255)),
            ("Bezier Ease", CubicBezierCurve.Ease, Color2D.Rgba(72, 190, 150, 255)),
            ("Bezier EaseIn", CubicBezierCurve.EaseIn, Color2D.Rgba(245, 170, 55, 255)),
            ("Bezier EaseOut", CubicBezierCurve.EaseOut, Color2D.Rgba(235, 105, 95, 255)),
            ("Bezier EaseInOut", CubicBezierCurve.EaseInOut, Color2D.Rgba(165, 110, 235, 255)),
            ("OutCubic", OutCubicCurve.Instance, Color2D.Rgba(50, 190, 220, 255)),
            ("InOutCubic", InOutCubicCurve.Instance, Color2D.Rgba(220, 90, 170, 255)),
            ("Steps JumpStart", new StepsCurve(5, StepPosition.JumpStart), Color2D.Rgba(135, 190, 70, 255)),
            ("Steps JumpEnd", new StepsCurve(5, StepPosition.JumpEnd), Color2D.Rgba(100, 175, 85, 255)),
            ("Steps JumpBoth", new StepsCurve(5, StepPosition.JumpBoth), Color2D.Rgba(80, 160, 110, 255)),
            ("Steps JumpNone", new StepsCurve(5, StepPosition.JumpNone), Color2D.Rgba(65, 145, 135, 255)),
            ("Spring underdamped", new SpringCurve(damping: 10f), Color2D.Rgba(245, 115, 65, 255)),
            ("Spring critical", new SpringCurve(damping: 2f * MathF.Sqrt(170f)), Color2D.Rgba(235, 145, 65, 255)),
            ("Spring overdamped", new SpringCurve(damping: 50f), Color2D.Rgba(210, 175, 70, 255)),
        ];

        var cells = new List<Widget>(curves.Length * 2);
        for (int row = 0; row < curves.Length; row++)
        {
            (string name, ICurve curve, uint color) = curves[row];
            Widget label = Text(name, 12).GridCell(0, row);
            label.HAlign.SetBase(Align.End);
            label.VAlign.SetBase(Align.Center);

            Widget sample = Canvas2D(300, 28, animate: (scene, time) =>
            {
                float phase = time % 2.4f / 1.2f;
                float input = phase <= 1f ? phase : 2f - phase;
                float output = curve.Eval(input);
                // -0.2..1.3をtrack全幅へ写し、springのovershootを端でclampせず見せる。
                float x = 20f + (output + 0.2f) / 1.5f * 250f;
                scene.FillRoundedRect(Color2D.Rgba(55, 60, 72, 255), 18, 12, 254, 4, 2);
                scene.FillRoundedRect(color, x - 7, 6, 14, 16, 5);
            }).GridCell(1, row);
            sample.HAlign.SetBase(Align.Start);
            sample.VAlign.SetBase(Align.Center);

            cells.Add(label);
            cells.Add(sample);
        }

        Grid comparison = Grid(
            columns: [GridLength.Px(150), GridLength.Px(300)],
            rows: Enumerable.Repeat(GridLength.Px(35), curves.Length).ToArray())[cells.ToArray()];
        comparison.HAlign.SetBase(Align.Start);

        return Frame(VStack(7)[
            Text("同じ0→1→0入力に対する位置の変化。Springはovershoot、Stepsはjump位置の差を表示します。", 12),
            comparison]);
    }

    /// <summary>コード DSL: Sequence(Parallel(slide+fade), Parallel(slide+fade))。
    /// Signal へ SignalAnimationTarget 経由で書き、ループ毎に Play し直す。</summary>
    [Story("Examples/Animation/Tween", Height = 300, Order = 141)]
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
    [Story("Examples/Animation/CssKeyframes", Height = 300, Order = 142, SourceMembers = "CssClipScene,AnimationSceneBase,RasterShader,ShaderResource")]
    public static Widget CssKeyframes(StoryContext ctx) => ctx.Snap(Frame(AnimationSceneBase.View(256, 128, new CssClipScene())));

    /// <summary>StateMachine (idle ⇄ jump、crossfade 0.15s)。ボタンで Trigger を送る —
    /// press でジャンプ (黄)、done で idle (青) へ戻る。</summary>
    [Story("Examples/Animation/StateMachine", Height = 340, Order = 143, SourceMembers = "StateMachineScene,AnimationSceneBase,RasterShader,ShaderResource")]
    public static Widget StateMachineDemo(StoryContext ctx)
    {
        var scene = new StateMachineScene();
        return Frame(VStack(8)[
            HStack(8)[
                Button(_ => { scene.Trigger("press"); ctx.Log("trigger: press → jump"); }, "press"),
                Button(_ => { scene.Trigger("done"); ctx.Log("trigger: done → idle"); }, "done")],
            AnimationSceneBase.View(256, 128, scene)]);
    }

    /// <summary>AnimationClipをEcsAnimationTarget経由でLocalTransformへ書き、
    /// ECSの結果を読み取って2D markerとして表示する最小例。</summary>
    [Story("Examples/Animation/EcsClip", Height = 300, Order = 144)]
    public static Widget EcsClip()
    {
        var world = new Luxel.Ecs.World();
        var entity = world.Create();
        world.Set(entity, new LocalTransform(Matrix4x4.Identity));
        var target = new EcsAnimationTarget(world).Bind("marker", entity);
        var clip = new AnimationClip("Move", new TrackBase[]
        {
            Tracks.Vector3("marker/translation", InterpolationKind.Linear,
            [
                new Keyframe<Vector3>(0f, new Vector3(20, 48, 0)),
                new Keyframe<Vector3>(1f, new Vector3(196, 48, 0)),
                new Keyframe<Vector3>(2f, new Vector3(20, 48, 0)),
            ]),
        });
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        Animate.Clip(clip, target).WithLoop().Play(player, clock);

        return Frame(Canvas2D(256, 128, animate: (scene, time) =>
        {
            clock.SetTime(time);
            player.Update(clock);
            Matrix4x4.Decompose(world.Get<LocalTransform>(entity).Matrix,
                out _, out _, out Vector3 position);
            scene.FillRoundedRect(Color2D.Rgba(55, 60, 72, 255), 20, 54, 196, 4, 2);
            scene.FillRoundedRect(Color2D.Rgba(70, 180, 120, 255), position.X, position.Y, 40, 24, 6);
        }));
    }

    /// <summary>AnimationGraphのBlendNodeで上下clipと左右clipを混ぜる最小2D例。</summary>
    [Story("Examples/Animation/Graph", Height = 300, Order = 145, SourceMembers = "PositionTarget")]
    public static Widget Graph(StoryContext ctx)
    {
        Signal<float> weight = ctx.Signal("weight", 0.5f, "Blend: 0 = 上下振動, 1 = 左右振動");
        var target = new PositionTarget();
        var vertical = new AnimationClip("Vertical", new TrackBase[]
        {
            Tracks.Vector2("dot/position", InterpolationKind.Linear,
            [new(0f, new Vector2(108, 16)), new(0.5f, new Vector2(108, 88)), new(1f, new Vector2(108, 16))]),
        });
        var horizontal = new AnimationClip("Horizontal", new TrackBase[]
        {
            Tracks.Vector2("dot/position", InterpolationKind.Linear,
            [new(0f, new Vector2(28, 52)), new(0.5f, new Vector2(188, 52)), new(1f, new Vector2(28, 52))]),
        });
        var blend = new BlendNode(new ClipNode(vertical), new ClipNode(horizontal));
        var graph = new AnimationGraph(blend, target) { Loop = true };

        return Frame(Canvas2D(256, 128, animate: (scene, time) =>
        {
            blend.Weight = Math.Clamp(weight.Peek(), 0f, 1f);
            graph.Tick(time);
            scene.FillRoundedRect(Color2D.Rgba(55, 60, 72, 255), 24, 62, 184, 4, 2);
            scene.FillRoundedRect(Color2D.Rgba(165, 110, 235, 255),
                target.Position.X, target.Position.Y, 24, 24, 12);
        }));
    }

    private sealed class PositionTarget : IAnimationTarget
    {
        public Vector2 Position { get; private set; }
        public void Apply(string path, object value)
        {
            if (path == "dot/position") Position = (Vector2)value;
        }
    }

    // ---- 2D (RetainedCanvas をオフスクリーンで所有する) シーン ----

    /// <summary>CSS @keyframes を CssKeyframesImporter でパースし、card ノードで再生。</summary>
    private sealed class CssClipScene : AnimationSceneBase
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

        protected override bool RenderEveryFrame => true;

        protected override void OnInit()
        {
            var raster = Track(new GpuDeviceRasterizer2D(Device, RasterShader));
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

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            _rasterScene.Render(Camera2D.Pixels, new GpuRasterTarget2D(cmd, OutBuffer, StridePixels, H));
            cmd.Finish();
            Device.MainQueue.Submit(cmd);
        }
    }

    /// <summary>idle ⇄ jump の StateMachine。Trigger はキュー経由 (入力イベント → 次フレーム反映)。</summary>
    private sealed class StateMachineScene : AnimationSceneBase
    {
        private RetainedCanvas _canvas = null!;
        private IRasterScene2D _rasterScene = null!;
        private StateMachine _sm = null!;
        private readonly FixedFrameClock _clock = new() { FrameRate = 60f };
        private readonly Queue<string> _pending = new();
        private bool _started;

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

            var raster = Track(new GpuDeviceRasterizer2D(Device, RasterShader));
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

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            _rasterScene.Render(Camera2D.Pixels, new GpuRasterTarget2D(cmd, OutBuffer, StridePixels, H));
            cmd.Finish();
            Device.MainQueue.Submit(cmd);
        }
    }

    private static GpuShaderCode RasterShader(string name) => new()
    {
        SpirV = ShaderResource(name + ".spv"),
        Dxil = ShaderResource(name + ".dxil"),
        Wgsl = ShaderResource(name + ".wgsl"),
    };

    private static byte[] ShaderResource(string fileName)
    {
        System.Reflection.Assembly assembly = typeof(AnimationStories).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith("Shaders." + fileName, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded cube shader is missing: {fileName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

}
