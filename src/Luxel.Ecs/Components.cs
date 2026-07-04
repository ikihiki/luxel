using System.Numerics;
using Friflo.Engine.ECS;

namespace Luxel.Ecs;

/// <summary>ローカル座標系での 4x4 変換行列。</summary>
public struct LocalTransform : IComponent
{
    public Matrix4x4 Matrix;
    public LocalTransform(Matrix4x4 m) { Matrix = m; }
}

/// <summary>ワールド座標系での 4x4 変換行列 (システムが伝搬で計算)。</summary>
public struct GlobalTransform : IComponent
{
    public Matrix4x4 Matrix;
    public GlobalTransform(Matrix4x4 m) { Matrix = m; }
}

/// <summary>親 entity への参照 (Hierarchy)。</summary>
public struct Parent : IComponent
{
    public Entity ParentEntity;
    public Parent(Entity p) { ParentEntity = p; }
}

/// <summary>色 (RGBA)。</summary>
public struct Color3D : IComponent
{
    public Vector4 Rgba;
    public Color3D(Vector4 rgba) { Rgba = rgba; }
}

/// <summary>メッシュ参照 (bindless 配列のインデックス)。</summary>
public struct MeshRef : IComponent
{
    public int Index;
    public MeshRef(int idx) { Index = idx; }
    public const int Cube = 0;
}

/// <summary>表示/非表示。</summary>
public struct Visible : IComponent
{
    public bool On;
    public Visible(bool on) { On = on; }
}
