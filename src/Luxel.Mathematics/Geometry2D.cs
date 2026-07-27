using System.Numerics;

namespace Luxel.Mathematics;

/// <summary>ドメイン非依存の2D幾何計算。</summary>
public static class Geometry2D
{
    /// <summary>点<paramref name="point"/>と線分<paramref name="start"/>..<paramref name="end"/>の最短距離。</summary>
    public static float DistancePointToSegment(Vector2 point, Vector2 start, Vector2 end,
        float degenerateLengthSquared = 1e-9f)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared < degenerateLengthSquared) return Vector2.Distance(point, start);
        float t = Math.Clamp(Vector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
        return Vector2.Distance(point, start + segment * t);
    }
}
