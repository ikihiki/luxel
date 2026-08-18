using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Luxel.UI;

/// <summary>
/// DebugProps の値フォーマット / JSON coerce。ソースジェネレーター (Luxel.UI.Generators) が
/// 各 widget に焼き込む DebugProps/SetDebugProp override から呼ばれる。
/// <para><see cref="Coerce{T}"/> の <c>typeof(T) == typeof(...)</c> 比較は JIT の値型特殊化で
/// 定数畳み込みされるため、実行時は boxing なし・分岐なしの直書きになる。</para>
/// </summary>
public static class WidgetDebugCodec
{
    /// <summary>rgba (little-endian 0xAABBGGRR パック) を #rrggbb / #rrggbbaa にする。</summary>
    public static string FormatColor(uint u)
    {
        byte r = (byte)(u & 0xff), g = (byte)((u >> 8) & 0xff), b = (byte)((u >> 16) & 0xff), a = (byte)(u >> 24);
        return a == 0xff ? $"#{r:x2}{g:x2}{b:x2}" : $"#{r:x2}{g:x2}{b:x2}{a:x2}";
    }

    /// <summary>null 許容 boxed 値の表示文字列 (色以外の任意型)。生成コードの非対応 T 用。</summary>
    public static string FormatBoxed(object? v) => v?.ToString() ?? "";

    // ---- 書き込み (生成コードの switch case から呼ばれる) ----

    /// <summary>JSON 値を Bindable フィールドへ直接書き込む (boxing なし)。</summary>
    public static void Write<T>(Bindable<T> field, JsonElement value)
        => field.SetOverride(Coerce<T>(value));

    /// <summary>JSON 値を BindableString フィールドへ直接書き込む (生成コードは同じ呼び形でオーバーロード解決)。</summary>
    public static void Write(BindableString field, JsonElement value)
        => field.SetOverride(CoerceString(value));

    /// <summary>IParsable な複合型 (Thickness 等) を文字列経由で書き込む。parse 失敗は no-op。</summary>
    public static void WriteParsable<T>(Bindable<T> field, JsonElement value) where T : IParsable<T>
    {
        if (T.TryParse(CoerceString(value), System.Globalization.CultureInfo.InvariantCulture, out T? parsed)
            && parsed is not null)
            field.SetOverride(parsed);
    }

    /// <summary>JSON → T for the supported Storybook/debug scalar surface.</summary>
    public static T Coerce<T>(JsonElement el)
    {
        if (typeof(T) == typeof(uint)) { uint v = CoerceColor(el); return Unsafe.As<uint, T>(ref v); }
        if (typeof(T) == typeof(int)) { int v = CoerceInt(el); return Unsafe.As<int, T>(ref v); }
        if (typeof(T) == typeof(float)) { float v = CoerceFloat(el); return Unsafe.As<float, T>(ref v); }
        if (typeof(T) == typeof(double)) { double v = CoerceDouble(el); return Unsafe.As<double, T>(ref v); }
        if (typeof(T) == typeof(bool)) { bool v = CoerceBool(el); return Unsafe.As<bool, T>(ref v); }
        if (typeof(T) == typeof(string)) { return (T)(object)CoerceString(el); }
        if (typeof(T) == typeof(Length))
        {
            if (!Length.TryParse(CoerceString(el), System.Globalization.CultureInfo.InvariantCulture, out Length length))
                throw new FormatException($"'{CoerceString(el)}' is not a valid Length.");
            return (T)(object)length;
        }
        if (typeof(T).IsEnum)
        {
            string value = CoerceString(el);
            if (!Enum.TryParse(typeof(T), value, ignoreCase: true, out object? parsed))
                throw new FormatException($"'{value}' is not a valid {typeof(T).Name} value.");
            return (T)parsed!;
        }
        throw new InvalidCastException($"Debug arg type '{typeof(T).FullName}' requires a generated parser.");
    }

    // ---- JSON → 値の coerce (型別) ----

    /// <summary>"#rgb" / "#rrggbb" / "#rrggbbaa" 文字列 or 整数 → rgba パック uint。</summary>
    public static uint CoerceColor(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Number) return el.GetUInt32();
        if (el.ValueKind != JsonValueKind.String)
            throw new FormatException("Color must be a hexadecimal string or an unsigned integer.");

        string text = el.GetString() ?? "";
        string hex = text.StartsWith('#') ? text[1..] : text;
        if (hex.Length == 3)
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        if (hex.Length is not (6 or 8)
            || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint rgba))
            throw new FormatException($"'{text}' is not a valid color.");

        byte r = (byte)(rgba >> (hex.Length == 8 ? 24 : 16));
        byte g = (byte)(rgba >> (hex.Length == 8 ? 16 : 8));
        byte b = (byte)(rgba >> (hex.Length == 8 ? 8 : 0));
        byte a = hex.Length == 8 ? (byte)rgba : (byte)0xff;
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)g << 8) | r;
    }

    public static int CoerceInt(JsonElement el)
        => el.ValueKind == JsonValueKind.Number ? el.GetInt32() : 0;

    public static float CoerceFloat(JsonElement el)
        => el.ValueKind == JsonValueKind.Number ? el.GetSingle()
         : el.ValueKind == JsonValueKind.String && float.TryParse(el.GetString(), out float v) ? v : 0f;

    public static double CoerceDouble(JsonElement el)
        => el.ValueKind == JsonValueKind.Number ? el.GetDouble() : 0.0;

    public static bool CoerceBool(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.True) return true;
        if (el.ValueKind == JsonValueKind.False) return false;
        return el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out bool b) && b;
    }

    public static string CoerceString(JsonElement el)
        => el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.ToString();
}
