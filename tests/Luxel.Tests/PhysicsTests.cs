using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using Friflo.Engine.ECS;
using Luxel.Ecs;
using Luxel.Physics;
using Xunit;

namespace Luxel.Tests;

/// <summary>Luxel.Physics — PhysicsWorld (決定性/accumulator/レイキャスト) と ECS ブリッジ。</summary>
public class PhysicsTests
{
    [Fact]
    public void FreeFall_ApproximatesGravity()
    {
        using var physics = new PhysicsWorld();
        BodyHandle ball = physics.AddDynamic(new RigidPose(new Vector3(0, 0, 0)), new Sphere(0.5f));

        for (int i = 0; i < 60; i++) physics.StepOnce();   // 1 秒

        // 半陰積分 + damping なので厳密な -g t²/2 (-4.905) より深め/浅めに数 % ずれる
        float y = physics.GetPose(ball).Position.Y;
        Assert.InRange(y, -5.4f, -4.4f);
    }

    /// <summary>決定性のコア担保 — 同一セットアップの 2 world は 120 ステップ後も bit 一致。</summary>
    [Fact]
    public void Determinism_TwoWorldsBitIdentical()
    {
        static (PhysicsWorld physics, BodyHandle[] bodies) Build()
        {
            var p = new PhysicsWorld();
            p.AddStatic(new RigidPose(new Vector3(0, -0.5f, 0)), p.AddShape(new Box(16, 1, 16)));
            var handles = new BodyHandle[8];
            for (int i = 0; i < handles.Length; i++)
                handles[i] = p.AddDynamic(
                    new RigidPose(new Vector3(i % 3 * 0.9f - 0.9f, 1 + i * 1.1f, i / 3 * 0.7f - 0.7f),
                                  Quaternion.CreateFromYawPitchRoll(i * 0.4f, 0.2f, 0.1f)),
                    new Box(1, 1, 1));
            return (p, handles);
        }

        (PhysicsWorld a, BodyHandle[] ha) = Build();
        (PhysicsWorld b, BodyHandle[] hb) = Build();
        using (a)
        using (b)
        {
            for (int i = 0; i < 120; i++) { a.StepOnce(); b.StepOnce(); }
            for (int i = 0; i < ha.Length; i++)
            {
                RigidPose pa = a.GetPose(ha[i]), pb = b.GetPose(hb[i]);
                Assert.Equal(pa.Position, pb.Position);         // float 完全一致
                Assert.Equal(pa.Orientation, pb.Orientation);
            }
        }
    }

    [Fact]
    public void Stack_ComesToRestAndSleeps()
    {
        using var physics = new PhysicsWorld();
        physics.AddStatic(new RigidPose(new Vector3(0, -0.5f, 0)), physics.AddShape(new Box(16, 1, 16)));
        BodyHandle bottom = physics.AddDynamic(new RigidPose(new Vector3(0, 0.55f, 0)), new Box(1, 1, 1));
        BodyHandle top = physics.AddDynamic(new RigidPose(new Vector3(0, 1.7f, 0)), new Box(1, 1, 1));

        for (int i = 0; i < 600; i++) physics.StepOnce();   // 10 秒

        Assert.False(physics.IsAwake(bottom));   // 静止 → sleep
        Assert.False(physics.IsAwake(top));
        Assert.InRange(physics.GetPose(bottom).Position.Y, 0.35f, 0.65f);   // 床上 ~0.5
        Assert.InRange(physics.GetPose(top).Position.Y, 1.3f, 1.7f);        // 1 段上 ~1.5
    }

    [Fact]
    public void RayCast_HitsStaticBox()
    {
        using var physics = new PhysicsWorld();
        physics.AddStatic(RigidPose.Identity, physics.AddShape(new Box(2, 2, 2)));   // 原点、上面 y=+1

        Assert.True(physics.RayCast(new Vector3(0, 5, 0), new Vector3(0, -1, 0), 100, out PhysicsRayHit hit));
        Assert.Equal(4f, hit.T, 2);                 // 5 → 上面 y=1
        Assert.True(hit.Normal.Y > 0.99f);          // +Y 法線
        Assert.True(hit.IsStatic);

        Assert.False(physics.RayCast(new Vector3(10, 5, 0), new Vector3(0, -1, 0), 100, out _));   // 外し
    }

