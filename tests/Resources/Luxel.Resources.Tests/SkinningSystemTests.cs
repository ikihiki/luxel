using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Luxel.Ecs;
using Xunit;

namespace Luxel.Tests;

/// <summary>SkinningSystem の joint 行列計算 (GPU 不要)。行列 = InverseBind × jointWorld を数値で検証。</summary>
public class SkinningSystemTests
{
    [Fact]
    public void JointMatrix_IsInverseBindTimesJointWorld()
    {
        var world = new Luxel.Ecs.World();
        var assets = new SceneAssets();

        var jointNode = new AssetNode { Name = "joint" };
        var skin = new AssetSkin();
        skin.Joints.Add(jointNode);
        skin.InverseBindMatrices = [Matrix4x4.Identity];   // InverseBind = I

        Entity jointEntity = world.CreateEntity();
        Matrix4x4 jointWorld = Matrix4x4.CreateTranslation(0, 5, 0);
        jointEntity.AddComponent(new Luxel.Ecs.GlobalTransform(jointWorld));
        assets.NodeEntities[jointNode] = jointEntity;

        Entity meshEntity = world.CreateEntity();
        meshEntity.AddComponent(new AssetSkinRef(skin));

        SkinningSystem.Run(world, assets);

        Matrix4x4[] mats = meshEntity.GetComponent<JointMatrices>().Matrices;
        Assert.Single(mats);
        Assert.Equal(jointWorld, mats[0]);   // I × jointWorld = jointWorld
    }

    [Fact]
    public void JointMatrix_AtBindPose_IsIdentity()
    {
        var world = new Luxel.Ecs.World();
        var assets = new SceneAssets();

        Matrix4x4 bindWorld = Matrix4x4.CreateFromYawPitchRoll(0.3f, 0.2f, 0.1f) * Matrix4x4.CreateTranslation(1, 2, 3);
        Assert.True(Matrix4x4.Invert(bindWorld, out Matrix4x4 invBind));

        var jointNode = new AssetNode();
        var skin = new AssetSkin();
        skin.Joints.Add(jointNode);
        skin.InverseBindMatrices = [invBind];

        Entity jointEntity = world.CreateEntity();
        jointEntity.AddComponent(new Luxel.Ecs.GlobalTransform(bindWorld));   // jointWorld == bindWorld
        assets.NodeEntities[jointNode] = jointEntity;

        Entity meshEntity = world.CreateEntity();
        meshEntity.AddComponent(new AssetSkinRef(skin));

        SkinningSystem.Run(world, assets);

        // バインドポーズ: InverseBind × jointWorld = inverse(bindWorld) × bindWorld ≈ I (頂点は不変)
        Matrix4x4 m = meshEntity.GetComponent<JointMatrices>().Matrices[0];
        float[] e = [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
                     m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
        float[] id = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        for (int i = 0; i < 16; i++) Assert.Equal(id[i], e[i], 4);
    }

    [Fact]
    public void MissingJointEntity_FallsBackToIdentity()
    {
        var world = new Luxel.Ecs.World();
        var assets = new SceneAssets();

        var skin = new AssetSkin();
        skin.Joints.Add(new AssetNode());   // NodeEntities に無い → 単位行列でフォールバック
        skin.InverseBindMatrices = [Matrix4x4.CreateTranslation(9, 9, 9)];

        Entity meshEntity = world.CreateEntity();
        meshEntity.AddComponent(new AssetSkinRef(skin));

        SkinningSystem.Run(world, assets);

        Assert.Equal(Matrix4x4.Identity, meshEntity.GetComponent<JointMatrices>().Matrices[0]);
    }
}
