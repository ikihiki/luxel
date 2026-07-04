namespace Luxel.Animation;

/// <summary>ease-out cubic (1 - (1-t)^3)。UI トランジションの標準 (減速して着地)。
/// CSS の cubic-bezier 近似ではなく多項式そのもの — 旧 Luxel.UI Easing.OutCubic と同式 (AS-M1 移設)。</summary>
public sealed class OutCubicCurve : ICurve
{
    public static readonly OutCubicCurve Instance = new();
    public float Eval(float t01)
    {
        float t = 1f - Math.Clamp(t01, 0f, 1f);
        return 1f - t * t * t;
    }
}

/// <summary>ease-in-out cubic。旧 Luxel.UI Easing.InOutCubic と同式 (AS-M1 移設)。</summary>
public sealed class InOutCubicCurve : ICurve
{
    public static readonly InOutCubicCurve Instance = new();
    public float Eval(float t01)
    {
        float t = Math.Clamp(t01, 0f, 1f);
        return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
    }
}
