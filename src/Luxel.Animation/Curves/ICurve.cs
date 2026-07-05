namespace Luxel.Animation;

/// <summary>
/// Easing 関数。正規化時刻 t ∈ [0, 1] を progress ∈ [0, 1] に写す。
/// Linear なら identity、CubicBezier なら CSS の cubic-bezier(x1,y1,x2,y2)、Step なら CSS の steps()、
/// Spring なら stiffness/damping/mass の物理ベース。
/// 純粋関数 (stateless) であること。
/// </summary>
public interface ICurve
{
    /// <summary>正規化時刻 → progress。t は通常 [0, 1] にクランプして渡されるが、超過時の挙動は実装に依存。</summary>
    float Eval(float t01);
}
