using Friflo.Engine.ECS;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Xunit;

namespace Luxel.Tests;

/// <summary>morph target 重みの再生 (SceneAnimationPlayer の weights channel → MorphWeights component)。GPU 不要。</summary>
public class MorphTests
{
    private static (Luxel.Ecs.World World, Entity Entity, SceneAnimationPlayer Player) Setup(AssetInterpolation interp)
    {
        var world = new Luxel.Ecs.World();
        var assets = new SceneAssets();

        // 2 morph target を持つ mesh (targetCount = 2)
        var mesh = new AssetMesh();
        mesh.Primitives.Add(new AssetPrimitive { MorphTargets = [new AssetMorphTarget(), new AssetMorphTarget()] });
        var node = new AssetNode { Mesh = mesh };

        Entity entity = world.CreateEntity();
        entity.AddComponent(new MorphWeights(new float[2]));
        assets.NodeEntities[node] = entity;

        // weights sampler: flat float[] = keyCount(2) × targetCount(2)。t0=(0,0), t1=(1, 0.5)
        var sampler = new AssetAnimationSampler
        {
            Times = [0f, 1f],
            Values = new float[] { 0f, 0f, 1f, 0.5f },
            Interpolation = interp,
        };
        var anim = new AssetAnimation();
        anim.Channels.Add(new AssetAnimationChannel { Path = AssetAnimationPath.Weights, Sampler = sampler, TargetNode = node });

        return (world, entity, new SceneAnimationPlayer(world, assets, anim));
    }

    [Fact]
    public void WeightsChannel_AtKeyframe_SetsWeights()
    {
        (_, Entity entity, SceneAnimationPlayer player) = Setup(AssetInterpolation.Linear);
        player.Sample(1.0f);
        float[] w = entity.GetComponent<MorphWeights>().Weights;
        Assert.Equal(1f, w[0], 5);
        Assert.Equal(0.5f, w[1], 5);
    }

    [Fact]
    public void WeightsChannel_MidSegment_Lerps()
    {
        (_, Entity entity, SceneAnimationPlayer player) = Setup(AssetInterpolation.Linear);
        player.Sample(0.5f);   // t0..t1 の中間 → 線形補間
        float[] w = entity.GetComponent<MorphWeights>().Weights;
        Assert.Equal(0.5f, w[0], 4);    // 0 → 1 の中間
        Assert.Equal(0.25f, w[1], 4);   // 0 → 0.5 の中間
    }

    [Fact]
    public void WeightsChannel_Step_HoldsKeyframe()
    {
        (_, Entity entity, SceneAnimationPlayer player) = Setup(AssetInterpolation.Step);
        player.Sample(0.5f);   // STEP → 補間せず t0 の値を保持
        float[] w = entity.GetComponent<MorphWeights>().Weights;
        Assert.Equal(0f, w[0], 5);
        Assert.Equal(0f, w[1], 5);
    }
}
