using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

namespace Luxel.Physics;

// Bepu v2 の Simulation.Create に必須の callbacks boilerplate。PhysicsWorld が隠蔽する —
// 利用側はこのファイルを知らなくてよい。Luxel.Ecs には依存しない (将来の分割余地)。

/// <summary>接触の可否とマテリアル (摩擦/反発ばね) を返す narrow phase callbacks。
/// フィールドは実行時に <see cref="PhysicsWorld.Bounciness"/> 等から書き換えられる
/// (Simulation 内へコピーされた実体を <c>NarrowPhase&lt;T&gt;.Callbacks</c> 経由で触る)。</summary>
internal struct LuxelNarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    public float FrictionCoefficient;
    public float MaximumRecoveryVelocity;
    public SpringSettings ContactSpringiness;

    public void Initialize(Simulation simulation) { }

    public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b, ref float speculativeMargin)
        => a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic;

    public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB) => true;

    public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold,
        out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial = new PairMaterialProperties(FrictionCoefficient, MaximumRecoveryVelocity, ContactSpringiness);
        return true;
    }

    public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB,
        ref ConvexContactManifold manifold) => true;

    public void Dispose() { }
}

/// <summary>重力 + 減衰の pose integrator callbacks (SIMD wide — Bepu 2.4 のシグネチャ)。</summary>
internal struct LuxelPoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    public Vector3 Gravity;
    public float LinearDamping;
    public float AngularDamping;

    private Vector3Wide _gravityWideDt;
    private Vector<float> _linearDampingDt;
    private Vector<float> _angularDampingDt;

    public readonly AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    public readonly bool AllowSubstepsForUnconstrainedBodies => false;
    public readonly bool IntegrateVelocityForKinematics => false;

    public void Initialize(Simulation simulation) { }

    public void PrepareForIntegration(float dt)
    {
        // dt はステップ内で一定 — 重力×dt と damping^dt を先に broadcast しておく
        _gravityWideDt = Vector3Wide.Broadcast(Gravity * dt);
        _linearDampingDt = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - LinearDamping, 0, 1), dt));
        _angularDampingDt = new Vector<float>(MathF.Pow(MathHelper.Clamp(1 - AngularDamping, 0, 1), dt));
    }

    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation,
        BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, Vector<float> dt,
        ref BodyVelocityWide velocity)
    {
        velocity.Linear = (velocity.Linear + _gravityWideDt) * _linearDampingDt;
        velocity.Angular *= _angularDampingDt;
    }
}
