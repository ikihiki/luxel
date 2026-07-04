namespace Luxel.UI;

public readonly record struct Size(float Width, float Height)
{
    public static readonly Size Zero = new(0, 0);
    public static Size operator +(Size a, Size b) => new(a.Width + b.Width, a.Height + b.Height);
}

public readonly record struct Point(float X, float Y)
{
    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
}

public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public bool Contains(float px, float py) => px >= X && px <= X + Width && py >= Y && py <= Y + Height;
    public Rect Intersect(Rect o)
    {
        float x = MathF.Max(X, o.X), y = MathF.Max(Y, o.Y);
        float r = MathF.Min(X + Width, o.X + o.Width), b = MathF.Min(Y + Height, o.Y + o.Height);
        return new Rect(x, y, MathF.Max(0, r - x), MathF.Max(0, b - y));
    }
}

public readonly record struct Thickness(float Left, float Top, float Right, float Bottom) : IParsable<Thickness>
{
    public Thickness(float all) : this(all, all, all, all) { }
    public Thickness(float h, float v) : this(h, v, h, v) { }
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    /// <summary>"8" (全辺) / "8,4" (水平,垂直) / "8,4,8,4" (L,T,R,B)。DevTools の書き戻しで使用。</summary>
    public override string ToString() => $"{Left},{Top},{Right},{Bottom}";

    public static Thickness Parse(string s, IFormatProvider? provider)
        => TryParse(s, provider, out Thickness t) ? t : throw new FormatException($"invalid Thickness: '{s}'");

    public static bool TryParse(string? s, IFormatProvider? provider, out Thickness result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        provider ??= System.Globalization.CultureInfo.InvariantCulture;
        string[] p = s.Split(',', StringSplitOptions.TrimEntries);
        Span<float> v = stackalloc float[4];
        if (p.Length is not (1 or 2 or 4)) return false;
        for (int i = 0; i < p.Length; i++)
            if (!float.TryParse(p[i], System.Globalization.NumberStyles.Float, provider, out v[i])) return false;
        result = p.Length switch
        {
            1 => new Thickness(v[0]),
            2 => new Thickness(v[0], v[1]),
            _ => new Thickness(v[0], v[1], v[2], v[3]),
        };
        return true;
    }
}

public enum Align { Start, Center, End, Stretch }

public enum GridUnit { Pixel, Auto, Star }

/// <summary>列/行の長さ。<c>[1,2]</c> は int→star の暗黙変換で star 比率になる。</summary>
public readonly record struct GridLength(float Value, GridUnit Unit)
{
    public static implicit operator GridLength(int starWeight) => new(starWeight, GridUnit.Star);
    public static GridLength Star(float weight = 1) => new(weight, GridUnit.Star);
    public static GridLength Px(float pixels) => new(pixels, GridUnit.Pixel);
    public static GridLength Auto => new(0, GridUnit.Auto);
}

/// <summary>レイアウト制約 (Flutter の BoxConstraints 相当)。</summary>
public readonly record struct Constraints(float MinW, float MaxW, float MinH, float MaxH)
{
    public bool IsTightWidth => MinW >= MaxW;
    public bool IsTightHeight => MinH >= MaxH;
    public bool IsTight => IsTightWidth && IsTightHeight;

    public static Constraints Tight(Size s) => new(s.Width, s.Width, s.Height, s.Height);
    public static Constraints Loose(Size s) => new(0, s.Width, 0, s.Height);
    public static Constraints LooseW(float maxW, float maxH) => new(0, maxW, 0, maxH);

    public Size Constrain(Size s) => new(
        Math.Clamp(s.Width, MinW, MaxW), Math.Clamp(s.Height, MinH, MaxH));

    public Constraints Deflate(Thickness t) => new(
        MathF.Max(0, MinW - t.Horizontal), MathF.Max(0, MaxW - t.Horizontal),
        MathF.Max(0, MinH - t.Vertical), MathF.Max(0, MaxH - t.Vertical));
}
