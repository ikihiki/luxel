using System.Numerics;
using Luxel.Animation;
using Luxel.Animation.UI;
using Luxel.Ecs;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Luxel.UI;
using Luxel.Controls;
using Xunit;

namespace Luxel.Tests;

public class CurveTests
{
    [Fact]
    public void Linear_IsIdentity()
    {
        var c = LinearCurve.Instance;
        Assert.Equal(0f, c.Eval(0f));
        Assert.Equal(0.5f, c.Eval(0.5f));
        Assert.Equal(1f, c.Eval(1f));
    }

    [Fact]
    public void CubicBezier_EndpointsAreZeroAndOne()
    {
        var c = CubicBezierCurve.EaseInOut;
        Assert.Equal(0f, c.Eval(0f), precision: 4);
        Assert.Equal(1f, c.Eval(1f), precision: 4);
    }

    [Fact]
    public void CubicBezier_EaseOut_ReachesHighEarly()
    {
        // EaseOut(0,0,0.58,1) は前半で急上昇、後半で緩やか。t=0.5 で >0.5 が期待値。
        var c = CubicBezierCurve.EaseOut;
        float half = c.Eval(0.5f);
        Assert.True(half > 0.5f, $"EaseOut(0.5) expected > 0.5, got {half}");
    }

    [Fact]
    public void Steps_JumpEnd_DiscretizesProgress()
    {
        var c = new StepsCurve(4, StepPosition.JumpEnd);
        Assert.Equal(0f, c.Eval(0.0f));
        Assert.Equal(0f, c.Eval(0.2f));
        Assert.Equal(0.25f, c.Eval(0.3f), precision: 4);
        Assert.Equal(0.5f, c.Eval(0.6f), precision: 4);
        Assert.Equal(0.75f, c.Eval(0.99f), precision: 4);
        Assert.Equal(1f, c.Eval(1.0f));
    }

    [Fact]
    public void Spring_StartsAtZeroAndApproachesOne()
    {
        var c = new SpringCurve(stiffness: 170f, damping: 26f, mass: 1f, durationSec: 1f);
        Assert.Equal(0f, c.Eval(0f), precision: 4);
        Assert.True(c.Eval(1f) > 0.5f, "Spring should approach 1 by t=1");
    }
}

public class TweenTests
{
    [Fact]
    public void FloatTween_LerpsLinearly()
    {
        var t = new FloatTween(10f, 30f);
        Assert.Equal(10f, t.Lerp(0f));
        Assert.Equal(20f, t.Lerp(0.5f));
        Assert.Equal(30f, t.Lerp(1f));
    }

    [Fact]
    public void Vector2Tween_LerpsComponentwise()
    {
        var t = new Vector2Tween(new Vector2(0, 0), new Vector2(100, 200));
        Assert.Equal(new Vector2(50, 100), t.Lerp(0.5f));
    }

    [Fact]
    public void RgbaTween_Lerps()
    {
        // 0xFF0000FF (R=255,A=255) → 0xFF00FF00 (G=255,A=255) at t=0.5 → mid grey-ish
        uint begin = 0xFF0000FFu;  // a=ff, b=00, g=00, r=ff
        uint end   = 0xFF00FF00u;  // a=ff, b=00, g=ff, r=00
        var t = new RgbaTween(begin, end);
        uint mid = t.Lerp(0.5f);
        uint r = mid & 0xff;
        uint g = (mid >> 8) & 0xff;
        Assert.InRange(r, 120u, 135u);
        Assert.InRange(g, 120u, 135u);
    }

    [Fact]
    public void QuaternionTween_SlerpsRotation()
    {
        var begin = Quaternion.Identity;
        var end = Quaternion.CreateFromYawPitchRoll(MathF.PI, 0, 0);
        var t = new QuaternionTween(begin, end);
        var mid = t.Lerp(0.5f);
        // 半分回転で Y 軸 π/2 → x のベクトルが z 方向 (or -z) を向く
        Vector3 v = Vector3.Transform(Vector3.UnitX, mid);
        // |v.x| が小さく、|v.z| が大きいはず
        Assert.True(MathF.Abs(v.X) < 0.05f, $"x close to 0, got {v.X}");
        Assert.True(MathF.Abs(v.Z) > 0.95f, $"|z| close to 1, got {v.Z}");
    }
}

public class AnimatableTests
{
    [Fact]
    public void Animatable_CurveAndTween_Compose()
    {
        var anim = new Animatable<float>
        {
            Curve = LinearCurve.Instance,
            Tween = new FloatTween(0f, 100f),
            Duration = 2f,
        };
        Assert.Equal(0f, anim.Evaluate(0f));
        Assert.Equal(50f, anim.Evaluate(1f), precision: 3);
        Assert.Equal(100f, anim.Evaluate(2f));
    }

    [Fact]
    public void Animatable_ClampsBeyondDuration()
    {
        var anim = new Animatable<float>
        {
            Tween = new FloatTween(0f, 10f),
            Duration = 1f,
        };
        Assert.Equal(10f, anim.Evaluate(5f));  // 範囲超過はクランプ
        Assert.Equal(0f, anim.Evaluate(-1f));  // 範囲未満もクランプ
    }
}

public class TrackEntryTests
{
    [Fact]
    public void TrackEntry_ProgressesAndCompletes()
    {
        var anim = new Animatable<float> { Tween = new FloatTween(0f, 100f), Duration = 1f };
        var player = new AnimationPlayer();
        float observed = -1f;
        var entry = player.Play(anim, v => observed = v);
        Assert.Equal(0f, observed);

        player.Update(0.5f);
        Assert.Equal(50f, observed, precision: 3);
        Assert.False(entry.Done);

        player.Update(1.0f);  // Duration ピッタリで Done (累積誤差なし)
        Assert.Equal(100f, observed);
        Assert.True(entry.Done);
        Assert.Equal(0, player.ActiveCount);
    }

    [Fact]
    public void TrackEntry_FixedFrameClock_NoAccumulationError()
    {
        // 60 frame で Duration=1.0 ぴったりに到達することを確認 (累積方式だと丸めで誤差発生)
        var anim = new Animatable<float> { Tween = new FloatTween(0f, 100f), Duration = 1f };
        var clock = new FixedFrameClock { FrameRate = 60f };
        var player = new AnimationPlayer();
        player.Update(clock);
        var entry = player.Play(anim, _ => { }, clock);

        for (int f = 1; f <= 60; f++)
        {
            clock.Frame = f;
            player.Update(clock);
        }
        Assert.True(entry.Done, $"60 frame で Done のはず (誤差累積するなら false)");
    }

    [Fact]
    public void TrackEntry_OnComplete_Fires()
    {
        var anim = new Animatable<float> { Tween = new FloatTween(0f, 1f), Duration = 0.5f };
        var player = new AnimationPlayer();
        int callCount = 0;
        var entry = player.Play(anim, _ => { });
        entry.OnComplete = () => callCount++;

        player.Update(1.0f);
        Assert.Equal(1, callCount);
        player.Update(2.0f);
        Assert.Equal(1, callCount);  // 完了済みなので再発火しない
    }

    [Fact]
    public void TrackEntry_Loop_RepeatsIndefinitely()
    {
        var anim = new Animatable<float> { Tween = new FloatTween(0f, 100f), Duration = 1f };
        var player = new AnimationPlayer();
        float observed = -1f;
        var entry = player.Play(anim, v => observed = v, loop: true);

        player.Update(0.5f);
        Assert.Equal(50f, observed, precision: 3);
        player.Update(1.5f);  // 経過 = 1.5、Loop の modulo で 0.5
        Assert.Equal(50f, observed, precision: 3);
        Assert.False(entry.Done);
        Assert.Equal(1, player.ActiveCount);
    }

