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
