using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;

namespace Luxel.Physics;

/// <summary>
/// BepuPhysics v2 の薄いラッパ — <see cref="Simulation"/>/<see cref="BufferPool"/> の生成/破棄と
/// callbacks boilerplate を隠蔽し、固定タイムステップの accumulator を提供する。
///
/// <para><b>決定性</b>: 既定 (ThreadCount = 0 = 単スレッド) では、同じ初期状態 + 同じステップ列は
/// 常に同じ結果になる — snap 回帰 (固定 1/60s ステップ) と両立する。マルチスレッドは opt-in で、
/// 速い代わりに決定性を失う。</para>
///
/// <para><b>使い方</b>:</para>
/// <code>
/// using var physics = new PhysicsWorld();
/// var floor = physics.AddStatic(new RigidPose(new Vector3(0, -0.5f, 0)), physics.AddShape(new Box(8, 1, 8)));
/// var box = physics.AddDynamic(new RigidPose(new Vector3(0, 3, 0)), new Box(1, 1, 1), mass: 1);
/// physics.Step(dt);                       // 毎フレーム (固定 dt へ分割される)
/// RigidPose pose = physics.GetPose(box);  // 描画へ反映
/// </code>
/// ECS で使う場合は <see cref="PhysicsStepSystem"/> が Attach/Step/書き戻しを担う。
/// </summary>
public sealed class PhysicsWorld : IDisposable
{
    private readonly ThreadDispatcher? _dispatcher;
    private float _accumulator;

    private readonly PhysicsContacts _contacts = new();
    private readonly HashSet<ContactPairKey> _prevPairs = new();
    private readonly List<ContactPairEvent> _contactEvents = new();

    /// <summary>Bepu の生 Simulation。高度な操作 (constraint 等) はこれ経由で。</summary>
    public Simulation Simulation { get; }
    /// <summary>Bepu のアンマネージドメモリプール (Simulation と寿命を共にする)。</summary>
    public BufferPool BufferPool { get; }
    /// <summary>固定タイムステップ (秒)。</summary>
    public float FixedDt { get; }

    public PhysicsWorld(PhysicsSettings? settings = null)
    {
        PhysicsSettings s = settings ?? new PhysicsSettings();
        FixedDt = MathF.Max(1e-4f, s.FixedDt);
        BufferPool = new BufferPool();
        if (s.ThreadCount > 0) _dispatcher = new ThreadDispatcher(s.ThreadCount);
        Simulation = Simulation.Create(
            BufferPool,
            new LuxelNarrowPhaseCallbacks
            {
                FrictionCoefficient = s.Friction,
                MaximumRecoveryVelocity = s.MaximumRecoveryVelocity,
                ContactSpringiness = s.ContactSpring,
                Contacts = _contacts,
            },
            new LuxelPoseIntegratorCallbacks
            {
                Gravity = s.Gravity,
                LinearDamping = s.LinearDamping,
                AngularDamping = s.AngularDamping,
            },
            new SolveDescription(s.SolverVelocityIterations, s.SolverSubsteps));
    }

    // callbacks は Simulation 内へ値コピーされる — 実行時変更はコピー先をキャストで触る
    private ref LuxelPoseIntegratorCallbacks PoseCallbacks
        => ref ((PoseIntegrator<LuxelPoseIntegratorCallbacks>)Simulation.PoseIntegrator).Callbacks;
    private ref LuxelNarrowPhaseCallbacks NarrowCallbacks
        => ref ((NarrowPhase<LuxelNarrowPhaseCallbacks>)Simulation.NarrowPhase).Callbacks;

    /// <summary>重力 (実行時変更可 — 次のステップから効く)。</summary>
    public Vector3 Gravity
    {
        get => PoseCallbacks.Gravity;
        set => PoseCallbacks.Gravity = value;
    }

    /// <summary>接触の反発上限 (m/s) — Bepu v2 の「跳ね」の実体 (古典 restitution は無い)。</summary>
    public float Bounciness
    {
        get => NarrowCallbacks.MaximumRecoveryVelocity;
        set => NarrowCallbacks.MaximumRecoveryVelocity = value;
    }

    /// <summary>今フレームの接触イベント (Begin/End)。フレーム内で読み切る規約 — 次の
    /// <see cref="Step"/>/<see cref="StepOnce"/> 冒頭でクリアされる。raw (collidable ハンドルベース)。</summary>
    public IReadOnlyList<ContactPairEvent> ContactEvents => _contactEvents;

    /// <summary>直近ステップで実接触しているペア集合 (gizmo/デバッグ用)。</summary>
    public IReadOnlyCollection<ContactPairKey> CurrentContacts => _contacts.Current;