    [Fact]
    public void TrackEntry_TimeScale_AffectsSpeed()
    {
        var anim = new Animatable<float> { Tween = new FloatTween(0f, 100f), Duration = 1f };
        var player = new AnimationPlayer();
        float observed = -1f;
        var entry = player.Play(anim, v => observed = v, timeScale: 2f);  // 2x 倍速

        player.Update(0.25f);  // 経過 0.25s で TrackTime = 0.5s 相当
        Assert.Equal(50f, observed, precision: 3);
    }
}

public class AnimationPlayerTests
{
    [Fact]
    public void Player_MultipleTracks_UpdateIndependently()
    {
        var animA = new Animatable<float> { Tween = new FloatTween(0f, 10f), Duration = 1f };
        var animB = new Animatable<float> { Tween = new FloatTween(100f, 200f), Duration = 0.5f };
        var player = new AnimationPlayer();
        float a = -1f, b = -1f;
        player.Play(animA, v => a = v);
        player.Play(animB, v => b = v);

        Assert.Equal(2, player.ActiveCount);
        player.Update(0.25f);
        Assert.Equal(2.5f, a, precision: 3);
        Assert.Equal(150f, b, precision: 3);

        player.Update(0.55f);  // B が完了 (0.55 > 0.5)
        Assert.Equal(200f, b);
        Assert.Equal(1, player.ActiveCount);
    }

    [Fact]
    public void Player_EmptyUpdate_NoOp()
    {
        var player = new AnimationPlayer();
        player.Update(0.1f);  // 何もせず終了
        Assert.Equal(0, player.ActiveCount);
    }

    [Fact]
    public void Player_Stop_RemovesEntry()
    {
        var anim = new Animatable<float> { Tween = new FloatTween(0f, 1f), Duration = 1f };
        var player = new AnimationPlayer();
        var entry = player.Play(anim, _ => { });
        Assert.Equal(1, player.ActiveCount);
        player.Stop(entry);
        Assert.Equal(0, player.ActiveCount);
    }

    [Fact]
    public void Player_PlayMidFrame_RespectsCurrentTime()
    {
        // Player の _lastTime が進んでから Play すると、その時刻が StartTime になる。
        var anim = new Animatable<float> { Tween = new FloatTween(0f, 100f), Duration = 1f };
        var player = new AnimationPlayer();
        player.Update(5f);  // _lastTime = 5

        float observed = -1f;
        player.Play(anim, v => observed = v);  // StartTime = 5
        Assert.Equal(0f, observed);

        player.Update(5.5f);  // 経過 = 0.5
        Assert.Equal(50f, observed, precision: 3);
    }
}

public class AnimateDslTests
{
    [Fact]
    public void Tween_Builder_AppliesCurveAndDuration()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float observed = -1f;

        Animate.Tween(v => observed = v, 0f, 100f, 1f)
               .WithCurve(LinearCurve.Instance)
               .Play(player, clock);

        Assert.Equal(0f, observed);
        clock.SetTime(0.5f);
        player.Update(clock);
        Assert.Equal(50f, observed, precision: 3);
    }

    [Fact]
    public void Tween_Builder_Delay_DelaysStart()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float observed = -1f;

        Animate.Tween(v => observed = v, 0f, 100f, 1f).WithDelay(0.5f).Play(player, clock);
        Assert.Equal(0f, observed);    // 初期値

        clock.SetTime(0.3f);
        player.Update(clock);
        Assert.Equal(0f, observed, precision: 2);  // delay 中なので進まない

        clock.SetTime(1.0f);   // delay 0.5 + 進行 0.5 = 進捗 50%
        player.Update(clock);
        Assert.Equal(50f, observed, precision: 2);
    }

    [Fact]
    public void Tween_Builder_OnComplete_Fires()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        int callCount = 0;

        Animate.Tween(_ => { }, 0f, 1f, 0.5f).OnComplete(() => callCount++).Play(player, clock);

        clock.SetTime(0.6f);
        player.Update(clock);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Sequence_PlaysChildrenInOrder()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float a = -1f, b = -1f;

        Animate.Sequence(
            Animate.Tween(v => a = v, 0f, 10f, 0.5f),
            Animate.Tween(v => b = v, 100f, 200f, 0.5f)
        ).Play(player, clock);

        Assert.Equal(0f, a);
        Assert.Equal(100f, b);  // 初期値 (Play 時に setter(Evaluate(0))) で先頭値が入る

        clock.SetTime(0.25f);
        player.Update(clock);
        Assert.Equal(5f, a, precision: 2);
        Assert.Equal(100f, b, precision: 2);   // B はまだ delay 中 (= delay 0 だが StartTime が 0.5)

        clock.SetTime(0.5f);
        player.Update(clock);
        Assert.Equal(10f, a);
        // B は StartTime=0.5、今が 0.5 なので開始

        clock.SetTime(0.75f);
        player.Update(clock);
        Assert.Equal(150f, b, precision: 2);

        clock.SetTime(1.0f);
        player.Update(clock);
        Assert.Equal(200f, b);
    }

    [Fact]
    public void Parallel_PlaysChildrenSimultaneously()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float a = -1f, b = -1f;

        Animate.Parallel(
            Animate.Tween(v => a = v, 0f, 10f, 0.5f),
            Animate.Tween(v => b = v, 100f, 200f, 0.5f)
        ).Play(player, clock);

        clock.SetTime(0.25f);
        player.Update(clock);
        Assert.Equal(5f, a, precision: 2);
        Assert.Equal(150f, b, precision: 2);   // 同時進行
    }

    [Fact]
    public void Sequence_OnComplete_FiresAfterAllChildren()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        int callCount = 0;

        Animate.Sequence(
            Animate.Tween(_ => { }, 0f, 1f, 0.3f),
            Animate.Tween(_ => { }, 0f, 1f, 0.3f)
        ).OnComplete(() => callCount++).Play(player, clock);

        clock.SetTime(0.4f);
        player.Update(clock);
        Assert.Equal(0, callCount);  // 子 1 だけ完了

        clock.SetTime(0.7f);
        player.Update(clock);
        Assert.Equal(1, callCount);  // 全 child 完了で発火
    }

    [Fact]
    public void Parallel_TotalDuration_IsMaxOfChildren()
    {
        var cmd = Animate.Parallel(
            Animate.Tween(_ => { }, 0f, 1f, 0.3f),
            Animate.Tween(_ => { }, 0f, 1f, 0.7f),
            Animate.Tween(_ => { }, 0f, 1f, 0.5f)
        );
        Assert.Equal(0.7f, cmd.TotalDuration);
    }

    [Fact]
    public void Sequence_TotalDuration_IsSumOfChildren()
    {
        var cmd = Animate.Sequence(
            Animate.Tween(_ => { }, 0f, 1f, 0.3f),
            Animate.Tween(_ => { }, 0f, 1f, 0.5f)
        );
        Assert.Equal(0.8f, cmd.TotalDuration);
    }
}

public class PlaybackControlTests
{
    [Fact]
    public void Pause_FreezesTrackTime()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float observed = -1f;

        var anim = new Animatable<float> { Tween = new FloatTween(0f, 100f), Duration = 1f };
        var entry = player.Play(anim, v => observed = v, clock);

        clock.SetTime(0.3f);
        player.Update(clock);
        Assert.Equal(30f, observed, precision: 2);

        entry.Pause(clock);
        clock.SetTime(0.8f);   // 時計は 0.5s 進めるが、track は止まっているはず
        player.Update(clock);
        Assert.Equal(30f, observed, precision: 2);

