using System.Numerics;

namespace Luxel.Strudel;

/// <summary>
/// パターン時間の有理数表現 (Tidal/Strudel の Rational 相当)。**float だと 1/3 拍が壊れる** —
/// 3 連符や 1/6 シフトの合成でサイクル境界の判定 (floor) が誤差でずれ、イベントの取りこぼし/二重発火になる。
/// 常に既約 (gcd 正規化) + 分母正。演算は long で行い、桁あふれは例外にする (実用パターンでは到達しない)。
/// 時間の単位は「サイクル」— 1 サイクル = 1 小節相当で、秒への換算は cps (cycles per second) を掛ける。
/// </summary>
public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
{
    /// <summary>分子 (符号を持つ)。</summary>
    public long Num { get; }
    /// <summary>分母 (常に正、既約)。</summary>
    public long Den { get; }

    public static readonly Fraction Zero = new(0, 1);
    public static readonly Fraction One = new(1, 1);

    public Fraction(long num, long den = 1)
    {
        if (den == 0) throw new DivideByZeroException("Fraction: 分母 0");
        if (den < 0) { num = -num; den = -den; }
        long g = Gcd(Math.Abs(num), den);
        if (g > 1) { num /= g; den /= g; }
        Num = num; Den = den;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a == 0 ? 1 : a;
    }

    public static implicit operator Fraction(long v) => new(v);
    public static implicit operator Fraction(int v) => new(v);

    public static Fraction operator +(Fraction a, Fraction b)
        => new(checked(a.Num * b.Den + b.Num * a.Den), checked(a.Den * b.Den));
    public static Fraction operator -(Fraction a, Fraction b)
        => new(checked(a.Num * b.Den - b.Num * a.Den), checked(a.Den * b.Den));
    public static Fraction operator -(Fraction a) => new(-a.Num, a.Den);
    public static Fraction operator *(Fraction a, Fraction b)
        => new(checked(a.Num * b.Num), checked(a.Den * b.Den));
    public static Fraction operator /(Fraction a, Fraction b)
        => b.Num == 0 ? throw new DivideByZeroException("Fraction: 0 除算")
                      : new(checked(a.Num * b.Den), checked(a.Den * b.Num));

    public static bool operator ==(Fraction a, Fraction b) => a.Num == b.Num && a.Den == b.Den;
    public static bool operator !=(Fraction a, Fraction b) => !(a == b);
    public static bool operator <(Fraction a, Fraction b) => (BigInteger)a.Num * b.Den < (BigInteger)b.Num * a.Den;
    public static bool operator >(Fraction a, Fraction b) => b < a;
    public static bool operator <=(Fraction a, Fraction b) => !(b < a);
    public static bool operator >=(Fraction a, Fraction b) => !(a < b);

    /// <summary>床関数 (負値も数学的 floor — サイクル番号の取得)。</summary>
    public long Floor => Num >= 0 ? Num / Den : (Num - Den + 1) / Den;

    /// <summary>属するサイクルの先頭 (Tidal の sam)。</summary>
    public Fraction Sam => new(Floor);
    /// <summary>次のサイクル先頭。</summary>
    public Fraction NextSam => new(Floor + 1);
    /// <summary>サイクル内位置 (this - Sam、[0,1))。</summary>
    public Fraction CyclePos => this - Sam;

    public static Fraction Min(Fraction a, Fraction b) => a <= b ? a : b;
    public static Fraction Max(Fraction a, Fraction b) => a >= b ? a : b;

    public double ToDouble() => (double)Num / Den;

    public int CompareTo(Fraction other) => this < other ? -1 : this == other ? 0 : 1;
    public bool Equals(Fraction other) => this == other;
    public override bool Equals(object? obj) => obj is Fraction f && this == f;
    public override int GetHashCode() => HashCode.Combine(Num, Den);
    public override string ToString() => Den == 1 ? Num.ToString() : $"{Num}/{Den}";
}
