using System.Numerics;

namespace Luxel.Assets;

/// <summary>軸平行境界ボックス (Axis-Aligned Bounding Box)。カリング/frustum 判定用。</summary>
public struct AssetAabb
{
    /// <summary>最小コーナー。</summary>
    public Vector3 Min;
    /// <summary>最大コーナー。</summary>
    public Vector3 Max;

    /// <summary>Min/Max を指定して作成。</summary>
    public AssetAabb(Vector3 min, Vector3 max) { Min = min; Max = max; }

    /// <summary>中心座標。</summary>
    public Vector3 Center => (Min + Max) * 0.5f;
    /// <summary>中心から各面までの半径 (Size の半分)。</summary>
    public Vector3 Extent => (Max - Min) * 0.5f;
    /// <summary>各軸の全長 (Max - Min)。</summary>
    public Vector3 Size => Max - Min;

    /// <summary>点群を包む AABB を計算 (空なら zero AABB)。</summary>
    public static AssetAabb FromPoints(ReadOnlySpan<Vector3> points)
    {
        if (points.Length == 0) return new AssetAabb(Vector3.Zero, Vector3.Zero);
        Vector3 min = points[0], max = points[0];
        for (int i = 1; i < points.Length; i++)
        {
            min = Vector3.Min(min, points[i]);
            max = Vector3.Max(max, points[i]);
        }
        return new AssetAabb(min, max);
    }
}
