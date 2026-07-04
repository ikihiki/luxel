using System.Numerics;

namespace Luxel.Assets;

/// <summary>軸平行境界ボックス (Axis-Aligned Bounding Box)。カリング/frustum 判定用。</summary>
public struct AssetAabb
{
    public Vector3 Min;
    public Vector3 Max;

    public AssetAabb(Vector3 min, Vector3 max) { Min = min; Max = max; }

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Extent => (Max - Min) * 0.5f;
    public Vector3 Size => Max - Min;

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