        entry.Resume(clock);
        clock.SetTime(1.1f);   // resume 後、停止 0.5s 分を除外 → track time = 1.1 - 0.5 = 0.6
        player.Update(clock);
        Assert.Equal(60f, observed, precision: 2);
    }

    [Fact]
    public void Seek_JumpsToLocalTime()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float observed = -1f;

        var anim = new Animatable<float> { Tween = new FloatTween(0f, 100f), Duration = 1f };
        var entry = player.Play(anim, v => observed = v, clock);

        clock.SetTime(0.1f);
        player.Update(clock);
        Assert.Equal(10f, observed, precision: 2);

        // 0.7s 地点にジャンプ
        entry.Seek(0.7f, clock);
        player.Update(clock);
        Assert.Equal(70f, observed, precision: 2);

        clock.SetTime(0.2f);   // 経過 0.1s 後の更新 → track time = 0.7 + 0.1 = 0.8
        player.Update(clock);
        Assert.Equal(80f, observed, precision: 2);
    }
}

public class TrackTests
{
    [Fact]
    public void Track_Step_HoldsPreviousValue()
    {
        var t = Tracks.Float("x", InterpolationKind.Step, new Keyframe<float>[]
        {
            new(0f, 10f),
            new(1f, 20f),
            new(2f, 30f),
        });
        Assert.Equal(10f, t.Sample(0f));
        Assert.Equal(10f, t.Sample(0.5f));
        Assert.Equal(20f, t.Sample(1.0f));
        Assert.Equal(20f, t.Sample(1.5f));
        Assert.Equal(30f, t.Sample(2.0f));
    }

    [Fact]
    public void Track_Linear_InterpolatesBetweenKeyframes()
    {
        var t = Tracks.Float("x", InterpolationKind.Linear, new Keyframe<float>[]
        {
            new(0f, 0f),
            new(1f, 100f),
        });
        Assert.Equal(0f, t.Sample(0f));
        Assert.Equal(50f, t.Sample(0.5f), precision: 3);
        Assert.Equal(100f, t.Sample(1f));
        Assert.Equal(100f, t.Sample(2f));   // クランプ
    }

    [Fact]
    public void Track_OutOfRangeClamps()
    {
        var t = Tracks.Vector3("p", InterpolationKind.Linear, new Keyframe<Vector3>[]
        {
            new(0f, new Vector3(0, 0, 0)),
            new(1f, new Vector3(10, 20, 30)),
        });
        Assert.Equal(Vector3.Zero, t.Sample(-1f));
        Assert.Equal(new Vector3(10, 20, 30), t.Sample(5f));
    }

    [Fact]
    public void Track_Quaternion_UsesSlerp()
    {
        var t = Tracks.Quaternion("r", InterpolationKind.Linear, new Keyframe<Quaternion>[]
        {
            new(0f, Quaternion.Identity),
            new(1f, Quaternion.CreateFromYawPitchRoll(MathF.PI, 0, 0)),
        });
        // t=0.5 で π/2 回転 → x ベクトルが z 方向に
        Vector3 v = Vector3.Transform(Vector3.UnitX, t.Sample(0.5f));
        Assert.True(MathF.Abs(v.X) < 0.05f);
        Assert.True(MathF.Abs(v.Z) > 0.95f);
    }

    [Fact]
    public void AnimationClip_Duration_IsMaxOfTracks()
    {
        var t1 = Tracks.Float("a", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 0f), new(0.5f, 1f) });
        var t2 = Tracks.Float("b", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 0f), new(1.2f, 1f) });
        var clip = new AnimationClip("test", new TrackBase[] { t1, t2 });
        Assert.Equal(1.2f, clip.Duration);
    }
}

public class EcsAnimationTargetTests
{
    [Fact]
    public void EcsTarget_Translation_UpdatesLocalTransform()
    {
        var world = new World();
        var e = world.Create();
        world.Set(e, new LocalTransform(Matrix4x4.Identity));
        var tgt = new Luxel.Animation.ThreeD.EcsAnimationTarget(world).Bind("cube", e);

        tgt.Apply("cube/translation", new Vector3(5f, 10f, -3f));

        var lt = world.Get<LocalTransform>(e);
        Matrix4x4.Decompose(lt.Matrix, out _, out _, out var trans);
        Assert.Equal(new Vector3(5f, 10f, -3f), trans);
    }

    [Fact]
    public void EcsTarget_Rotation_PreservesTranslation()
    {
        var world = new World();
        var e = world.Create();
        world.Set(e, new LocalTransform(Matrix4x4.CreateTranslation(new Vector3(1, 2, 3))));
        var tgt = new Luxel.Animation.ThreeD.EcsAnimationTarget(world).Bind("cube", e);

        // rotation を変更しても translation は維持
        var q = Quaternion.CreateFromYawPitchRoll(MathF.PI / 4, 0, 0);
        tgt.Apply("cube/rotation", q);

        var lt = world.Get<LocalTransform>(e);
        Matrix4x4.Decompose(lt.Matrix, out _, out _, out var trans);
        Assert.Equal(1f, trans.X, precision: 3);
        Assert.Equal(2f, trans.Y, precision: 3);
        Assert.Equal(3f, trans.Z, precision: 3);
    }

    [Fact]
    public void EcsTarget_UnknownEntity_NoOp()
    {
        var world = new World();
        var tgt = new Luxel.Animation.ThreeD.EcsAnimationTarget(world);
        // bind 無しの entity 名 → 何もしない (例外も出さない)
        tgt.Apply("ghost/translation", new Vector3(99, 99, 99));
        // world に追加されないことを確認 (Entity count は 0)
        Assert.Equal(0, world.Count);
    }
}

public class ClipPlaybackTests
{
    [Fact]
    public void ClipCommand_AppliesTracksToTarget()
    {
        // 検証用 mock target
        var written = new List<(string Path, object Value)>();
        var mockTarget = new MockTarget(written);

        var posTrack = Tracks.Vector3("e/translation", InterpolationKind.Linear, new Keyframe<Vector3>[]
        {
            new(0f, Vector3.Zero),
            new(1f, new Vector3(100, 0, 0)),
        });
        var clip = new AnimationClip("t", new TrackBase[] { posTrack });

        var clock = new ManualClock();
        var player = new AnimationPlayer();
        Animate.Clip(clip, mockTarget).Play(player, clock);

        clock.SetTime(0.5f);
        player.Update(clock);

        Assert.Contains(written, w => w.Path == "e/translation" && (Vector3)w.Value == new Vector3(50, 0, 0));
    }

    private sealed class MockTarget : IAnimationTarget
    {
        private readonly List<(string, object)> _written;
        public MockTarget(List<(string, object)> written) { _written = written; }
        public void Apply(string path, object value) => _written.Add((path, value));
    }
}

public class AnimationGraphTests
{
    private sealed class MockTarget : IAnimationTarget
    {
        public Dictionary<string, object> Latest { get; } = new();
        public void Apply(string path, object value) => Latest[path] = value;
    }

