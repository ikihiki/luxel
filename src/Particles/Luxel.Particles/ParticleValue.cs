using Luxel.Animation;

namespace Luxel.Particles;

/// <summary>パーティクルパラメータの形態。</summary>
public enum ParticleValueKind
{
    /// <summary>定数。</summary>
    Const,
    /// <summary>[A,B] の一様乱数 (放出時に 1 回サンプル)。</summary>
    Range,
    /// <summary>寿命 t∈[0,1] に沿って A→B を <see cref="ParticleValue.Curve"/> で補間 (アニメーション)。</summary>
    Curve,
}

/// <summary>
/// パーティクルパラメータの判別共用体 (Effekseer 風)。Const / Range / Curve を 1 型で表し、
/// Min/Max ペアの ad hoc 増殖を避ける。放出時に固定する量 (寿命/速度/初期サイズ) は
/// <see cref="Sample"/> でスカラー化し、寿命に沿って変える量 (サイズ/色カーブ) は <see cref="Eval"/> で評価する。
/// v1 の評価は Const/Range/線形〜曲線補間まで。カーブは <see cref="ICurve"/> (Luxel.Animation)。
/// <c>float</c> からの暗黙変換で定数を書ける。
/// </summary>
public readonly struct ParticleValue
{
    private ParticleValue(ParticleValueKind kind, float a, float b, ICurve? curve)
    {
        Kind = kind;
        A = a;
        B = b;
        Curve = curve;
    }

    public ParticleValueKind Kind { get; }
    /// <summary>Const の値 / Range・Curve の起点。</summary>
    public float A { get; }
    /// <summary>Range・Curve の終点 (Const では A と同じ)。</summary>
    public float B { get; }
    /// <summary>Curve 種のイージング (null は線形)。</summary>
    public ICurve? Curve { get; }

    public static ParticleValue Const(float v) => new(ParticleValueKind.Const, v, v, null);
    public static ParticleValue Range(float min, float max) => new(ParticleValueKind.Range, min, max, null);
    public static ParticleValue Curved(float from, float to, ICurve? curve = null) => new(ParticleValueKind.Curve, from, to, curve);

    public static implicit operator ParticleValue(float v) => Const(v);

    /// <summary>寿命に沿って変化するか (Curve 種のみ true)。</summary>
    public bool IsAnimated => Kind == ParticleValueKind.Curve;

    /// <summary>放出時スカラー化: Range は一様乱数、それ以外は起点 A。</summary>
    public float Sample(ref Xorshift64 rng)
        => Kind == ParticleValueKind.Range ? rng.NextRange(A, B) : A;

    /// <summary>寿命 t01∈[0,1] での値: Curve は A→B を補間、それ以外は起点 A (定数)。</summary>
    public float Eval(float t01)
    {
        if (Kind != ParticleValueKind.Curve) return A;
        float k = (Curve ?? LinearCurve.Instance).Eval(Math.Clamp(t01, 0f, 1f));
        return A + (B - A) * k;
    }
}
