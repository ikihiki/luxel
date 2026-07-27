using System.Numerics;

namespace Luxel.Graphics.TwoD;

/// <summary>2x3 アフィン変換。screen = (A*x + C*y + E, B*x + D*y + F)。</summary>
public struct Affine2D
{
    public float A, B, C, D, E, F;

    public static Affine2D Identity => new() { A = 1, B = 0, C = 0, D = 1, E = 0, F = 0 };

    public static Affine2D Translate(float x, float y) => new() { A = 1, D = 1, E = x, F = y };
    public static Affine2D Scale(float sx, float sy) => new() { A = sx, D = sy };

    public static Affine2D Rotate(float radians)
    {
        float cs = MathF.Cos(radians), sn = MathF.Sin(radians);
        return new Affine2D { A = cs, B = sn, C = -sn, D = cs };
    }

    /// <summary>合成: 返り値を点に適用すると m1(m2(p)) と同じ (m2 を先に適用)。world = Mul(parent, local)。</summary>
    public static Affine2D Mul(in Affine2D m1, in Affine2D m2) => new()
    {
        A = m1.A * m2.A + m1.C * m2.B,
        B = m1.B * m2.A + m1.D * m2.B,
        C = m1.A * m2.C + m1.C * m2.D,
        D = m1.B * m2.C + m1.D * m2.D,
        E = m1.A * m2.E + m1.C * m2.F + m1.E,
        F = m1.B * m2.E + m1.D * m2.F + m1.F,
    };

    public readonly Vector2 Apply(Vector2 p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

    /// <summary>逆変換を求める (ヒットテストでワールド→ローカルへ写す用)。特異 (det≈0) なら false。</summary>
    public readonly bool TryInvert(out Affine2D inv)
    {
        float det = A * D - C * B;
        if (MathF.Abs(det) < 1e-12f) { inv = Identity; return false; }
        float id = 1f / det;
        inv = new Affine2D
        {
            A = D * id,
            B = -B * id,
            C = -C * id,
            D = A * id,
            E = (C * F - D * E) * id,
            F = (B * E - A * F) * id,
        };
        return true;
    }

    internal readonly GpuTransform ToGpu() => new() { A = A, B = B, C = C, D = D, E = E, F = F };
}
