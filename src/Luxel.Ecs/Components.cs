using System.Numerics;
using Friflo.Engine.ECS;

namespace Luxel.Ecs;

/// <summary>ローカル座標系での 4x4 変換行列。</summary>
public struct LocalTransform : IComponent
{
    /// <summary>親空間に対するローカル変換行列。</summary>
    public Matrix4x4 Matrix;
    /// <summary>行列を指定して生成。</summary>
    public LocalTransform(Matrix4x4 m) { Matrix = m; }
}

/// <summary>ワールド座標系での 4x4 変換行列 (システムが伝搬で計算)。</summary>
public struct GlobalTransform : IComponent
{
    /// <summary>ワールド変換行列。</summary>
    public Matrix4x4 Matrix;
    /// <summary>行列を指定して生成。</summary>
    public GlobalTransform(Matrix4x4 m) { Matrix = m; }
}

/// <summary>親 entity への参照 (Hierarchy)。</summary>
public struct Parent : IComponent
{
    /// <summary>親の Entity。</summary>
    public Entity ParentEntity;
    /// <summary>親 entity を指定して生成。</summary>
    public Parent(Entity p) { ParentEntity = p; }
}

/// <summary>色 (RGBA)。</summary>
public struct Color3D : IComponent
{
    /// <summary>RGBA 各成分 (0..1)。</summary>
    public Vector4 Rgba;
    /// <summary>RGBA を指定して生成。</summary>
    public Color3D(Vector4 rgba) { Rgba = rgba; }
}

/// <summary>メッシュ参照 (bindless 配列のインデックス)。</summary>
public struct MeshRef : IComponent
{
    /// <summary>bindless メッシュ配列内のインデックス。</summary>
    public int Index;
    /// <summary>インデックスを指定して生成。</summary>
    public MeshRef(int idx) { Index = idx; }
    /// <summary>組込み Cube メッシュのインデックス。</summary>
    public const int Cube = 0;
}

/// <summary>表示/非表示。</summary>
public struct Visible : IComponent
{
    /// <summary>true なら表示。</summary>
    public bool On;
    /// <summary>表示状態を指定して生成。</summary>
    public Visible(bool on) { On = on; }
}