    /// <summary>経過時間を accumulator へ積み、固定 <see cref="FixedDt"/> で進める (実行ステップ数を返す)。
    /// 巨大な elapsed は 0.25s に clamp — 停止からの復帰でスパイラルしない。</summary>
    public int Step(float elapsed)
    {
        _contactEvents.Clear();
        _accumulator += MathF.Min(MathF.Max(0, elapsed), 0.25f);
        int steps = 0;
        while (_accumulator >= FixedDt)
        {
            RunTimestep(FixedDt);
            _accumulator -= FixedDt;
            steps++;
        }
        return steps;
    }

    /// <summary>固定 dt で 1 ステップだけ進める (テスト/手動駆動用)。</summary>
    public void StepOnce()
    {
        _contactEvents.Clear();
        RunTimestep(FixedDt);
    }

    /// <summary>1 Timestep = 接触集合をクリア → 前進 → 前ステップとの差分を Begin/End イベントへ。</summary>
    private void RunTimestep(float dt)
    {
        _contacts.BeginStep();
        Simulation.Timestep(dt, _dispatcher);
        DiffContacts();
    }

    private void DiffContacts()
    {
        foreach (ContactPairKey k in _contacts.Current)
            if (!_prevPairs.Contains(k))
                _contactEvents.Add(new ContactPairEvent(k.A, k.B, ContactPhase.Begin));
        foreach (ContactPairKey k in _prevPairs)
            if (!_contacts.Current.Contains(k))
                _contactEvents.Add(new ContactPairEvent(k.A, k.B, ContactPhase.End));
        _prevPairs.Clear();
        foreach (ContactPairKey k in _contacts.Current) _prevPairs.Add(k);
    }

    /// <summary>shape を登録して bindless 的な TypedIndex を得る (同じ形は使い回してよい)。</summary>
    public TypedIndex AddShape<TShape>(in TShape shape) where TShape : unmanaged, IShape
        => Simulation.Shapes.Add(shape);

    /// <summary>動的ボディを追加する (慣性は shape × mass から計算)。
    /// <paramref name="continuous"/> = true で CCD (連続衝突検出) を有効化 — 高速な物体が薄い壁を
    /// すり抜けるトンネリングを防ぐ (掃引コストと引き換え)。既定 false は Bepu の Passive (discrete)。
    /// <para><paramref name="maxSpeculativeMargin"/> は投機的接触の生成距離上限。既定 (無制限) では
    /// Bepu は速度に応じた投機マージンで大抵のトンネリングを discrete でも防ぐ — CCD が本当に効くのは
    /// この値を絞った (薄い壁 + 極端な速度) 場合や回転を伴う掃引。</para></summary>
    public BodyHandle AddDynamic<TShape>(in RigidPose pose, in TShape shape, float mass = 1f,
        in BodyVelocity velocity = default, float sleepThreshold = 0.01f, bool continuous = false,
        float maxSpeculativeMargin = float.MaxValue)
        where TShape : unmanaged, IConvexShape
    {
        BodyInertia inertia = shape.ComputeInertia(mass);
        var collidable = new CollidableDescription(
            AddShape(shape), 0f, maxSpeculativeMargin,
            continuous ? ContinuousDetection.Continuous() : ContinuousDetection.Passive);
        return Simulation.Bodies.Add(BodyDescription.CreateDynamic(
            pose, velocity, inertia, collidable, new BodyActivityDescription(sleepThreshold)));
    }

    /// <summary>静的コライダーを追加する (床/壁)。</summary>
    public StaticHandle AddStatic(in RigidPose pose, TypedIndex shape)
        => Simulation.Statics.Add(new StaticDescription(pose, shape));

    /// <summary>トリガーボリュームを追加する — 物理応答なしで通過だけを検知する静的 collidable。
    /// 動的ボディがこの形状に触れると <see cref="ContactEvents"/> に Begin/End が出るが、力は働かない。</summary>
    public StaticHandle AddTrigger(in RigidPose pose, TypedIndex shape)
    {
        StaticHandle handle = Simulation.Statics.Add(new StaticDescription(pose, shape));
        _contacts.RegisterTriggerStatic(handle.Value);
        return handle;
    }

    /// <summary>三角形スープ (頂点 + インデックス) から静的メッシュ shape を登録する (地形/建物など)。
    /// Bepu <see cref="Mesh"/> は BufferPool のメモリを保持するが、<see cref="Simulation"/> の Shapes 経由で
    /// 追加するため <see cref="Dispose"/> 時に解放される。動的メッシュは非対応 (動的は <see cref="AddDynamicHull"/>)。</summary>
    public TypedIndex AddMeshShape(ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, Vector3 scale)
    {
        int triangleCount = indices.Length / 3;
        BufferPool.Take<Triangle>(triangleCount, out Buffer<Triangle> triangles);
        for (int i = 0; i < triangleCount; i++)
            triangles[i] = new Triangle(
                vertices[indices[i * 3 + 0]], vertices[indices[i * 3 + 1]], vertices[indices[i * 3 + 2]]);
        var mesh = new Mesh(triangles, scale, BufferPool);
        return Simulation.Shapes.Add(mesh);
    }