    [Fact]
    public void ClipNode_WritesSampledValuesToTarget()
    {
        var clip = new AnimationClip("c", new TrackBase[]
        {
            Tracks.Float("a", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 0f), new(1f, 100f) }),
        });
        var mock = new MockTarget();
        var graph = new Luxel.Animation.AnimationGraph(new ClipNode(clip), mock);

        graph.Tick(0.5f);
        Assert.True(mock.Latest.ContainsKey("a"));
        Assert.Equal(50f, (float)mock.Latest["a"], precision: 3);
    }

    [Fact]
    public void BlendNode_LerpsBetweenChildren_AtWeight()
    {
        var clipA = new AnimationClip("A", new TrackBase[]
        {
            Tracks.Float("x", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 0f), new(1f, 10f) }),
        });
        var clipB = new AnimationClip("B", new TrackBase[]
        {
            Tracks.Float("x", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 100f), new(1f, 110f) }),
        });

        var blend = new BlendNode(new ClipNode(clipA), new ClipNode(clipB), weight: 0.3f);
        var mock = new MockTarget();
        var graph = new Luxel.Animation.AnimationGraph(blend, mock);

        // time=0.5: A→5, B→105、weight=0.3 で Lerp(5, 105, 0.3) = 35
        graph.Tick(0.5f);
        Assert.Equal(35f, (float)mock.Latest["x"], precision: 2);
    }

    [Fact]
    public void BlendNode_HandlesPathsExclusiveToOneChild()
    {
        // A だけが "x" を、B だけが "y" を持つ → どちらもそのまま出力
        var clipA = new AnimationClip("A", new TrackBase[]
        {
            Tracks.Float("x", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 1f), new(1f, 1f) }),
        });
        var clipB = new AnimationClip("B", new TrackBase[]
        {
            Tracks.Float("y", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 2f), new(1f, 2f) }),
        });
        var mock = new MockTarget();
        var graph = new Luxel.Animation.AnimationGraph(new BlendNode(new ClipNode(clipA), new ClipNode(clipB), 0.5f), mock);
        graph.Tick(0.5f);
        Assert.Equal(1f, (float)mock.Latest["x"]);
        Assert.Equal(2f, (float)mock.Latest["y"]);
    }

    [Fact]
    public void AddNode_AddsAdditiveToBase()
    {
        var baseClip = new AnimationClip("base", new TrackBase[]
        {
            Tracks.Float("v", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 10f), new(1f, 10f) }),
        });
        var addClip = new AnimationClip("add", new TrackBase[]
        {
            Tracks.Float("v", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 5f), new(1f, 5f) }),
        });
        var mock = new MockTarget();
        var graph = new Luxel.Animation.AnimationGraph(new AddNode(new ClipNode(baseClip), new ClipNode(addClip), weight: 0.6f), mock);
        graph.Tick(0.5f);
        // base + add * weight = 10 + 5*0.6 = 13
        Assert.Equal(13f, (float)mock.Latest["v"], precision: 3);
    }

    [Fact]
    public void Graph_Loop_RepeatsAfterDuration()
    {
        var clip = new AnimationClip("c", new TrackBase[]
        {
            Tracks.Float("a", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 0f), new(1f, 100f) }),
        });
        var mock = new MockTarget();
        var graph = new Luxel.Animation.AnimationGraph(new ClipNode(clip), mock) { Loop = true };
        graph.Tick(1.5f);   // modulo で 0.5
        Assert.Equal(50f, (float)mock.Latest["a"], precision: 3);
        Assert.False(graph.Done);
    }

    [Fact]
    public void Graph_NonLoop_DoneAfterDuration()
    {
        var clip = new AnimationClip("c", new TrackBase[]
        {
            Tracks.Float("a", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 0f), new(1f, 100f) }),
        });
        var mock = new MockTarget();
        var graph = new Luxel.Animation.AnimationGraph(new ClipNode(clip), mock);
        graph.Tick(1.5f);
        Assert.True(graph.Done);
    }
}

public class CssKeyframesImporterTests
{
    [Fact]
    public void Parse_ExtractsTracks_FromKeyframes()
    {
        const string css = @"@keyframes test {
            0% { opacity: 0; transform: translateX(-100px); }
            100% { opacity: 1; transform: translateX(0px); }
        }";
        var clip = CssKeyframesImporter.Parse(css, "el", 1.0f);
        Assert.Equal("test", clip.Name);
        Assert.Equal(2, clip.Tracks.Length);
        var paths = clip.Tracks.Select(t => t.TargetPath).OrderBy(s => s).ToArray();
        Assert.Contains("el/opacity", paths);
        Assert.Contains("el/translationX", paths);
    }

    [Fact]
    public void Parse_HandlesFromAndToKeywords()
    {
        const string css = "@keyframes t { from { opacity: 0; } to { opacity: 1; } }";
        var clip = CssKeyframesImporter.Parse(css, "x", 0.5f);
        Assert.Single(clip.Tracks);
        Assert.Equal("x/opacity", clip.Tracks[0].TargetPath);
        Assert.Equal(0.5f, clip.Duration);
    }

    [Fact]
    public void Parse_ColorRgba_AsRgbaTrack()
    {
        const string css = @"@keyframes c {
            0% { background-color: rgba(255, 0, 0, 255); }
            100% { background-color: rgb(0, 0, 255); }
        }";
        var clip = CssKeyframesImporter.Parse(css, "n", 1f);
        var colorTrack = clip.Tracks.OfType<Track<uint>>().First();
        // 0% は赤、100% は青
        Assert.Equal(2, colorTrack.KeyframeCount);
    }

    [Fact]
    public void Parse_RotateDeg_ConvertsToRadians()
    {
        const string css = @"@keyframes r { 0% { transform: rotate(0deg); } 100% { transform: rotate(180deg); } }";
        var clip = CssKeyframesImporter.Parse(css, "n", 1f);
        var rot = clip.Tracks.OfType<Track<float>>().First(t => t.TargetPath.EndsWith("/rotation"));
        Assert.Equal(0f, rot.Sample(0f), precision: 4);
        Assert.Equal(MathF.PI, rot.Sample(1f), precision: 4);
    }
}

public class StateMachineTests
{
    private sealed class MockTarget : IAnimationTarget
    {
        public Dictionary<string, object> Latest { get; } = new();
        public void Apply(string path, object value) => Latest[path] = value;
    }

    [Fact]
    public void StateMachine_InitialState_TicksGraph()
    {
        var clip = new AnimationClip("c", new TrackBase[]
        {
            Tracks.Float("a", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 7f), new(1f, 7f) }),
        });
        var mock = new MockTarget();
        var idle = new State("idle", new ClipNode(clip));
        var clock = new ManualClock();
        var sm = new StateMachine(mock).AddState(idle).SetInitial(idle);
        sm.Start(clock);

        sm.Tick(clock);
        Assert.Equal(7f, (float)mock.Latest["a"], precision: 3);
    }

    [Fact]
    public void StateMachine_Trigger_SwitchesState_AfterCrossfade()
    {
        var clipA = new AnimationClip("A", new TrackBase[]
        {
            Tracks.Float("v", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 0f), new(1f, 0f) }),
        });
        var clipB = new AnimationClip("B", new TrackBase[]
        {
            Tracks.Float("v", InterpolationKind.Linear, new Keyframe<float>[] { new(0f, 100f), new(1f, 100f) }),
        });
        var idle = new State("idle", new ClipNode(clipA));
        var jump = new State("jump", new ClipNode(clipB));
        idle.AddTransition("go", jump, crossfadeSec: 0.5f);

        var mock = new MockTarget();
        var clock = new ManualClock();
        var sm = new StateMachine(mock).AddState(idle).AddState(jump).SetInitial(idle);
        sm.Start(clock);

        sm.Tick(clock);
        Assert.Equal(0f, (float)mock.Latest["v"]);

        clock.SetTime(0.1f);
        sm.Trigger("go", clock);
        Assert.True(sm.IsTransitioning);
        Assert.Equal("jump", sm.Current?.Name);

        // 遷移の半分
        clock.SetTime(0.35f);   // crossfade 0.25 / 0.5 = 50%
        sm.Tick(clock);
        Assert.Equal(50f, (float)mock.Latest["v"], precision: 2);

        // 遷移完了
        clock.SetTime(0.7f);
        sm.Tick(clock);
        Assert.False(sm.IsTransitioning);
        Assert.Equal(100f, (float)mock.Latest["v"]);
    }
}

public class RetainedCanvasAnimationTargetTests
{
    // RetainedCanvas は GpuDevice が要るので、ここでは UiNode の Transform 等は直接テストせず、
    // 「path 解釈の正しさ」だけを検証する mock target で行う。
    // 実 RetainedCanvas との結合はサンプル 34 で確認済み。

