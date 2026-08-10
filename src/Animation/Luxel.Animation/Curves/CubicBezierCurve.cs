namespace Luxel.Animation;

/// <summary>
/// CSS cubic-bezier(x1, y1, x2, y2) と同じ挙動。
/// 制御点 P0=(0,0), P1=(x1,y1), P2=(x2,y2), P3=(1,1) の 3 次 Bezier 曲線。
/// 与えられた t01 を x として、対応する y を返す (ニュートン法で逆引き)。
/// </summary>
public sealed class CubicBezierCurve : ICurve
{
    public float X1 { get; }
    public float Y1 { get; }
    public float X2 { get; }
    public float Y2 { get; }

    public CubicBezierCurve(float x1, float y1, float x2, float y2)
    {
        X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
    }

    // CSS の標準的 easing 名
    public static CubicBezierCurve Ease => new(0.25f, 0.1f, 0.25f, 1.0f);
    public static CubicBezierCurve EaseIn => new(0.42f, 0.0f, 1.0f, 1.0f);
    public static CubicBezierCurve EaseOut => new(0.0f, 0.0f, 0.58f, 1.0f);
    public static CubicBezierCurve EaseInOut => new(0.42f, 0.0f, 0.58f, 1.0f);

    public float Eval(float t01)
    {
        if (t01 <= 0f) return 0f;
        if (t01 >= 1f) return 1f;
        // x(t) = 3(1-t)² t·X1 + 3(1-t) t² X2 + t³  と  y(t) を同様に。
        // x(t) = t01 となる t をニュートン法で求める。
        float t = SolveTForX(t01);
        return BezierY(t);
    }

    private float BezierX(float t)
    {
        float u = 1f - t;
        return 3f * u * u * t * X1 + 3f * u * t * t * X2 + t * t * t;
    }

    private float BezierY(float t)
    {
        float u = 1f - t;
        return 3f * u * u * t * Y1 + 3f * u * t * t * Y2 + t * t * t;
    }

    private float BezierDxDt(float t)
    {
        float u = 1f - t;
        return 3f * u * u * X1 + 6f * u * t * (X2 - X1) + 3f * t * t * (1f - X2);
    }

    private float SolveTForX(float x)
    {
        // ニュートン法 (高速)、収束しないときは二分法へフォールバック
        float t = x;
        for (int i = 0; i < 8; i++)
        {
            float xt = BezierX(t) - x;
            if (MathF.Abs(xt) < 1e-5f) return t;
            float dx = BezierDxDt(t);
            if (MathF.Abs(dx) < 1e-6f) break;
            t -= xt / dx;
        }
        // 二分法
        float lo = 0f, hi = 1f;
        t = x;
        for (int i = 0; i < 24; i++)
        {
            float xt = BezierX(t);
            if (MathF.Abs(xt - x) < 1e-5f) return t;
            if (xt < x) lo = t; else hi = t;
            t = 0.5f * (lo + hi);
        }
        return t;
    }
}
