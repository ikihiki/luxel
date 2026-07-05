using Luxel.TwoD;

namespace Luxel.UI;

/// <summary>外観バリアント (塗り/淡色/枠線/透明)。</summary>
public enum Variant { Filled, Tonal, Outline, Ghost }

/// <summary>意味的な色 (主/中立/成功/警告/危険/情報)。</summary>
public enum Intent { Primary, Neutral, Success, Warning, Danger, Info }

/// <summary>対話状態。</summary>
public enum ControlState { Normal, Hover, Active, Disabled }

/// <summary>解決済みの見た目 (背景/前景/枠線色, 枠線太さ)。</summary>
public readonly record struct VisualStyle(uint Bg, uint Fg, uint Border, float BorderWidth);

/// <summary>(theme, variant, intent, state) → 配色を解決する。</summary>
public static class Styles
{
    public static uint Base(Theme t, Intent i) => i switch
    {
        Intent.Primary => t.Primary,
        Intent.Success => t.Success,
        Intent.Warning => t.Warning,
        Intent.Danger => t.Danger,
        Intent.Info => t.Info,
        _ => t.SurfaceAlt,
    };

    private static uint Hover(Theme t, Intent i) => i == Intent.Primary ? t.PrimaryHover : Lighten(Base(t, i), 0.12f);
    private static uint Active(Theme t, Intent i) => i == Intent.Primary ? t.PrimaryActive : Darken(Base(t, i), 0.10f);

    public static VisualStyle Resolve(Theme t, Variant v, Intent i, ControlState s)
    {
        uint accent = s switch
        {
            ControlState.Hover => Hover(t, i),
            ControlState.Active => Active(t, i),
            _ => Base(t, i),
        };
        bool neutral = i == Intent.Neutral;
        uint onAccent = neutral ? t.Text : t.OnAccent;

        VisualStyle vs = v switch
        {
            Variant.Filled => new(accent, onAccent, accent, 0),
            Variant.Tonal => new(WithAlpha(accent, neutral ? (byte)255 : (byte)40), neutral ? t.Text : Base(t, i), 0, 0),
            Variant.Outline => new(0x00000000, neutral ? t.Text : Base(t, i), accent, 1.5f),
            Variant.Ghost => new(0x00000000, neutral ? t.Text : Base(t, i), 0, 0),
            _ => new(accent, onAccent, accent, 0),
        };

        if (s == ControlState.Disabled)
            vs = vs with { Bg = WithAlpha(vs.Bg, 90), Fg = t.TextMuted, Border = WithAlpha(vs.Border, 90) };
        return vs;
    }

    // ---- 色ユーティリティ (RGBA 各成分操作) ----
    public static uint Lighten(uint rgba, float t) => Mix(rgba, Color2D.White, t);
    public static uint Darken(uint rgba, float t) => Mix(rgba, Color2D.Rgba(0, 0, 0), t);

    public static uint Mix(uint a, uint b, float t)
    {
        (byte ar, byte ag, byte ab, byte aa) = Unpack(a);
        (byte br, byte bg, byte bb, byte _) = Unpack(b);
        byte L(byte x, byte y) => (byte)(x + (y - x) * Math.Clamp(t, 0, 1));
        return Color2D.Rgba(L(ar, br), L(ag, bg), L(ab, bb), aa);
    }

    public static uint WithAlpha(uint rgba, byte alpha)
    {
        (byte r, byte g, byte b, byte _) = Unpack(rgba);
        return Color2D.Rgba(r, g, b, alpha);
    }

    private static (byte r, byte g, byte b, byte a) Unpack(uint c)
        => ((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF), (byte)((c >> 24) & 0xFF));
}