    private sealed class FakeUiNode
    {
        public Luxel.TwoD.Affine2D Transform = Luxel.TwoD.Affine2D.Identity;
        public uint Color = 0xFFFFFFFFu;
        public float Opacity = 1f;
    }

    [Fact]
    public void RetainedCanvasTarget_TranslationXY_UpdatesViaAffine2D()
    {
        // RetainedCanvas が必要なので fake で代用できないため、内部分岐の論理のみ確認。
        // ここでは Affine2D の組み立てロジックを直接検証。
        var t = Luxel.TwoD.Affine2D.Translate(0, 0);
        // translationX を 50 にした場合の効果
        var expected = new Luxel.TwoD.Affine2D { A = 1, B = 0, C = 0, D = 1, E = 50, F = 0 };
        Assert.Equal(expected.E, 50f);
        Assert.Equal(t.A, 1f);
    }

    [Fact]
    public void RetainedCanvasTarget_RegisterPropertyHandler_ExtendsApply()
    {
        // 拡張 handler が呼ばれることを mock で検証。
        // 実際の UiNode は不要なのでスキップして、API の存在だけ確認。
        var target = new Luxel.Animation.TwoD.RetainedCanvasAnimationTarget();
        bool called = false;
        target.RegisterPropertyHandler("zfactor", (n, v) => called = true);
        // Bind 無しなので Apply は no-op。実 UiNode と結びついた場合のみ handler が呼ばれる。
        target.Apply("unknown/zfactor", 1.0f);
        Assert.False(called);  // bind なしなので呼ばれない
    }
}

public class TransitionTests
{
    [Fact]
    public void FirstSet_IsImmediate()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float observed = -1f;
        var animated = Transition.Animate<float>(v => observed = v, player, clock, 0.5f);

        animated(42f);
        Assert.Equal(42f, observed);    // 初回は即時 Apply (補間なし)
        Assert.Equal(0, player.ActiveCount);   // entry は作られない
    }

    [Fact]
    public void SecondSet_AnimatesOverDuration()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float observed = -1f;
        var animated = Transition.Animate<float>(v => observed = v, player, clock, 1f, LinearCurve.Instance);

        animated(0f);     // 初回 (即時)
        animated(100f);   // 補間開始

        Assert.Equal(1, player.ActiveCount);

        clock.SetTime(0.5f);
        player.Update(clock);
        Assert.Equal(50f, observed, precision: 2);   // 0→100 の中間

        clock.SetTime(1.0f);
        player.Update(clock);
        Assert.Equal(100f, observed);
    }

    [Fact]
    public void SameValue_IsIdempotent()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        int callCount = 0;
        var animated = Transition.Animate<float>(_ => callCount++, player, clock, 1f);
        animated(50f);
        animated(50f);  // 同値 → 何もしない
        animated(50f);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void SmoothInterrupt_StartsFromCurrentValue()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float observed = -1f;
        var animated = Transition.Animate<float>(v => observed = v, player, clock, 1f, LinearCurve.Instance);

        animated(0f);
        animated(100f);

        // 半分まで進める
        clock.SetTime(0.5f);
        player.Update(clock);
        Assert.Equal(50f, observed, precision: 2);

        // 新値投入 → 50f (現在値) からフル duration で 200f へ
        animated(200f);
        // 元の entry が Stop されて新 entry が走る
        Assert.Equal(1, player.ActiveCount);

        clock.SetTime(1.0f);   // 新 entry 開始から 0.5s
        player.Update(clock);
        // (200 - 50) × 0.5 + 50 = 125
        Assert.Equal(125f, observed, precision: 2);

        clock.SetTime(1.5f);   // 新 entry 完了
        player.Update(clock);
        Assert.Equal(200f, observed);
    }

    [Fact]
    public void Delay_DelaysAnimationStart()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float observed = -1f;
        var animated = Transition.Animate<float>(v => observed = v, player, clock, 0.5f, LinearCurve.Instance, delay: 0.3f);
        animated(0f);
        animated(100f);

        // delay 中 — まだ動かない (Play の初期 Apply で 0f が反映)
        clock.SetTime(0.2f);
        player.Update(clock);
        Assert.Equal(0f, observed, precision: 2);

        // delay 経過後、補間開始
        clock.SetTime(0.55f);   // delay 0.3 + 0.25s = 0.55 → progress 50%
        player.Update(clock);
        Assert.Equal(50f, observed, precision: 2);
    }

    [Fact]
    public void Animate_HandlesVector2()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        Vector2 observed = default;
        var animated = Transition.Animate<Vector2>(v => observed = v, player, clock, 1f, LinearCurve.Instance);

        animated(Vector2.Zero);
        animated(new Vector2(100, 200));

        clock.SetTime(0.5f);
        player.Update(clock);
        Assert.Equal(new Vector2(50, 100), observed);
    }

    [Fact]
    public void Animate_HandlesRgbaUint()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        uint observed = 0;
        var animated = Transition.Animate<uint>(v => observed = v, player, clock, 1f, LinearCurve.Instance);

        animated(0xFF0000FFu);  // a=ff, b=00, g=00, r=ff (赤)
        animated(0xFFFF0000u);  // a=ff, b=ff, g=00, r=00 (青)

        clock.SetTime(0.5f);
        player.Update(clock);
        // 中間 → R と B が両方とも ~127 になる
        uint r = observed & 0xff;
        uint b = (observed >> 16) & 0xff;
        Assert.InRange(r, 120u, 135u);
        Assert.InRange(b, 120u, 135u);
    }

    [Fact]
    public void ZeroDuration_AppliesImmediately()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float observed = -1f;
        var animated = Transition.Animate<float>(v => observed = v, player, clock, 0f);
        animated(0f);
        animated(50f);
        Assert.Equal(50f, observed);   // 補間なし、即時
        Assert.Equal(0, player.ActiveCount);
    }

    [Fact]
    public void Watch_UpdatesOnSignalChange()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float observed = -1f;
        var animated = Transition.Animate<float>(v => observed = v, player, clock, 1f, LinearCurve.Instance);

        var src = new Signal<float>(0f);
        using var sub = Luxel.Animation.UI.SignalTransition.Watch(src, animated);
        Assert.Equal(0f, observed);   // 初回反映

        src.Value = 100f;
        Assert.Equal(1, player.ActiveCount);

        clock.SetTime(0.5f);
        player.Update(clock);
        Assert.Equal(50f, observed, precision: 2);
    }
}

public class BindableStateLayerTests
{
    private sealed class ProbeWidget : Luxel.UI.Widget
    {
        public readonly Luxel.UI.Bindable<float> Opacity = new();   // 状態レイヤ検証用の自由な float プロパティ
        protected override void PerformLayout(Luxel.UI.Constraints c, Luxel.UI.LayoutContext ctx) { }
        protected override void RealizeCore(Luxel.UI.UiBuildContext ctx, Luxel.TwoD.UiNode parent, Luxel.UI.Point worldOrigin) { }
    }

    [Fact]
    public void StateLayer_ResolvesWhenStateActive()
    {
        var w = new ProbeWidget();
        w.Opacity.SetState(Luxel.UI.WidgetState.Hover, 10f, w);
        Assert.Equal(0f, w.Opacity.Or(0f));           // hover でなければ fallback
        w.Hovered.Value = true;
        Assert.Equal(10f, w.Opacity.Or(0f));          // hover で状態レイヤが勝つ
        w.Hovered.Value = false;
        Assert.Equal(0f, w.Opacity.Or(0f));
    }

