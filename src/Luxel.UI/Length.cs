using System.Globalization;

namespace Luxel.UI;

/// <summary>寸法の単位 (CSS 相当)。<see cref="Unset"/> = 未指定 (default)。</summary>
public enum LengthUnit : byte
{
    Unset = 0,
    /// <summary>論理ピクセル (DPI スケール前)。数値リテラルの既定。</summary>
    Px,
    /// <summary>親の利用可能サイズに対する % (無限制約下では未指定扱い)。</summary>
    Percent,
    /// <summary>テーマ基準フォントサイズ (<see cref="Theme.Font"/>) の倍数。</summary>
    Em,
    /// <summary>ビューポート (UiHost 論理サイズ) 幅の %。</summary>
    Vw,
    /// <summary>ビューポート高さの %。</summary>
    Vh,
}

/// <summary>
/// 単位付き寸法 (CSS 風): <c>width: 380</c> (px) / <c>width: "50%"</c> / <c>"1.5em"</c> / <c>"40vw"</c>。
/// <c>default</c> = 未指定 (<see cref="LengthUnit.Unset"/>)。float/int/string から暗黙変換できる。
/// 解決は単一パスレイアウト中に <see cref="Resolve"/> — % は親の利用可能サイズ、em はテーマ基準フォント、
/// vw/vh は UiHost の論理ビューポート (<see cref="LayoutContext.ViewportW"/>)。
/// </summary>
public readonly struct Length : IEquatable<Length>, IParsable<Length>
{
    public readonly float Value;
    public readonly LengthUnit Unit;

    public Length(float value, LengthUnit unit)
    {
        Value = value;
        Unit = unit;
    }

    /// <summary>指定済みか (default = 未指定)。</summary>
    public bool IsSet => Unit != LengthUnit.Unset;

    public static implicit operator Length(float px) => new(px, LengthUnit.Px);
    public static implicit operator Length(int px) => new(px, LengthUnit.Px);
    public static implicit operator Length(double px) => new((float)px, LengthUnit.Px);
    /// <summary>CSS 風の文字列: "380" "380px" "50%" "1.5em" "40vw" "30vh"。</summary>
    public static implicit operator Length(string css) => Parse(css, CultureInfo.InvariantCulture);

    public static Length Percent(float v) => new(v, LengthUnit.Percent);
    public static Length Em(float v) => new(v, LengthUnit.Em);
    public static Length Vw(float v) => new(v, LengthUnit.Vw);
    public static Length Vh(float v) => new(v, LengthUnit.Vh);

    /// <summary>論理 px へ解決する。<paramref name="reference"/> は % の基準 (親の利用可能サイズ;
    /// 無限なら % は <paramref name="fallback"/>)。未指定も <paramref name="fallback"/>。</summary>
    public float Resolve(float reference, LayoutContext ctx, float fallback = 0) => Unit switch
    {
        LengthUnit.Px => Value,
        LengthUnit.Percent => float.IsInfinity(reference) ? fallback : reference * Value / 100f,
        LengthUnit.Em => Value * ctx.Theme.Font,
        LengthUnit.Vw => Value * ctx.ViewportW / 100f,
        LengthUnit.Vh => Value * ctx.ViewportH / 100f,
        _ => fallback,
    };

    public bool Equals(Length other) => Value.Equals(other.Value) && Unit == other.Unit;
    public override bool Equals(object? obj) => obj is Length l && Equals(l);
    public override int GetHashCode() => HashCode.Combine(Value, Unit);
    public static bool operator ==(Length a, Length b) => a.Equals(b);
    public static bool operator !=(Length a, Length b) => !a.Equals(b);

    public override string ToString() => Unit switch
    {
        LengthUnit.Unset => "",
        LengthUnit.Px => Value.ToString(CultureInfo.InvariantCulture),
        LengthUnit.Percent => Value.ToString(CultureInfo.InvariantCulture) + "%",
        LengthUnit.Em => Value.ToString(CultureInfo.InvariantCulture) + "em",
        LengthUnit.Vw => Value.ToString(CultureInfo.InvariantCulture) + "vw",
        LengthUnit.Vh => Value.ToString(CultureInfo.InvariantCulture) + "vh",
        _ => "",
    };

    public static Length Parse(string s, IFormatProvider? provider)
        => TryParse(s, provider, out Length l) ? l : throw new FormatException($"Length として解釈できません: '{s}'");

    public static bool TryParse(string? s, IFormatProvider? provider, out Length result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s)) return true;   // 空 = 未指定
        s = s.Trim();
        (string num, LengthUnit unit) =
            s.EndsWith("%", StringComparison.Ordinal) ? (s[..^1], LengthUnit.Percent) :
            s.EndsWith("px", StringComparison.OrdinalIgnoreCase) ? (s[..^2], LengthUnit.Px) :
            s.EndsWith("em", StringComparison.OrdinalIgnoreCase) ? (s[..^2], LengthUnit.Em) :
            s.EndsWith("vw", StringComparison.OrdinalIgnoreCase) ? (s[..^2], LengthUnit.Vw) :
            s.EndsWith("vh", StringComparison.OrdinalIgnoreCase) ? (s[..^2], LengthUnit.Vh) :
            (s, LengthUnit.Px);
        if (!float.TryParse(num, NumberStyles.Float, provider ?? CultureInfo.InvariantCulture, out float v))
            return false;
        result = new Length(v, unit);
        return true;
    }
}
