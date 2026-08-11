using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.Ecs;
using Luxel.Scene.UI;
using Luxel.UI;

namespace Luxel.Tests;

/// <summary>Luxel.Scene.UI の PlaybackState + BoneEditor が Signal で reactive に動くか検証。</summary>
public class SceneUiTests
{
    [Fact]
    public void PlaybackState_TickAdvancesTime()
    {
        var s = new PlaybackState { };
        s.Duration.Value = 5f;
        s.IsPlaying.Value = true;
        s.Speed.Value = 1f;
        s.Tick(2f);
        Assert.Equal(2f, s.CurrentTime.Value, precision: 4);
        s.Tick(1f);
        Assert.Equal(3f, s.CurrentTime.Value, precision: 4);
    }

    [Fact]
    public void PlaybackState_LoopWraps()
    {
        var s = new PlaybackState { };
        s.Duration.Value = 2f;
        s.IsPlaying.Value = true;
        s.Speed.Value = 1f;
        s.Looped.Value = true;
        s.Tick(2.5f);  // overshoot 0.5s
        Assert.True(s.CurrentTime.Value < 2f);
        Assert.True(s.IsPlaying.Value);  // looped で playing は維持
    }

    [Fact]
    public void PlaybackState_StopsAtEndWhenNotLooped()
    {
        var s = new PlaybackState { };
        s.Duration.Value = 2f;
        s.IsPlaying.Value = true;
        s.Speed.Value = 1f;
        s.Looped.Value = false;
        s.Tick(3f);
        Assert.Equal(2f, s.CurrentTime.Value, precision: 3);  // clamped
        Assert.False(s.IsPlaying.Value);
    }

    [Fact]
    public void PlaybackState_SpeedAffectsTick()
    {
        var s = new PlaybackState { };
        s.Duration.Value = 10f;
        s.IsPlaying.Value = true;
        s.Speed.Value = 2f;
        s.Tick(1f);
        Assert.Equal(2f, s.CurrentTime.Value, precision: 3);
    }

    [Fact]
    public void PlaybackState_PausedTickIsNoOp()
    {
        var s = new PlaybackState { };
        s.Duration.Value = 5f;
        s.IsPlaying.Value = false;
        s.Tick(1f);
        Assert.Equal(0f, s.CurrentTime.Value);
    }

    [Fact]
    public void PlaybackState_SignalDrivesReactiveEffect()
    {
        // Signal を Reactive.Effect で監視 → tick で notify される
        var s = new PlaybackState { };
        s.Duration.Value = 10f;
        s.IsPlaying.Value = true;

        int notify = 0;
        float observed = -1;
        Reactive.Effect(() => { observed = s.CurrentTime.Value; notify++; });
        Assert.Equal(1, notify);  // 初回

        s.Tick(2f);
        Assert.Equal(2f, observed, precision: 3);
        Assert.True(notify >= 2);
    }

    [Fact]
    public void BoneEditor_ApplyUpdatesLocalTransform()
    {
        var world = new Luxel.Ecs.World();
        var bone = world.CreateEntity(new Luxel.Ecs.LocalTransform(Matrix4x4.Identity));
        var editor = new BoneEditor(world, new[] { bone });

        editor.SelectedIndex.Value = 0;
        editor.TX.Value = 10;
        editor.TY.Value = 5;
        editor.TZ.Value = -2;
        editor.Apply();

        var lt = bone.GetComponent<Luxel.Ecs.LocalTransform>();
        Matrix4x4.Decompose(lt.Matrix, out _, out _, out var trans);
        Assert.Equal(10f, trans.X, precision: 3);
        Assert.Equal(5f, trans.Y, precision: 3);
        Assert.Equal(-2f, trans.Z, precision: 3);
    }

    [Fact]
    public void BoneEditor_LoadsFromBoneOnSelect()
    {
        var world = new Luxel.Ecs.World();
        var b0 = world.CreateEntity(new Luxel.Ecs.LocalTransform(Matrix4x4.CreateTranslation(1, 0, 0)));
        var b1 = world.CreateEntity(new Luxel.Ecs.LocalTransform(Matrix4x4.CreateTranslation(0, 7, 0)));
        var editor = new BoneEditor(world, new[] { b0, b1 });

        // 切替で signal が bone の値を載せる
        editor.SelectedIndex.Value = 1;
        Assert.Equal(7f, editor.TY.Value, precision: 3);

        editor.SelectedIndex.Value = 0;
        Assert.Equal(1f, editor.TX.Value, precision: 3);
    }

    [Fact]
    public void BoneEditor_ApplyAffectsTransformPropagate()
    {
        var world = new Luxel.Ecs.World();
        var parent = world.CreateEntity(new Luxel.Ecs.LocalTransform(Matrix4x4.CreateTranslation(10, 0, 0)));
        var child = world.CreateEntity(new Luxel.Ecs.LocalTransform(Matrix4x4.Identity));
        child.AddComponent(new Luxel.Ecs.Parent(parent));

        var editor = new BoneEditor(world, new[] { parent, child });
        editor.SelectedIndex.Value = 1;
        editor.TX.Value = 5;
        editor.Apply();

        Luxel.AssetRuntime.TransformPropagateSystem.Run(world);
        var childWorld = child.GetComponent<Luxel.Ecs.GlobalTransform>().Matrix;
        Vector3 p = Vector3.Transform(Vector3.Zero, childWorld);
        Assert.Equal(15f, p.X, precision: 3);  // parent(10) + child(5)
    }

    [Fact]
    public void PlaybackState_IntegrationWithAnimation()
    {
        // PlaybackState を SceneAnimationPlayer と連動 ─ CurrentTime → Sample(t) で TRS 更新
        var world = new Luxel.Ecs.World();
        var e = world.CreateEntity(new Luxel.Ecs.LocalTransform(Matrix4x4.Identity));
        var assets = new SceneAssets();
        var targetNode = new AssetNode();
        assets.NodeEntities[targetNode] = e;

        var anim = new AssetAnimation { Name = "t", Duration = 2f };
        anim.Channels.Add(new AssetAnimationChannel
        {
            TargetNode = targetNode,
            Path = AssetAnimationPath.Translation,
            Sampler = new AssetAnimationSampler
            {
                Times = new[] { 0f, 2f },
                Values = new[] { Vector3.Zero, new Vector3(20, 0, 0) },
                Interpolation = AssetInterpolation.Linear,
            },
        });
        var player = new SceneAnimationPlayer(world, assets, anim);
        var pb = new PlaybackState { };
        pb.Duration.Value = anim.Duration;
        pb.IsPlaying.Value = true;
        pb.Speed.Value = 1f;

        // 1s tick → currentTime = 1s → sample で半分 (10, 0, 0)
        pb.Tick(1f);
        player.Sample(pb.CurrentTime.Value);
        var lt = e.GetComponent<Luxel.Ecs.LocalTransform>();
        Matrix4x4.Decompose(lt.Matrix, out _, out _, out var trans);
        Assert.Equal(10f, trans.X, precision: 3);
    }
}