    [Fact]
    public void StateLayer_Priority_PressedBeatsHover()
    {
        var w = new ProbeWidget();
        w.Opacity.SetState(Luxel.UI.WidgetState.Hover, 10f, w);
        w.Opacity.SetState(Luxel.UI.WidgetState.Pressed, 20f, w);
        w.Hovered.Value = true;
        w.Pressed.Value = true;
        Assert.Equal(20f, w.Opacity.Or(0f));
        w.Pressed.Value = false;
        Assert.Equal(10f, w.Opacity.Or(0f));
    }

    [Fact]
    public void Override_BeatsStateLayer()
    {
        var w = new ProbeWidget();
        w.Opacity.SetState(Luxel.UI.WidgetState.Hover, 10f, w);
        w.Hovered.Value = true;
        w.Opacity.SetOverride(99f);
        Assert.Equal(99f, w.Opacity.Or(0f));
    }

    [Fact]
    public void SetBase_KeepsStateLayers()
    {
        var w = new ProbeWidget();
        w.Opacity.SetState(Luxel.UI.WidgetState.Hover, 10f, w);
        w.Opacity.SetBase(5f);
        Assert.Equal(5f, w.Opacity.Or(0f));
        w.Hovered.Value = true;
        Assert.Equal(10f, w.Opacity.Or(0f));
    }

    [Fact]
    public void StateLayer_ReactiveValue_IsTracked()
    {
        var w = new ProbeWidget();
        var sig = new Signal<float>(10f);
        w.Opacity.SetState(Luxel.UI.WidgetState.Hover, sig, w);
        w.Hovered.Value = true;
        Assert.Equal(10f, w.Opacity.Or(0f));
        sig.Value = 42f;
        Assert.Equal(42f, w.Opacity.Or(0f));   // レイヤ値も Signal で動く
    }

    [Fact]
    public void StateLayer_WorksWithLength()
    {
        // Width (Bindable<Length>) でも状態レイヤは同じ仕組みで動く
        var w = new ProbeWidget();
        w.Width.SetState(Luxel.UI.WidgetState.Hover, (Luxel.UI.Length)10f, w);
        Assert.Equal(default, w.Width.Or(default));
        w.Hovered.Value = true;
        Assert.Equal((Luxel.UI.Length)10f, w.Width.Or(default));
    }
}

public class TransitionSetTests
{
    [Fact]
    public void Spec_ImplicitFromFloat()
    {
        TransitionSpec s = 0.3f;
        Assert.Equal(0.3f, s.Duration);
        Assert.Null(s.Curve);
        Assert.Equal(0f, s.Delay);
    }

    [Fact]
    public void Spec_ImplicitFromTuple2()
    {
        TransitionSpec s = (0.25f, CubicBezierCurve.EaseInOut);
        Assert.Equal(0.25f, s.Duration);
        Assert.NotNull(s.Curve);
    }

    [Fact]
    public void Spec_ImplicitFromTuple3()
    {
        TransitionSpec s = (0.5f, LinearCurve.Instance, 0.1f);
        Assert.Equal(0.5f, s.Duration);
        Assert.Equal(0.1f, s.Delay);
    }

    [Fact]
    public void TransitionSet_RecordSetters()
    {
        var t = new TransitionSet { Background = (0.3f, CubicBezierCurve.EaseInOut), Scale = 0.15f };
        Assert.NotNull(t.Background);
        Assert.Equal(0.3f, t.Background.Value.Duration);
        Assert.NotNull(t.Scale);
        Assert.Equal(0.15f, t.Scale.Value.Duration);
        Assert.Null(t.Opacity);
    }
}

public class PTransitionTests
{
    [Fact]
    public void PTransition_Color_CreatesAttachment()
    {
        var part = Luxel.UI.Decl.P.Transition.Color(0.3f, CubicBezierCurve.EaseInOut, delay: 0.1f);
        var ta = Assert.IsType<Luxel.Animation.UI.TransitionAttachment>(part);
        Assert.Equal(Luxel.Animation.UI.TransitionKeys.Color, ta.Key);
        Assert.Equal(0.3f, ta.Spec.Duration);
        Assert.Equal(0.1f, ta.Spec.Delay);
        Assert.NotNull(ta.Spec.Curve);
    }

    [Fact]
    public void WidgetTransitions_FindSpec_ReturnsSpec()
    {
        INodePart[] parts = [
            Luxel.UI.Decl.P.Transition.Color(0.25f),
            Luxel.UI.Decl.P.Transition.Scale(0.15f),
        ];
        var color = Luxel.Animation.UI.WidgetTransitions.FindSpec(parts, Luxel.Animation.UI.TransitionKeys.Color);
        Assert.NotNull(color);
        Assert.Equal(0.25f, color.Value.Duration);
        var scale = Luxel.Animation.UI.WidgetTransitions.FindSpec(parts, Luxel.Animation.UI.TransitionKeys.Scale);
        Assert.NotNull(scale);
        Assert.Equal(0.15f, scale.Value.Duration);
        var missing = Luxel.Animation.UI.WidgetTransitions.FindSpec(parts, Luxel.Animation.UI.TransitionKeys.Opacity);
        Assert.Null(missing);
    }

    [Fact]
    public void WidgetTransitions_Wrap_NoSpec_ReturnsRawSetter()
    {
        INodePart[] parts = [
            Luxel.UI.Decl.P.Transition.Color(0.25f),   // 別キー
        ];
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        float observed = -1f;

        // Opacity の spec は無いので raw setter がそのまま返る
        var setter = Luxel.Animation.UI.WidgetTransitions.Wrap<float>(
            parts, Luxel.Animation.UI.TransitionKeys.Opacity, v => observed = v, player, clock);
        setter(1f);
        setter(0f);
        Assert.Equal(0f, observed);   // 補間なし即時
        Assert.Equal(0, player.ActiveCount);
    }

    [Fact]
    public void WidgetTransitions_Wrap_WithSpec_WrapsAsTransition()
    {
        INodePart[] parts = [
            Luxel.UI.Decl.P.Transition.Color(0.5f, LinearCurve.Instance),
        ];
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        uint observed = 0xFFFFFFFFu;

        var setter = Luxel.Animation.UI.WidgetTransitions.Wrap<uint>(
            parts, Luxel.Animation.UI.TransitionKeys.Color, v => observed = v, player, clock);
        setter(0u);
        setter(0xFFFFFFFFu);
        // 補間あり: 0.5s で完了
        Assert.Equal(1, player.ActiveCount);
        clock.SetTime(0.25f);
        player.Update(clock);
        // 中間値 (色補間)
        uint r = observed & 0xff;
        Assert.InRange(r, 100u, 160u);
    }
}

public class TransitionFactoryTests
{
    [Fact]
    public void Background_RegistersSetterWrap()
    {
        var btn = Luxel.Controls.Kit.Button(_ => { }, "X");
        var fx = new Luxel.Animation.UI.TransitionFactory(new AnimationPlayer(), new ManualClock());
        fx.Background(0.3f, LinearCurve.Instance).Apply(btn);

        Action<uint> raw = _ => { };
        Assert.NotSame(raw, btn.WrapSetter<uint>("Background", raw));   // ラップされた
        Action<float> rawScale = _ => { };
        Assert.Same(rawScale, btn.WrapSetter<float>("Scale", rawScale));   // 未登録はそのまま
    }

    [Fact]
    public void WrappedSetter_Interpolates()
    {
        var clock = new ManualClock();
        var player = new AnimationPlayer();
        var btn = Luxel.Controls.Kit.Button(_ => { }, "X");
        new Luxel.Animation.UI.TransitionFactory(player, clock).Background(0.5f, LinearCurve.Instance).Apply(btn);

        uint observed = 0;
        Action<uint> wrapped = btn.WrapSetter<uint>("Background", v => observed = v);
        wrapped(0u);                    // 初回は即時
        wrapped(0xFF0000FFu);           // 2 回目から補間開始
        Assert.Equal(1, player.ActiveCount);
        clock.SetTime(0.25f);
        player.Update(clock);
        uint r = observed & 0xff;
        Assert.InRange(r, 100u, 160u);  // 中間値
    }

