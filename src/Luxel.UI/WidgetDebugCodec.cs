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
    /// <summary>rgba (little-endian 0xAABBGGRR パック) を #rrggbb に落とす。</summary>
    public static string FormatColor(uint u)
    {
        byte r = (byte)(u & 0xff), g = (byte)((u >> 8) & 0xff), b = (byte)((u >> 16) & 0xff);
        return $"#{r:x2}{g:x2}{b:x2}";
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
        if (T.TryParse(CoerceString(value), System.Globalization.CultureInfo.InvariantCulture, out T parsed))
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
            return (T)parsed;
        }
        throw new InvalidCastException($"Debug arg type '{typeof(T).FullName}' requires a generated parser.");
    }

    // ---- JSON → 値の coerce (型別) ----

    /// <summary>"#rrggbb" 文字列 or 整数 → rgba パック uint (A=ff)。</summary>
    public static uint CoerceColor(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
        {
            string s = el.GetString() ?? "";
            if (s.StartsWith('#')) s = s[1..];
            if (uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint rgb))
            {
                byte r = (byte)((rgb >> 16) & 0xff), g = (byte)((rgb >> 8) & 0xff), b = (byte)(rgb & 0xff);
                return 0xff000000u | ((uint)b << 16) | ((uint)g << 8) | r;   // A=ff, packed R->G->B->A little-endian
            }
            return uint.TryParse(s, out uint u) ? u : 0u;
        }
        return el.ValueKind == JsonValueKind.Number ? el.GetUInt32() : 0u;
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
