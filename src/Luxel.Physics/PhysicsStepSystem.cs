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

    private readonly List<ContactEvent> _contactEvents = new();
    private readonly List<EntityPair> _currentContacts = new();
    private readonly Dictionary<int, Entity> _bodyToEntity = new();
    private readonly Dictionary<int, Entity> _staticToEntity = new();

    public PhysicsStepSystem(Luxel.Ecs.World world, PhysicsWorld physics)
    {
        _world = world;
        _physics = physics;
    }

    /// <summary>今フレームの接触/トリガーイベント (Begin/End、Entity ベース)。フレーム内で読み切る規約。</summary>
    public IReadOnlyList<ContactEvent> ContactEvents => _contactEvents;

    /// <summary>直近ステップで接触中の Entity ペア (gizmo/デバッグ用)。<see cref="TrackCurrentContacts"/> が
    /// true のときだけ埋まる (既定 false = ゼロコスト)。</summary>
    public IReadOnlyList<EntityPair> CurrentContacts => _currentContacts;

    /// <summary>接触中ペアの毎ステップ収集を有効にするか (contact gizmo 用のオプトイン)。既定 false。</summary>
    public bool TrackCurrentContacts { get; set; }

    protected override void OnUpdateGroup() => Run(Tick.deltaTime);

    /// <summary>手動駆動 (デモ/テスト用)。dt は経過秒 — 内部で固定ステップへ分割される。実行ステップ数を返す。</summary>
    public int Run(float dt)
    {
        AttachNewBodies();
        int steps = _physics.Step(dt);
        if (steps > 0) WriteBackPoses();
        TranslateContacts();
        return steps;
    }

    /// <summary>FixedUpdate フェーズから駆動する用: 固定 dt で**必ず 1 ステップ**進める
    /// (蓄積は GameScene 側に一本化されている前提)。</summary>
    public void StepFixedOnce()
    {
        AttachNewBodies();
        _physics.StepOnce();
        WriteBackPoses();
        TranslateContacts();
    }

    /// <summary>PhysicsWorld の raw イベント (collidable ハンドル) を Entity ベースへ変換する。
    /// 逆引きマップはイベントがあるときだけ構築 (無ければゼロコスト)。</summary>
    private void TranslateContacts()
    {
        _contactEvents.Clear();
        _currentContacts.Clear();
        bool wantCurrent = TrackCurrentContacts && _physics.CurrentContacts.Count > 0;
        if (_physics.ContactEvents.Count == 0 && !wantCurrent) return;

        BuildEntityMap();
        foreach (ContactPairEvent e in _physics.ContactEvents)
            if (Resolve(e.A, out Entity a) && Resolve(e.B, out Entity b))
                _contactEvents.Add(new ContactEvent(a, b, e.Phase));
        if (wantCurrent)
            foreach (ContactPairKey k in _physics.CurrentContacts)
                if (Resolve(k.A, out Entity a) && Resolve(k.B, out Entity b))
                    _currentContacts.Add(new EntityPair(a, b));
    }

    private void BuildEntityMap()
    {
        _bodyToEntity.Clear();
        _staticToEntity.Clear();
        _world.Query<RigidBody>().ForEachEntity((ref RigidBody b, Entity e) => { if (b.Attached) _bodyToEntity[b.Handle.Value] = e; });
        _world.Query<HullCollider>().ForEachEntity((ref HullCollider h, Entity e) => { if (h.Attached) _bodyToEntity[h.Handle.Value] = e; });
        _world.Query<StaticBody>().ForEachEntity((ref StaticBody s, Entity e) => { if (s.Attached) _staticToEntity[s.Handle.Value] = e; });
        _world.Query<MeshCollider>().ForEachEntity((ref MeshCollider m, Entity e) => { if (m.Attached) _staticToEntity[m.Handle.Value] = e; });
        _world.Query<Trigger>().ForEachEntity((ref Trigger t, Entity e) => { if (t.Attached) _staticToEntity[t.Handle.Value] = e; });
    }

    private bool Resolve(BepuPhysics.Collidables.CollidableReference c, out Entity e)
    {
        if (c.Mobility == BepuPhysics.Collidables.CollidableMobility.Static)
            return _staticToEntity.TryGetValue(c.StaticHandle.Value, out e);
        return _bodyToEntity.TryGetValue(c.BodyHandle.Value, out e);
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
            bool ccd = b.Continuous;
            BodyHandle handle = c.Kind switch
            {
                ColliderKind.Sphere => _physics.AddDynamic(pose, new Sphere(c.Radius), mass, velocity, continuous: ccd),
                ColliderKind.Capsule => _physics.AddDynamic(pose, new Capsule(c.Radius, c.Length), mass, velocity, continuous: ccd),
                _ => _physics.AddDynamic(pose, new Box(c.Size.X, c.Size.Y, c.Size.Z), mass, velocity, continuous: ccd),
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
            e.AddComponent(new StaticBody { Handle = _physics.AddStatic(pose, ShapeOf(c)), Attached = true });

        var newTriggers = new List<(Entity E, Collider C, RigidPose Pose)>();
        _world.Query<Collider, Trigger>().ForEachEntity((ref Collider c, ref Trigger t, Entity e) =>
        {
            if (!t.Attached) newTriggers.Add((e, c, InitialPose(e)));
        });
        foreach ((Entity e, Collider c, RigidPose pose) in newTriggers)
            e.AddComponent(new Trigger { Handle = _physics.AddTrigger(pose, ShapeOf(c)), Attached = true });

        var newMeshes = new List<(Entity E, MeshCollider M, RigidPose Pose)>();
        _world.Query<MeshCollider>().ForEachEntity((ref MeshCollider m, Entity e) =>
        {
            if (!m.Attached) newMeshes.Add((e, m, InitialPose(e)));
        });
        foreach ((Entity e, MeshCollider m, RigidPose pose) in newMeshes)
        {
            MeshCollider updated = m;
            updated.Handle = _physics.AddStaticMesh(pose, m.Vertices, m.Indices, m.Scale);
            updated.Attached = true;
            e.AddComponent(updated);
        }

        var newHulls = new List<(Entity E, HullCollider H, RigidPose Pose)>();
        _world.Query<HullCollider>().ForEachEntity((ref HullCollider h, Entity e) =>
        {
            if (!h.Attached) newHulls.Add((e, h, InitialPose(e)));
        });
        foreach ((Entity e, HullCollider h, RigidPose pose) in newHulls)
        {
            float mass = h.Mass > 0 ? h.Mass : 1f;
            HullCollider updated = h;
            updated.Handle = _physics.AddDynamicHull(pose, h.Points, mass, out updated.Center,
                new BepuPhysics.BodyVelocity(h.InitialVelocity), continuous: h.Continuous);
            updated.Attached = true;
            e.AddComponent(updated);
        }
    }

    /// <summary>Collider から Bepu の shape を登録して TypedIndex を得る (静的/トリガー共通)。</summary>
    private TypedIndex ShapeOf(in Collider c) => c.Kind switch
    {
        ColliderKind.Sphere => _physics.AddShape(new Sphere(c.Radius)),
        ColliderKind.Capsule => _physics.AddShape(new Capsule(c.Radius, c.Length)),
        _ => _physics.AddShape(new Box(c.Size.X, c.Size.Y, c.Size.Z)),
    };

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
        // 凸包: pose.Position は重心。元の頂点原点へ戻すため Center ぶん引く (スケールは持たない = 頂点そのまま)
        _world.Query<HullCollider>().ForEachEntity((ref HullCollider h, Entity e) =>
        {
            if (!h.Attached) return;
            RigidPose pose = _physics.GetPose(h.Handle);
            Vector3 origin = pose.Position - Vector3.Transform(h.Center, pose.Orientation);
            Matrix4x4 m = Matrix4x4.CreateFromQuaternion(pose.Orientation)
                        * Matrix4x4.CreateTranslation(origin);
            updates.Add((e, m));
        });
        foreach ((Entity e, Matrix4x4 m) in updates)
            e.AddComponent(new LocalTransform(m));
    }
}