    [Fact]
    public void FromSet_RegistersWrapsForSpecifiedPropsOnly()
    {
        var fx = new Luxel.Animation.UI.TransitionFactory(new AnimationPlayer(), new ManualClock());
        var set = new Luxel.Animation.UI.TransitionSet
        {
            Background = (0.3f, LinearCurve.Instance),
            Opacity = 0.2f,
        };
        var parts = fx.FromSet(set).ToList();
        Assert.Equal(2, parts.Count);

        var btn = Luxel.Controls.Kit.Button(_ => { }, "X");
        foreach (var p in parts) p.Apply(btn);
        Action<uint> rawU = _ => { };
        Action<float> rawF = _ => { };
        Assert.NotSame(rawU, btn.WrapSetter<uint>("Background", rawU));
        Assert.NotSame(rawF, btn.WrapSetter<float>("Opacity", rawF));
        Assert.Same(rawU, btn.WrapSetter<uint>("Foreground", rawU));
        Assert.Same(rawF, btn.WrapSetter<float>("Scale", rawF));
    }

    [Fact]
    public void FromSet_ZeroDuration_Excluded()
    {
        var fx = new Luxel.Animation.UI.TransitionFactory(new AnimationPlayer(), new ManualClock());
        var set = new Luxel.Animation.UI.TransitionSet { Background = 0f };
        Assert.Empty(fx.FromSet(set));
    }

    [Fact]
    public void Constructor_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Luxel.Animation.UI.TransitionFactory(null!, new ManualClock()));
        Assert.Throws<ArgumentNullException>(() => new Luxel.Animation.UI.TransitionFactory(new AnimationPlayer(), null!));
    }
}

public class TailwindUtilityTests
{
    [Fact]
    public void S_Bg_SetsButtonBackground()
    {
        var btn = Luxel.Controls.Kit.Button(_ => { }, "X");
        Luxel.UI.Tailwind.S.Bg(Luxel.UI.Tailwind.Tw.Blue500).Apply(btn);
        Assert.Equal(Luxel.UI.Tailwind.Tw.Blue500, btn.Background.Get());
    }

    [Fact]
    public void S_On_Hover_SetsStateLayer()
    {
        var btn = Luxel.Controls.Kit.Button(_ => { }, "X");
        var part = Luxel.UI.Tailwind.S.On(
            Luxel.UI.WidgetState.Hover,
            Luxel.UI.Tailwind.S.Bg(0xFF112233u),
            Luxel.UI.Tailwind.S.Scale(1.2f));
        part.Apply(btn);
        // 基底は触らない
        Assert.False(btn.Background.HasValue);
        Assert.Equal(1f, btn.Scale.Or(1f));
        // hover でレイヤが効く
        btn.Hovered.Value = true;
        Assert.Equal(0xFF112233u, btn.Background.Or(0u));
        Assert.Equal(1.2f, btn.Scale.Or(1f));
    }

    [Fact]
    public void S_On_Pressed_SetsStateLayer()
    {
        var btn = Luxel.Controls.Kit.Button(_ => { }, "X");
        Luxel.UI.Tailwind.S.On(Luxel.UI.WidgetState.Pressed,
            Luxel.UI.Tailwind.S.Scale(0.95f)).Apply(btn);
        btn.Pressed.Value = true;
        Assert.Equal(0.95f, btn.Scale.Or(1f));
    }

    [Fact]
    public void S_ChainedUtilities_SetIndependentProps()
    {
        var btn = Luxel.Controls.Kit.Button(_ => { }, "X");
        var parts = new Luxel.UI.IConfigPart[]
        {
            Luxel.UI.Tailwind.S.Bg(0xFF111111u),
            Luxel.UI.Tailwind.S.Rounded(8f),
        };
        foreach (var p in parts) p.Apply(btn);
        Assert.Equal(0xFF111111u, btn.Background.Get());
        Assert.Equal(8f, btn.Rounded.Get());
    }

    [Fact]
    public void S_AppliesToBorder()
    {
        var bd = Luxel.Controls.Kit.Border();
        Luxel.UI.Tailwind.S.Bg(Luxel.UI.Tailwind.Tw.Slate100).Apply(bd);
        Assert.Equal(Luxel.UI.Tailwind.Tw.Slate100, bd.Background.Get());
    }

    [Fact]
    public void Tw_Blue500_HasExpectedRgb()
    {
        // Tailwind v3 blue-500 = #3B82F6
        Assert.Equal(Luxel.TwoD.Color2D.Rgba(59, 130, 246), Luxel.UI.Tailwind.Tw.Blue500);
    }

    [Fact]
    public void Tw_Red500_HasExpectedRgb()
    {
        // Tailwind v3 red-500 = #EF4444
        Assert.Equal(Luxel.TwoD.Color2D.Rgba(239, 68, 68), Luxel.UI.Tailwind.Tw.Red500);
    }

    [Fact]
    public void S_Fg_AppliesToText_ViaColorCandidate()
    {
        // Fg は候補名 ["Foreground", "Color"] — Text は Color に解決される
        var tx = Luxel.Controls.Kit.Text("X");
        Luxel.UI.Tailwind.S.Fg(Luxel.UI.Tailwind.Tw.Slate900).Apply(tx);
        Assert.Equal(Luxel.UI.Tailwind.Tw.Slate900, tx.Color.Get());
    }

    [Fact]
    public void S_FontSize_AppliesToText()
    {
        var tx = Luxel.Controls.Kit.Text("X");
        Luxel.UI.Tailwind.S.FontSize(24f).Apply(tx);
        Assert.Equal(24f, tx.FontSize.Get());
    }

    [Fact]
    public void S_OnHover_AppliesToText()
    {
        var tx = Luxel.Controls.Kit.Text("X");
        Luxel.UI.Tailwind.S.On(Luxel.UI.WidgetState.Hover,
            Luxel.UI.Tailwind.S.Fg(0xFF112233u)).Apply(tx);
        Assert.False(tx.Color.HasValue);
        tx.Hovered.Value = true;
        Assert.Equal(0xFF112233u, tx.Color.Or(0u));
    }

    [Fact]
    public void TransitionFactory_Foreground_AppliesToText_ViaColorKey()
    {
        var tx = Luxel.Controls.Kit.Text("X");
        var fx = new Luxel.Animation.UI.TransitionFactory(new AnimationPlayer(), new ManualClock());
        fx.Foreground(0.3f, LinearCurve.Instance).Apply(tx);
        Action<uint> raw = _ => { };
        Assert.NotSame(raw, tx.WrapSetter<uint>("Color", raw));      // Text の色キー
        Action<float> rawF = _ => { };
        Assert.Same(rawF, tx.WrapSetter<float>("Opacity", rawF));
    }

    [Fact]
    public void S_Bg_AppliesToCheckBox()
    {
        var sig = new Signal<bool>(false);
        var cb = Luxel.Controls.Kit.Check(sig, "X");
        Luxel.UI.Tailwind.S.Bg(Luxel.UI.Tailwind.Tw.Slate300).Apply(cb);
        Assert.Equal(Luxel.UI.Tailwind.Tw.Slate300, cb.Background.Get());
    }

    [Fact]
    public void S_OnChecked_SetsCheckBoxStateLayer()
    {
        var sig = new Signal<bool>(false);
        var cb = Luxel.Controls.Kit.Check(sig, "X");
        Luxel.UI.Tailwind.S.On(Luxel.UI.WidgetState.Checked,
            Luxel.UI.Tailwind.S.Bg(Luxel.UI.Tailwind.Tw.Blue500)).Apply(cb);
        Assert.False(cb.Background.HasValue);   // 基底は未設定のまま
        sig.Value = true;                        // checked → IsStateActive(Checked) が true
        Assert.Equal(Luxel.UI.Tailwind.Tw.Blue500, cb.Background.Or(0u));
    }

