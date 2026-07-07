using System.Numerics;
using BepuPhysics;
using Friflo.Engine.ECS;

namespace Luxel.Physics;

/// <summary>コライダー形状の種別。</summary>
public enum ColliderKind
{
    /// <summary>箱 (Size = フルサイズ。CubeMesh が単位キューブなので見た目スケール = Size)。</summary>
    Box,
    /// <summary>球 (Radius)。</summary>
    Sphere,
    /// <summary>カプセル (Radius + 円筒部 Length、Y 軸)。</summary>
    Capsule,
}

/// <summary>衝突形状。<see cref="RigidBody"/> (動的) か <see cref="StaticBody"/> (静的) と組で付ける。</summary>
public struct Collider : IComponent
{
    public ColliderKind Kind;
    /// <summary>Box 用フルサイズ。</summary>
    public Vector3 Size;
    /// <summary>Sphere/Capsule 用半径。</summary>
    public float Radius;
    /// <summary>Capsule 用円筒部の長さ。</summary>
    public float Length;

    public static Collider Box(Vector3 size) => new() { Kind = ColliderKind.Box, Size = size };
    public static Collider Box(float w, float h, float l) => Box(new Vector3(w, h, l));
    public static Collider Sphere(float radius) => new() { Kind = ColliderKind.Sphere, Radius = radius };
    public static Collider Capsule(float radius, float length)
        => new() { Kind = ColliderKind.Capsule, Radius = radius, Length = length };

    /// <summary>描画用スケール (MeshRef.Cube を近似表示する v1 の規約 —
    /// Box は Size、Sphere は外接 2r、Capsule は (2r, len+2r, 2r))。</summary>
    public readonly Vector3 RenderScale => Kind switch
    {
        ColliderKind.Sphere => new Vector3(Radius * 2),
        ColliderKind.Capsule => new Vector3(Radius * 2, Length + Radius * 2, Radius * 2),
        _ => Size,
    };
}

/// <summary>動的剛体。<see cref="PhysicsStepSystem"/> が body を発行し、毎ステップ pose を
/// <c>LocalTransform</c> へ書き戻す。初期位置/回転は entity の LocalTransform から採る。</summary>
public struct RigidBody : IComponent
{
    /// <summary>質量 (kg 相当)。0 以下は 1 とみなす。</summary>
    public float Mass;
    /// <summary>Attach 時に与える初速。</summary>
    public Vector3 InitialVelocity;
    /// <summary>CCD (連続衝突検出) を有効にするか。高速な物体が薄い壁をすり抜ける
    /// トンネリングを防ぐ (弾/投擲物向け)。既定 false = discrete。Attach 時に一度だけ反映。</summary>
    public bool Continuous;
    /// <summary>発行済み body ハンドル (システムが書く)。</summary>
    public BodyHandle Handle;
    /// <summary>body 発行済みか (BodyHandle は値 0 が有効なため別フラグ)。</summary>
    public bool Attached;

    public static RigidBody Dynamic(float mass = 1f, Vector3 initialVelocity = default, bool ccd = false)
        => new() { Mass = mass, InitialVelocity = initialVelocity, Continuous = ccd };
}

/// <summary>静的コライダー (床/壁)。pose は Attach 時の LocalTransform で固定される。</summary>
public struct StaticBody : IComponent
{
    /// <summary>発行済み static ハンドル (システムが書く)。</summary>
    public StaticHandle Handle;
    /// <summary>発行済みか。</summary>
    public bool Attached;
}

/// <summary>静的メッシュコライダー — 三角形スープ (頂点 + インデックス) で地形/建物と衝突する静的 collidable。
/// <see cref="Collider"/> (凸プリミティブ) の代わりに付ける。pose は Attach 時の <see cref="LocalTransform"/>。
/// 動的メッシュは Bepu で非推奨のため非対応 (動的な実形状は <see cref="HullCollider"/> = 凸包を使う)。</summary>
public struct MeshCollider : IComponent
{
    /// <summary>頂点配列。</summary>
    public Vector3[] Vertices;
    /// <summary>三角形インデックス (3 個で 1 三角形)。</summary>
    public int[] Indices;
    /// <summary>形状スケール。</summary>
    public Vector3 Scale;
    /// <summary>発行済み static ハンドル (システムが書く)。</summary>
    public StaticHandle Handle;
    /// <summary>発行済みか。</summary>
    public bool Attached;

    public static MeshCollider Static(Vector3[] vertices, int[] indices, Vector3? scale = null)
        => new() { Vertices = vertices, Indices = indices, Scale = scale ?? Vector3.One };
}

/// <summary>動的な凸包コライダー — 頂点群から <c>ConvexHull</c> を作った動的剛体 (実アセットの小物など)。
/// <see cref="Collider"/> + <see cref="RigidBody"/> の代わりに単独で付ける。Bepu は形状を重心原点へ recenter
/// するため、システムが重心オフセット <see cref="Center"/> を書き、書き戻しで元の頂点原点に合わせる。
/// 凹メッシュの凸分解は v1 スコープ外 (凸包のみ)。</summary>
public struct HullCollider : IComponent
{
    /// <summary>凸包を張る頂点群 (入力座標系)。</summary>
    public Vector3[] Points;
    /// <summary>質量。0 以下は 1。</summary>
    public float Mass;
    /// <summary>Attach 時に与える初速。</summary>
    public Vector3 InitialVelocity;
    /// <summary>CCD を有効にするか。</summary>
    public bool Continuous;
    /// <summary>入力座標系での重心オフセット (システムが書く)。書き戻しの位置補正に使う。</summary>
    public Vector3 Center;
    /// <summary>発行済み body ハンドル (システムが書く)。</summary>
    public BodyHandle Handle;
    /// <summary>発行済みか。</summary>
    public bool Attached;

    public static HullCollider Dynamic(Vector3[] points, float mass = 1f, Vector3 initialVelocity = default, bool ccd = false)
        => new() { Points = points, Mass = mass, InitialVelocity = initialVelocity, Continuous = ccd };
}

/// <summary>トリガーボリューム — <see cref="Collider"/> の形状で「通過検知」だけを行う静的 collidable
/// (物理応答なし)。ゴール判定/アイテム取得/ダメージゾーンなどに。動的ボディが触れると
/// <see cref="PhysicsStepSystem.ContactEvents"/> に Begin/End が出る (相手側はゲームがコンポーネントで判別)。</summary>
public struct Trigger : IComponent
{
    /// <summary>発行済み static ハンドル (システムが書く)。</summary>
    public StaticHandle Handle;
    /// <summary>発行済みか。</summary>
    public bool Attached;
}

/// <summary>接触/トリガーイベント (Entity ベース)。<see cref="PhysicsStepSystem.ContactEvents"/> が公開。
/// フレーム内で読み切る規約 (持ち越さない)。トリガー判定はどちらかの Entity が <see cref="Trigger"/> を
/// 持つかで行う。</summary>
public readonly record struct ContactEvent(Entity A, Entity B, ContactPhase Phase);

/// <summary>接触中の Entity ペア (<see cref="PhysicsStepSystem.CurrentContacts"/>、gizmo/デバッグ用)。</summary>
public readonly record struct EntityPair(Entity A, Entity B);
