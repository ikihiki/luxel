namespace Luxel.Animation;

/// <summary>線形 easing (identity 関数)。デフォルト curve。</summary>
public sealed class LinearCurve : ICurve
{
    public static readonly LinearCurve Instance = new();
    public float Eval(float t01) => t01;
}
