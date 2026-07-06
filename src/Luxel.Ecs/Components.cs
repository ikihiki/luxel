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

/// <summary>
/// デバッグ表示名。DevTools の ECS 一覧で Id の代わりに人間可読な名前を出すための純データ。
/// ゲームが敵/弾/的などに命名する用途。無ければ DevTools は Id を表示する。
/// <para>セーブ対象外の意図 (実行時の観測専用。15 のセーブ機構が入ったら <c>[SaveIgnore]</c> の適用例第 1 号になる)。</para>
/// </summary>
public struct DebugName : IComponent
{
    /// <summary>表示名。</summary>
    public string Name;
    /// <summary>名前を指定して生成。</summary>
    public DebugName(string name) { Name = name; }
}

/// <summary>表示/非表示。</summary>
public struct Visible : IComponent
{
    /// <summary>true なら表示。</summary>
    public bool On;
    /// <summary>表示状態を指定して生成。</summary>
    public Visible(bool on) { On = on; }
}

/// <summary>
/// 固定タイムステップ (FixedUpdate) のシミュレーション結果を描画時に補間するための前/現ステップ TRS。
/// FixedUpdate 内で <see cref="Push"/> して「前ステップ位置」と「現ステップ位置」を持ち、
/// Render 前に <see cref="Luxel.Ecs.TransformInterpolationSystem"/> が <c>Alpha</c> で lerp/slerp して
/// <see cref="LocalTransform"/> に書き込む。これをやると 60Hz 表示 + 1/60 固定でもガタつかない。
/// </summary>
public struct InterpolatedTransform : IComponent
{
    /// <summary>前ステップの位置。</summary>
    public Vector3 PrevPosition;
    /// <summary>前ステップの回転。</summary>
    public Quaternion PrevRotation;
    /// <summary>前ステップのスケール。</summary>
    public Vector3 PrevScale;
    /// <summary>現ステップの位置。</summary>
    public Vector3 CurrPosition;
    /// <summary>現ステップの回転。</summary>
    public Quaternion CurrRotation;
    /// <summary>現ステップのスケール。</summary>
    public Vector3 CurrScale;

    /// <summary>前後を同じ TRS で初期化する (テレポート時に補間の飛びを消す)。</summary>
    public InterpolatedTransform(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        PrevPosition = CurrPosition = position;
        PrevRotation = CurrRotation = rotation;
        PrevScale = CurrScale = scale;
    }

    /// <summary>現ステップを前ステップへ送り、新しい TRS を現ステップに入れる (各 FixedUpdate で 1 回)。</summary>
    public void Push(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        PrevPosition = CurrPosition; PrevRotation = CurrRotation; PrevScale = CurrScale;
        CurrPosition = position; CurrRotation = rotation; CurrScale = scale;
    }

    /// <summary>前後を同じ TRS に揃える (瞬間移動 — 補間しない)。</summary>
    public void Teleport(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        PrevPosition = CurrPosition = position;
        PrevRotation = CurrRotation = rotation;
        PrevScale = CurrScale = scale;
    }

    /// <summary>alpha [0,1] で前→現を補間した行列 (Scale × Rotation × Translation)。</summary>
    public readonly Matrix4x4 Sample(float alpha)
    {
        Vector3 pos = Vector3.Lerp(PrevPosition, CurrPosition, alpha);
        Quaternion rot = Quaternion.Slerp(PrevRotation, CurrRotation, alpha);
        Vector3 scale = Vector3.Lerp(PrevScale, CurrScale, alpha);
        return Matrix4x4.CreateScale(scale)
             * Matrix4x4.CreateFromQuaternion(rot)
             * Matrix4x4.CreateTranslation(pos);
    }
}