    /// <summary>静的メッシュコライダーを追加する (三角形スープ直渡し)。</summary>
    public StaticHandle AddStaticMesh(in RigidPose pose, ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, Vector3 scale)
        => AddStatic(pose, AddMeshShape(vertices, indices, scale));

    /// <summary>頂点群から凸包 (<see cref="ConvexHull"/>) の動的ボディを追加する。
    /// Bepu は形状を重心原点へ recenter するため <paramref name="center"/> (入力座標系での重心) を返す —
    /// <paramref name="originPose"/> は「入力頂点の原点」を指す pose として渡し、内部で重心ぶん平行移動して発行する
    /// (描画メッシュとの位置合わせは <c>GetPose - Rotate(center)</c> で元の原点に戻す)。</summary>
    public BodyHandle AddDynamicHull(in RigidPose originPose, Span<Vector3> points, float mass,
        out Vector3 center, in BodyVelocity velocity = default, float sleepThreshold = 0.01f, bool continuous = false)
    {
        ConvexHullHelper.CreateShape(points, BufferPool, out center, out ConvexHull hull);
        var bodyPose = originPose;
        bodyPose.Position += Vector3.Transform(center, originPose.Orientation);
        BodyInertia inertia = hull.ComputeInertia(mass);
        var collidable = new CollidableDescription(
            Simulation.Shapes.Add(hull),
            continuous ? ContinuousDetection.Continuous() : ContinuousDetection.Passive);
        return Simulation.Bodies.Add(BodyDescription.CreateDynamic(
            bodyPose, velocity, inertia, collidable, new BodyActivityDescription(sleepThreshold)));
    }

    /// <summary>キネマティックボディを追加する (スクリプト駆動で動く的/プラットフォーム)。無限質量で
    /// 外力に動じず、<see cref="SetBodyPose"/> で毎ステップ姿勢を与える。動的ボディの CCD は静的同様に確実に当たる。</summary>
    public BodyHandle AddKinematic(in RigidPose pose, TypedIndex shape)
        => Simulation.Bodies.Add(BodyDescription.CreateKinematic(pose, shape, new BodyActivityDescription(0.01f)));

    /// <summary>ボディの姿勢を直接設定して起こす (キネマティックの scripted 移動用)。</summary>
    public void SetBodyPose(BodyHandle handle, in RigidPose pose)
    {
        BodyReference body = Simulation.Bodies[handle];
        body.Pose = pose;
        body.Awake = true;
    }

    /// <summary>動的ボディを削除する (despawn / kill plane 用)。ECS 側の entity 削除と対で呼ぶ —
    /// entity だけ消して body を残すと「見えない衝突体」がリークする。</summary>
    public void RemoveBody(BodyHandle handle) => Simulation.Bodies.Remove(handle);

    /// <summary>静的コライダーを削除する。</summary>
    public void RemoveStatic(StaticHandle handle) => Simulation.Statics.Remove(handle);

    /// <summary>ボディの現在 pose (位置 + 回転)。</summary>
    public RigidPose GetPose(BodyHandle handle) => Simulation.Bodies[handle].Pose;

    /// <summary>ボディが起きているか (静止すると sleep して false)。</summary>
    public bool IsAwake(BodyHandle handle) => Simulation.Bodies[handle].Awake;

    /// <summary>最近傍レイキャスト。ヒットが無ければ false。</summary>
    public bool RayCast(Vector3 origin, Vector3 direction, float maximumT, out PhysicsRayHit hit)
    {
        var handler = new ClosestHitHandler { T = float.MaxValue };
        Simulation.RayCast(origin, direction, maximumT, ref handler);
        if (handler.T < float.MaxValue)
        {
            hit = new PhysicsRayHit(handler.T, handler.Normal,
                handler.Collidable.Mobility == CollidableMobility.Static,
                handler.Collidable.Mobility == CollidableMobility.Static
                    ? handler.Collidable.StaticHandle.Value
                    : handler.Collidable.BodyHandle.Value);
            return true;
        }
        hit = default;
        return false;
    }

    private struct ClosestHitHandler : IRayHitHandler
    {
        public float T;
        public Vector3 Normal;
        public CollidableReference Collidable;

        public readonly bool AllowTest(CollidableReference collidable) => true;
        public readonly bool AllowTest(CollidableReference collidable, int childIndex) => true;

        public void OnRayHit(in RayData ray, ref float maximumT, float t, in Vector3 normal,
            CollidableReference collidable, int childIndex)
        {
            if (t >= T) return;
            T = t;
            Normal = normal;
            Collidable = collidable;
            maximumT = t;   // 以遠は枝刈り
        }
    }

    public void Dispose()
    {
        Simulation.Dispose();
        BufferPool.Clear();
        _dispatcher?.Dispose();
    }
}
