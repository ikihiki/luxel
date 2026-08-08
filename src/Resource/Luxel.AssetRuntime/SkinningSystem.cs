using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.Assets;
using Luxel.Ecs;

namespace Luxel.AssetRuntime;

/// <summary>
/// 各 <see cref="AssetSkinRef"/> 持ち entity に対して、<c>joint.GlobalTransform * inverseBindMatrix</c> を
/// 計算し <see cref="JointMatrices"/> component に保存する。GPU skinning shader はこれを bindless で参照。
/// </summary>
public static class SkinningSystem
{
    public static void Run(Luxel.Ecs.World world, SceneAssets assets)
    {
        var pending = new List<(Entity Entity, Matrix4x4[] Matrices)>();
        world.Query<AssetSkinRef>().ForEachEntity((ref AssetSkinRef sr, Entity e) =>
        {
            var skin = sr.Skin;
            if (skin is null) return;
            int n = skin.Joints.Count;
            var mats = new Matrix4x4[n];
            for (int i = 0; i < n; i++)
            {
                var jointNode = skin.Joints[i];
                if (!assets.NodeEntities.TryGetValue(jointNode, out var jointEntity))
                { mats[i] = Matrix4x4.Identity; continue; }
                Matrix4x4 jointWorld = jointEntity.HasComponent<Luxel.Ecs.GlobalTransform>()
                    ? jointEntity.GetComponent<Luxel.Ecs.GlobalTransform>().Matrix
                    : Matrix4x4.Identity;
                mats[i] = (i < skin.InverseBindMatrices.Length ? skin.InverseBindMatrices[i] : Matrix4x4.Identity) * jointWorld;
            }
            pending.Add((e, mats));
        });
        foreach (var (e, mats) in pending)
        {
            if (e.HasComponent<JointMatrices>()) e.RemoveComponent<JointMatrices>();
            e.AddComponent(new JointMatrices(mats));
        }
    }
}

/// <summary>skinning 用 joint 行列配列 (per-entity)。</summary>
public struct JointMatrices : IComponent
{
    public Matrix4x4[] Matrices;
    public JointMatrices(Matrix4x4[] m) { Matrices = m; }
}

/// <summary>morph target 重み (per-entity)。<see cref="SceneAnimationPlayer"/> が weights channel から更新、
/// morph シェーダが bindless で参照。既定は node.Weights (無ければ全 0)。</summary>
public struct MorphWeights : IComponent
{
    public float[] Weights;
    public MorphWeights(float[] w) { Weights = w; }
}