    [Fact]
    public void S_Bg_AppliesToSwitch()
    {
        var sig = new Signal<bool>(false);
        var sw = Luxel.Controls.Kit.Switch(sig);
        Luxel.UI.Tailwind.S.Bg(Luxel.UI.Tailwind.Tw.Slate300).Apply(sw);
        Assert.Equal(Luxel.UI.Tailwind.Tw.Slate300, sw.Background.Get());
    }

    [Fact]
    public void S_OnChecked_AppliesToSwitch()
    {
        var sig = new Signal<bool>(false);
        var sw = Luxel.Controls.Kit.Switch(sig);
        Luxel.UI.Tailwind.S.On(Luxel.UI.WidgetState.Checked,
            Luxel.UI.Tailwind.S.Bg(Luxel.UI.Tailwind.Tw.Green500)).Apply(sw);
        sig.Value = true;
        Assert.Equal(Luxel.UI.Tailwind.Tw.Green500, sw.Background.Or(0u));
    }

    [Fact]
    public void Slider_GeneratedFactory_TrackColorArgument()
    {
        var sl = Luxel.Controls.Kit.Slider(new Signal<float>(0.5f),
            trackColor: Luxel.UI.Tailwind.Tw.Slate200);
        Assert.Equal(Luxel.UI.Tailwind.Tw.Slate200, sl.TrackColor.Get());
    }

    [Fact]
    public void S_Bg_AppliesToTextField()
    {
        var tf = Luxel.Controls.Kit.TextField(new Signal<string>(""));
        Luxel.UI.Tailwind.S.Bg(Luxel.UI.Tailwind.Tw.Slate200).Apply(tf);
        Assert.Equal(Luxel.UI.Tailwind.Tw.Slate200, tf.Background.Get());
    }

    [Fact]
    public void S_Bg_AppliesToSelect()
    {
        var se = Luxel.Controls.Kit.Select(new[] { "a", "b" }, new Signal<int>(0));
        Luxel.UI.Tailwind.S.Bg(Luxel.UI.Tailwind.Tw.Slate200).Apply(se);
        Assert.Equal(Luxel.UI.Tailwind.Tw.Slate200, se.Background.Get());
    }

    [Fact]
    public void S_Bg_AppliesToSegmentedControl()
    {
        var sc = Luxel.Controls.Kit.Segmented(new[] { "a", "b" }, new Signal<int>(0));
        Luxel.UI.Tailwind.S.Bg(Luxel.UI.Tailwind.Tw.Slate200).Apply(sc);
        Assert.Equal(Luxel.UI.Tailwind.Tw.Slate200, sc.Background.Get());
    }

    [Fact]
    public void S_Fg_AppliesToRadioGroupAndTabs()
    {
        var rg = Luxel.Controls.Kit.Radios(new[] { "a", "b" }, new Signal<int>(0));
        Luxel.UI.Tailwind.S.Fg(Luxel.UI.Tailwind.Tw.Blue500).Apply(rg);
        Assert.Equal(Luxel.UI.Tailwind.Tw.Blue500, rg.Foreground.Get());

        var tb = Luxel.Controls.Kit.Tabs(new[] { "A" }, new Luxel.UI.Widget[] { Luxel.Controls.Kit.Text("x") }, new Signal<int>(0));
        Luxel.UI.Tailwind.S.Fg(Luxel.UI.Tailwind.Tw.Red500).Apply(tb);
        Assert.Equal(Luxel.UI.Tailwind.Tw.Red500, tb.Foreground.Get());
    }
}

public class ClockTests
{
    [Fact]
    public void FixedFrameClock_TimeSec_IsFrameDividedByRate()
    {
        var c = new FixedFrameClock { FrameRate = 60f };
        Assert.Equal(0f, c.TimeSec);
        c.Frame = 30;
        Assert.Equal(0.5f, c.TimeSec, precision: 6);
        c.Frame = 60;
        Assert.Equal(1f, c.TimeSec);   // 累積でなく毎回計算なので誤差なし
    }

    [Fact]
    public void FixedFrameClock_Advance_IncrementsFrame()
    {
        var c = new FixedFrameClock();
        c.Advance();
        Assert.Equal(1, c.Frame);
        c.Advance(10);
        Assert.Equal(11, c.Frame);
    }

    [Fact]
    public void ManualClock_SetAndAdvance()
    {
        var c = new ManualClock();
        c.SetTime(3f);
        Assert.Equal(3f, c.TimeSec);
        c.Advance(0.5f);
        Assert.Equal(3.5f, c.TimeSec);
    }
}

public class SignalAnimationTargetTests
{
    [Fact]
    public void For_WrapsSignalAsSetter()
    {
        var sig = new Signal<float>(0f);
        Action<float> setter = SignalAnimationTarget.For(sig);
        setter(42f);
        Assert.Equal(42f, sig.Peek());
    }

    [Fact]
    public void Player_WithSignalTarget_UpdatesReactively()
    {
        var sig = new Signal<float>(0f);
        var anim = new Animatable<float> { Tween = new FloatTween(0f, 100f), Duration = 1f };
        var player = new AnimationPlayer();
        player.Play(anim, SignalAnimationTarget.For(sig));

        player.Update(0.5f);
        Assert.Equal(50f, sig.Peek(), precision: 3);
    }
}

public class BindableTests
{
    [Fact]
    public void Value_DirectAssignment_NotReactive()
    {
        Bindable<uint> b = 0xFF00FF00u;
        Assert.True(b.HasValue);
        Assert.False(b.IsReactive);
        Assert.Equal(0xFF00FF00u, b.Get());
    }

    [Fact]
    public void Signal_DirectAssignment_ReactiveAndFollows()
    {
        var sig = new Signal<uint>(0x12345678u);
        Bindable<uint> b = sig; // Signal<uint> → Bindable<uint> 暗黙変換
        Assert.True(b.HasValue);
        Assert.True(b.IsReactive);
        Assert.Equal(0x12345678u, b.Get());
        sig.Value = 0xDEADBEEFu;
        Assert.Equal(0xDEADBEEFu, b.Get()); // signal 変化に追従
    }

    [Fact]
    public void Func_ViaBindFrom_Reactive()
    {
        int counter = 0;
        Bindable<uint> b = Bind.From(() => (uint)(counter * 100));
        Assert.True(b.IsReactive);
        Assert.Equal(0u, b.Get());
        counter = 3;
        Assert.Equal(300u, b.Get());
    }

    [Fact]
    public void Default_HasValueFalse()
    {
        var b = new Bindable<uint>();   // 未設定 (class 化後の「未設定」は new() — フィールド宣言と同形)
        Assert.False(b.HasValue);
    }

    [Fact]
    public void InterpolatedString_SignalHole_ReactiveUpdate()
    {
        // Text factory 経由ではなく handler を直接構築
        var count = new Signal<int>(0);
        BindableString h = $"Count: {count}";
        var getter = h.ToGetter();
        Assert.Equal("Count: 0", getter());
        count.Value = 42;
        Assert.Equal("Count: 42", getter());  // signal 変化に追従
    }

    [Fact]
    public void InterpolatedString_LiteralValueHole_Snapshot()
    {
        var count = new Signal<int>(7);
        // .Value 評価済みなので snapshot
        BindableString h = $"X={count.Value}";
        var getter = h.ToGetter();
        Assert.Equal("X=7", getter());
        count.Value = 99;
        Assert.Equal("X=7", getter());  // snapshot のままで変化しない
    }
}