    [Fact]
    public void EcsBridge_AttachesAndWritesLocalTransform()
    {
        using var physics = new PhysicsWorld();
        var world = new Luxel.Ecs.World();
        var system = new PhysicsStepSystem(world, physics);

        // 床 + 落ちる箱 (見た目スケール 0.5 の箱)
        world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, -0.5f, 0)),
            Collider.Box(16, 1, 16),
            new StaticBody());
        var box = world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, 3, 0)),
            Collider.Box(0.5f, 0.5f, 0.5f),
            RigidBody.Dynamic());

        system.Run(1f / 60f);   // Attach + 1 ステップ
        Assert.True(box.GetComponent<RigidBody>().Attached);

        for (int i = 0; i < 30; i++) system.Run(1f / 60f);   // 0.5 秒落下

        Matrix4x4 m = box.GetComponent<LocalTransform>().Matrix;
        Assert.True(m.Translation.Y < 3f - 0.5f);   // 下降した
        Assert.True(Matrix4x4.Decompose(m, out Vector3 scale, out _, out _));
        Assert.Equal(0.5f, scale.X, 3);             // RenderScale = Collider.Size を保持
    }

    /// <summary>CCD: 薄い壁へ高速な球を撃ち込み、CCD なしはトンネリング / CCD ありは手前で止まる、を両方示す
    /// (「CCD なしで実際にすり抜ける」ことがテストの信頼性を担保する)。</summary>
    [Fact]
    public void Ccd_PreventsTunnelingThroughThinWall()
    {
        static float FireSphere(bool ccd)
        {
            // 重力を切って Z 軸のトンネリングだけを見る
            using var physics = new PhysicsWorld(new PhysicsSettings { Gravity = Vector3.Zero });
            physics.AddStatic(new RigidPose(new Vector3(0, 0, 0)),
                physics.AddShape(new Box(4, 4, 0.1f)));   // 壁: z ∈ [-0.05, 0.05]、厚さ 0.1
            BodyHandle ball = physics.AddDynamic(
                new RigidPose(new Vector3(0, 0, -2)), new Sphere(0.2f), mass: 1f,
                velocity: new BodyVelocity(new Vector3(0, 0, 150f)),   // 150 m/s → 1 ステップ 2.5m (壁 0.1m を飛び越える)
                continuous: ccd,
                maxSpeculativeMargin: 0.1f);   // 投機マージンを絞る → discrete では壁を捕捉できず CCD の掃引だけが止める
            for (int i = 0; i < 8; i++) physics.StepOnce();
            return physics.GetPose(ball).Position.Z;
        }

        Assert.True(FireSphere(ccd: false) > 1f);   // CCD なし → 壁をすり抜けて向こう側 (z > 0)
        Assert.True(FireSphere(ccd: true) < 0f);    // CCD あり → 壁の手前で停止 (z < 0)
    }

    /// <summary>接触イベント: 落下する箱が床に着くと Begin が 1 回、跳ね上げると End が出る (ECS 経由)。</summary>
    [Fact]
    public void ContactEvents_BeginOnLanding_EndOnSeparation()
    {
        using var physics = new PhysicsWorld();
        var world = new Luxel.Ecs.World();
        var system = new PhysicsStepSystem(world, physics);

        var floor = world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, -0.5f, 0)),
            Collider.Box(16, 1, 16), new StaticBody());
        var box = world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, 1.2f, 0)),   // 床上すぐ (すぐ着地)
            Collider.Box(1, 1, 1), RigidBody.Dynamic());

        // 着地まで進める → どこかのステップで Begin が 1 回出る
        int begins = 0;
        for (int i = 0; i < 120; i++)
        {
            system.Run(1f / 60f);
            foreach (ContactEvent e in system.ContactEvents)
                if (e.Phase == ContactPhase.Begin && Involves(e, box, floor)) begins++;
        }
        Assert.Equal(1, begins);   // 着地は 1 回だけ (静止後は再発火しない)

        // 跳ね上げる (上向き初速の新しい箱でも良いが、ここは分離を作る) → End が出る
        // 箱に上向き速度を与えるため body を直接叩く
        BodyHandle h = box.GetComponent<RigidBody>().Handle;
        BodyReference bodyRef = physics.Simulation.Bodies[h];
        bodyRef.Velocity.Linear = new Vector3(0, 8, 0);
        bodyRef.Awake = true;

        int ends = 0;
        for (int i = 0; i < 60; i++)
        {
            system.Run(1f / 60f);
            foreach (ContactEvent e in system.ContactEvents)
                if (e.Phase == ContactPhase.End && Involves(e, box, floor)) ends++;
        }
        Assert.True(ends >= 1);   // 離れたら End

        static bool Involves(ContactEvent e, Entity a, Entity b)
            => (e.A == a && e.B == b) || (e.A == b && e.B == a);
    }

    /// <summary>トリガー: 球がトリガーボリュームを通過すると Begin/End が出るが、速度は変わらない (物理応答なし)。</summary>
    [Fact]
    public void Trigger_DetectsPassage_WithoutPhysicalResponse()
    {
        // 重力 + 減衰オフ = 応答が無ければ完全な等速直進 (トリガーの「力なし」を厳密に検証)
        using var physics = new PhysicsWorld(new PhysicsSettings { Gravity = Vector3.Zero, LinearDamping = 0f });
        var world = new Luxel.Ecs.World();
        var system = new PhysicsStepSystem(world, physics);

        var gate = world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(0, 0, 0)),
            Collider.Box(2, 2, 2), new Trigger());
        var ball = world.CreateEntity(
            new LocalTransform(Matrix4x4.CreateTranslation(-4, 0, 0)),
            Collider.Sphere(0.4f), RigidBody.Dynamic(initialVelocity: new Vector3(4, 0, 0)));

        int enter = 0, exit = 0;
        for (int i = 0; i < 120; i++)   // 2 秒: -4 → +4 でゲートを通過
        {
            system.Run(1f / 60f);
            foreach (ContactEvent e in system.ContactEvents)
            {
                bool involvesGate = e.A == gate || e.B == gate;
                if (!involvesGate) continue;
                if (e.Phase == ContactPhase.Begin) enter++;
                else exit++;
            }
        }

        Assert.Equal(1, enter);   // 1 回入って
        Assert.Equal(1, exit);    // 1 回出る

        // 物理応答なし = 等速直進 (重力オフ)。x 速度が初速のまま、y/z はほぼ 0
        Vector3 v = physics.Simulation.Bodies[ball.GetComponent<RigidBody>().Handle].Velocity.Linear;
        Assert.InRange(v.X, 3.9f, 4.1f);         // 減速していない (壁なら止まる)
        Assert.InRange(v.Y, -0.01f, 0.01f);
        Assert.True(ball.GetComponent<LocalTransform>().Matrix.Translation.X > 3f);   // 通り抜けた
    }

    [Fact]
    public void Accumulator_FixedStepCount()
    {
        using var physics = new PhysicsWorld();   // FixedDt = 1/60
        float dt = physics.FixedDt;

        Assert.Equal(3, physics.Step(dt * 3));      // ちょうど 3 ステップ
        Assert.Equal(0, physics.Step(dt * 0.5f));   // 半端は繰り越し
        Assert.Equal(1, physics.Step(dt * 0.6f));   // 0.5 + 0.6 = 1.1 dt → 1 ステップ + 余り

        using var fresh = new PhysicsWorld();
        Assert.InRange(fresh.Step(10f), 14, 15);    // 巨大 elapsed は 0.25s に clamp — ≈0.25/dt (float 丸めで 14 か 15)
    }
}
