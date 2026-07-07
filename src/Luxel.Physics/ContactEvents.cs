using BepuPhysics.Collidables;

namespace Luxel.Physics;

/// <summary>接触の遷移フェーズ。</summary>
public enum ContactPhase
{
    /// <summary>今ステップで新たに接触した (前ステップは非接触)。</summary>
    Begin,
    /// <summary>今ステップで接触が解けた (前ステップは接触)。</summary>
    End,
}

/// <summary>接触ペアのキー — 2 つの collidable を順序正規化して保持 (A↔B の入れ替えで同一)。</summary>
public readonly record struct ContactPairKey
{
    /// <summary>Packed の小さい方。</summary>
    public CollidableReference A { get; }
    /// <summary>Packed の大きい方。</summary>
    public CollidableReference B { get; }

    public ContactPairKey(CollidableReference a, CollidableReference b)
    {
        if (a.Packed <= b.Packed) { A = a; B = b; }
        else { A = b; B = a; }
    }
}

/// <summary>接触イベント (raw・collidable ハンドルベース)。ECS 層 (<see cref="PhysicsStepSystem"/>) が
/// <c>ContactEvent</c> (Entity ベース) へ変換する。</summary>
public readonly record struct ContactPairEvent(CollidableReference A, CollidableReference B, ContactPhase Phase);

/// <summary>
/// narrow-phase callbacks が「今ステップで実接触したペア」を積む共有コレクタ。
/// <b>class</b> なので <see cref="LuxelNarrowPhaseCallbacks"/> (struct) が Simulation 内へコピーされても
/// 同じインスタンスを共有する。<see cref="PhysicsWorld"/> が Timestep 前後で <see cref="BeginStep"/>/差分計算を回す。
///
/// <para><b>スレッド</b>: 単スレッド (ThreadCount=0) 前提。ThreadCount &gt; 0 では <see cref="Record"/> が
/// 並行呼び出しになり HashSet が壊れる (マルチスレッド接触イベントは v1 未対応)。</para>
/// </summary>
internal sealed class PhysicsContacts
{
    /// <summary>今ステップで実接触したペア集合 (Timestep 中に callbacks が埋める)。</summary>
    public readonly HashSet<ContactPairKey> Current = new();
    private readonly HashSet<int> _triggerStatics = new();

    /// <summary>ステップ開始時に接触集合をクリア。</summary>
    public void BeginStep() => Current.Clear();

    /// <summary>接触ペアを記録。</summary>
    public void Record(CollidableReference a, CollidableReference b) => Current.Add(new ContactPairKey(a, b));

    /// <summary>この static ハンドルをトリガー (物理応答なし) として登録。</summary>
    public void RegisterTriggerStatic(int staticHandleValue) => _triggerStatics.Add(staticHandleValue);

    /// <summary>collidable がトリガーか (トリガーは static のみ)。</summary>
    public bool IsTrigger(CollidableReference c)
        => c.Mobility == CollidableMobility.Static && _triggerStatics.Contains(c.StaticHandle.Value);
}
