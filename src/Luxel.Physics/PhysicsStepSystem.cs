using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using Friflo.Engine.ECS;
using Friflo.Engine.ECS.Systems;
using Luxel.Ecs;

namespace Luxel.Physics;

/// <summary>
/// ECS と <see cref="PhysicsWorld"/> のブリッジ (毎フレーム 1 回):
/// <list type="number">
/// <item><b>Attach</b> — 未発行の <see cref="Collider"/> + <see cref="RigidBody"/>/<see cref="StaticBody"/> entity に
///   body/static を発行 (初期 pose は LocalTransform の分解から)</item>
/// <item><b>Step</b> — 固定タイムステップ accumulator (<see cref="PhysicsWorld.Step"/>)</item>
/// <item><b>Write-back</b> — 動的ボディの pose を <c>LocalTransform = Scale(RenderScale) × Rotation × Translation</c> で書き戻す</item>
/// </list>
/// 書き戻し後は既存の流れ (TransformPropagateSystem → Render3DExtractSystem) がそのまま描く。
/// <see cref="ScheduleRoot"/> に載せる場合の規約位置は <see cref="Luxel.Ecs.Phase.Update"/>。
///
/// 制約 (v1): <c>Parent</c> 付き entity の RigidBody は未定義動作 (物理はワールド空間)。
/// entity 削除に連動した body 削除は行わない — リセットは World + PhysicsWorld の丸ごと再構築で
/// (決定的な初期状態に戻る正攻法)。
/// </summary>
public sealed class PhysicsStepSystem : BaseSystem
{
    private readonly Luxel.Ecs.World _world;
    private readonly PhysicsWorld _physics;

    public PhysicsStepSystem(Luxel.Ecs.World world, PhysicsWorld physics)
    {
        _world = world;
        _physics = physics;
    }

    protected override void OnUpdateGroup() => Run(Tick.deltaTime);

    /// <summary>手動駆動 (デモ/テスト用)。dt は経過秒 — 内部で固定ステップへ分割される。</summary>
    public void Run(float dt)
    {
        AttachNewBodies();
        int steps = _physics.Step(dt);
        if (steps > 0) WriteBackPoses();
    }

    private void AttachNewBodies()
    {
        // 収集 → 適用の 2 段 (クエリ列挙中の component 書き換えを避ける)
        var newDynamic = new List<(Entity E, Collider C, RigidBody B, RigidPose Pose)>();
        _world.Query<Collider, RigidBody>().ForEachEntity((ref Collider c, ref RigidBody b, Entity e) =>
        {
            if (!b.Attached) newDynamic.Add((e, c, b, InitialPose(e)));
        });
        foreach ((Entity e, Collider c, RigidBody b, RigidPose pose) in newDynamic)
        {
            var velocity = new BodyVelocity(b.InitialVelocity);
            float mass = b.Mass > 0 ? b.Mass : 1f;
            BodyHandle handle = c.Kind switch
            {
                ColliderKind.Sphere => _physics.AddDynamic(pose, new Sphere(c.Radius), mass, velocity),
                ColliderKind.Capsule => _physics.AddDynamic(pose, new Capsule(c.Radius, c.Length), mass, velocity),
                _ => _physics.AddDynamic(pose, new Box(c.Size.X, c.Size.Y, c.Size.Z), mass, velocity),
            };
            RigidBody updated = b;
            updated.Handle = handle;
            updated.Attached = true;
            e.AddComponent(updated);   // 上書き (Friflo は AddComponent = set)
        }

        var newStatic = new List<(Entity E, Collider C, RigidPose Pose)>();
        _world.Query<Collider, StaticBody>().ForEachEntity((ref Collider c, ref StaticBody s, Entity e) =>
        {
            if (!s.Attached) newStatic.Add((e, c, InitialPose(e)));
        });
        foreach ((Entity e, Collider c, RigidPose pose) in newStatic)
        {
            TypedIndex shape = c.Kind switch
            {
                ColliderKind.Sphere => _physics.AddShape(new Sphere(c.Radius)),
                ColliderKind.Capsule => _physics.AddShape(new Capsule(c.Radius, c.Length)),
                _ => _physics.AddShape(new Box(c.Size.X, c.Size.Y, c.Size.Z)),
            };
            e.AddComponent(new StaticBody { Handle = _physics.AddStatic(pose, shape), Attached = true });
        }
    }

    /// <summary>entity の LocalTransform から初期 pose (位置 + 回転) を採る。スケールは形状 (Collider) が持つ。</summary>
    private static RigidPose InitialPose(Entity e)
    {
        if (!e.HasComponent<LocalTransform>()) return RigidPose.Identity;
        Matrix4x4 m = e.GetComponent<LocalTransform>().Matrix;
        if (!Matrix4x4.Decompose(m, out _, out Quaternion rot, out Vector3 pos))
            return new RigidPose(m.Translation);
        return new RigidPose(pos, rot);
    }

    private void WriteBackPoses()
    {
        var updates = new List<(Entity E, Matrix4x4 M)>();
        _world.Query<Collider, RigidBody>().ForEachEntity((ref Collider c, ref RigidBody b, Entity e) =>
        {
            if (!b.Attached) return;
            RigidPose pose = _physics.GetPose(b.Handle);
            Vector3 s = c.RenderScale;
            Matrix4x4 m = Matrix4x4.CreateScale(s)
                        * Matrix4x4.CreateFromQuaternion(pose.Orientation)
                        * Matrix4x4.CreateTranslation(pose.Position);
            updates.Add((e, m));
        });
        foreach ((Entity e, Matrix4x4 m) in updates)
            e.AddComponent(new LocalTransform(m));
    }
}
