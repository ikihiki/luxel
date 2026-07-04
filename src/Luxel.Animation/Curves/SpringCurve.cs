namespace Luxel.Animation;

/// <summary>
/// バネ物理ベースの easing。t は本来「秒」だが、Curve インタフェースに合わせて t01 を「duration × t01 秒」と解釈する。
/// このため Duration は外部 (Animatable) が決め、Curve は「t01 を progress に写す」ことに集中する。
/// 解析解 (damped harmonic oscillator) を用いる。
///   x''(t) = -k·x(t) - c·x'(t)   (k=stiffness/mass, c=damping/mass)
///   初期 x(0)=0, x'(0)=0、定常 x(∞)=1 となるよう b=1 へ正規化、回答 = 1 - homogeneous solution。
/// </summary>
public sealed class SpringCurve : ICurve
{
    public float Stiffness { get; }
    public float Damping { get; }
    public float Mass { get; }
    public float DurationSec { get; }

    public SpringCurve(float stiffness = 170f, float damping = 26f, float mass = 1f, float durationSec = 1f)
    {
        Stiffness = stiffness;
        Damping = damping;
        Mass = mass;
        DurationSec = durationSec;
    }

    public float Eval(float t01)
    {
        if (t01 <= 0f) return 0f;
        float t = t01 * DurationSec;
        float w0 = MathF.Sqrt(Stiffness / Mass);              // 自然角振動数
        float zeta = Damping / (2f * MathF.Sqrt(Stiffness * Mass));   // 減衰比
        float homogeneous;
        if (zeta < 1f)
        {
            // Underdamped
            float wd = w0 * MathF.Sqrt(1f - zeta * zeta);
            homogeneous = MathF.Exp(-zeta * w0 * t) * (MathF.Cos(wd * t) + (zeta * w0 / wd) * MathF.Sin(wd * t));
        }
        else if (zeta == 1f)
        {
            // Critically damped
            homogeneous = MathF.Exp(-w0 * t) * (1f + w0 * t);
        }
        else
        {
            // Overdamped
            float wd = w0 * MathF.Sqrt(zeta * zeta - 1f);
            homogeneous = MathF.Exp(-zeta * w0 * t) * (MathF.Cosh(wd * t) + (zeta * w0 / wd) * MathF.Sinh(wd * t));
        }
        return 1f - homogeneous;
    }
}
