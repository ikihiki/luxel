namespace Luxel.UI.Styling;

/// <summary>
/// StateStyle ベース Widget が共通で使う適用ヘルパ (Button/Border などで共有)。
/// </summary>
public static class StyleApply
{
    /// <summary>RGBA の Alpha 成分を opacity (0..1) で掛けた色を返す (RGB はそのまま)。</summary>
    public static uint MultiplyAlpha(uint rgba, float opacity)
    {
        if (opacity >= 1f) return rgba;
        if (opacity <= 0f) return rgba & 0x00FFFFFFu;
        byte a = (byte)((rgba >> 24) & 0xFF);
        byte newA = (byte)Math.Clamp((int)(a * opacity + 0.5f), 0, 255);
        return (rgba & 0x00FFFFFFu) | ((uint)newA << 24);
    }
}
