using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.Assets;
using Luxel.Ecs;

namespace Luxel.AssetRuntime;

/// <summary>
/// <see cref="AssetAnimation"/> を駆動する sampler。指定時刻 t の値を求めて
/// <see cref="SceneAssets.NodeEntities"/> の LocalTransform を更新する。
///
/// direct-ref モデル: channel の TargetNode 直接参照で entity を引く。
/// </summary>
public sealed class SceneAnimationPlayer
{
    private readonly Luxel.Ecs.World _world;
    private readonly SceneAssets _assets;
    private readonly AssetAnimation _anim;

    public SceneAnimationPlayer(Luxel.Ecs.World world, SceneAssets assets, AssetAnimation anim)
    {
        _world = world;
        _assets = assets;
        _anim = anim;
    }

    /// <summary>時刻 t (秒、0..Duration) の値で全 channel を sample して ECS に反映。</summary>
    public void Sample(float t)
    {
        foreach (var ch in _anim.Channels)
        {
            if (ch.TargetNode is null) continue;
            if (!_assets.NodeEntities.TryGetValue(ch.TargetNode, out var entity)) continue;

            if (ch.Path == AssetAnimationPath.Weights) { ApplyWeights(entity, ch.TargetNode, ch.Sampler, t); continue; }
            if (!entity.HasComponent<Luxel.Ecs.LocalTransform>()) continue;

            var lt = entity.GetComponent<Luxel.Ecs.LocalTransform>();
            if (!Matrix4x4.Decompose(lt.Matrix, out var scale, out var rot, out var trans))
            { scale = Vector3.One; rot = Quaternion.Identity; trans = Vector3.Zero; }

            switch (ch.Path)
            {
                case AssetAnimationPath.Translation: trans = SampleVec3(ch.Sampler, t); break;
                case AssetAnimationPath.Rotation: rot = SampleQuat(ch.Sampler, t); break;
                case AssetAnimationPath.Scale: scale = SampleVec3(ch.Sampler, t); break;
            }

            var newMat = Matrix4x4.CreateScale(scale)
                       * Matrix4x4.CreateFromQuaternion(rot)
                       * Matrix4x4.CreateTranslation(trans);
            entity.RemoveComponent<Luxel.Ecs.LocalTransform>();
            entity.AddComponent(new Luxel.Ecs.LocalTransform(newMat));
        }
    }

    /// <summary>weights channel (morph 重み) を sample して <see cref="MorphWeights"/> component へ書く。
    /// sampler の出力は flat float[] (keyCount × targetCount)。</summary>
    private void ApplyWeights(Entity entity, AssetNode node, AssetAnimationSampler s, float t)
    {
        int tc = node.Mesh is { Primitives.Count: > 0 } m ? (m.Primitives[0].MorphTargets?.Count ?? 0) : 0;
        if (tc == 0) return;
        var vals = (float[])s.Values;
        if (vals.Length == 0) return;

        var (i, u) = FindSegment(s.Times, t);
        int next = Math.Min(i + 1, s.Times.Length - 1);
        bool lerp = u > 0 && s.Interpolation != AssetInterpolation.Step;
        var w = new float[tc];
        for (int k = 0; k < tc; k++)
        {
            float a = i * tc + k < vals.Length ? vals[i * tc + k] : 0f;
            if (!lerp) { w[k] = a; continue; }
            float b = next * tc + k < vals.Length ? vals[next * tc + k] : a;
            w[k] = a + (b - a) * u;
        }
        if (entity.HasComponent<MorphWeights>()) entity.RemoveComponent<MorphWeights>();
        entity.AddComponent(new MorphWeights(w));
    }

    private static (int idx, float u) FindSegment(float[] times, float t)
    {
        if (times.Length == 0) return (0, 0);
        if (t <= times[0]) return (0, 0);
        if (t >= times[^1]) return (times.Length - 1, 0);
        for (int i = 0; i < times.Length - 1; i++)
        {
            if (t >= times[i] && t < times[i + 1])
                return (i, (t - times[i]) / (times[i + 1] - times[i]));
        }
        return (times.Length - 1, 0);
    }

    private static Vector3 SampleVec3(AssetAnimationSampler s, float t)
    {
        var vals = (Vector3[])s.Values;
        if (vals.Length == 0) return Vector3.Zero;
        var (i, u) = FindSegment(s.Times, t);
        if (u == 0 || s.Interpolation == AssetInterpolation.Step) return vals[i];
        return Vector3.Lerp(vals[i], vals[Math.Min(i + 1, vals.Length - 1)], u);
    }

    private static Quaternion SampleQuat(AssetAnimationSampler s, float t)
    {
        var vals = (Quaternion[])s.Values;
        if (vals.Length == 0) return Quaternion.Identity;
        var (i, u) = FindSegment(s.Times, t);
        if (u == 0 || s.Interpolation == AssetInterpolation.Step) return vals[i];
        return Quaternion.Slerp(vals[i], vals[Math.Min(i + 1, vals.Length - 1)], u);
    }
}
