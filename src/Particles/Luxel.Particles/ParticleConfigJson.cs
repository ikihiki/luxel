using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Luxel.Animation;

namespace Luxel.Particles;

/// <summary>
/// <see cref="ParticleConfig"/> の JSON 往復。リソース DAG の watch/reload に乗せると
/// 「JSON 保存 → 実行中のゲームでエフェクトが変わる」ライブ編集が既存機構のタダ乗りで成立する。
/// <para>形式 (すべての <see cref="ParticleValue"/> は数値=Const か <c>{"const":x}</c>/<c>{"range":[a,b]}</c>/
/// <c>{"curve":[a,b],"ease":"easeIn"}</c>、色は <c>"#RRGGBBAA"</c> または <c>{"start","end","ease"}</c>):</para>
/// <code>{ "life":{"range":[0.4,0.9]}, "speed":{"range":[60,160]}, "spread":3.14159, "angle":-1.5708,
///   "gravity":260, "drag":0.6, "size":{"const":5},
///   "color":{"start":"#FFE678FF","end":"#E63C2800"}, "shape":"quad" }</code>
/// </summary>
public static class ParticleConfigJson
{
    public static ParticleConfig FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement r = doc.RootElement;
        return new ParticleConfig(
            Life: ParseValue(Req(r, "life")),
            Speed: ParseValue(Req(r, "speed")),
            SpreadRadians: OptFloat(r, "spread", 0f),
            BaseAngle: OptFloat(r, "angle", 0f),
            Gravity: OptFloat(r, "gravity", 0f),
            Drag: OptFloat(r, "drag", 0f),
            Size: ParseValue(Req(r, "size")),
            Color: ParseColor(Req(r, "color")),
            Shape: ParseShape(r));
    }

    public static string ToJson(ParticleConfig c)
    {
        var buf = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buf, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            WriteValue(w, "life", c.Life);
            WriteValue(w, "speed", c.Speed);
            w.WriteNumber("spread", c.SpreadRadians);
            w.WriteNumber("angle", c.BaseAngle);
            w.WriteNumber("gravity", c.Gravity);
            w.WriteNumber("drag", c.Drag);
            WriteValue(w, "size", c.Size);
            w.WritePropertyName("color");
            w.WriteStartObject();
            w.WriteString("start", Hex(c.Color.Start));
            w.WriteString("end", Hex(c.Color.End));
            if (EaseName(c.Color.Curve) is { } ce) w.WriteString("ease", ce);
            w.WriteEndObject();
            w.WriteString("shape", c.Shape == ParticleShape.Circle ? "circle" : "quad");
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    // ---- パース ----

    private static ParticleValue ParseValue(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Number) return ParticleValue.Const((float)e.GetDouble());
        if (e.ValueKind != JsonValueKind.Object) throw new FormatException("ParticleValue は数値かオブジェクト。");
        if (e.TryGetProperty("const", out JsonElement cv)) return ParticleValue.Const((float)cv.GetDouble());
        if (e.TryGetProperty("range", out JsonElement rv))
        {
            (float a, float b) = Pair(rv);
            return ParticleValue.Range(a, b);
        }
        if (e.TryGetProperty("curve", out JsonElement cu))
        {
            (float a, float b) = Pair(cu);
            return ParticleValue.Curved(a, b, ParseEase(OptString(e, "ease")));
        }
        throw new FormatException("ParticleValue に const/range/curve がありません。");
    }

    private static ParticleColor ParseColor(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String) return ParticleColor.Const(ParseHex(e.GetString()!));
        if (e.ValueKind != JsonValueKind.Object) throw new FormatException("color は文字列かオブジェクト。");
        uint start = ParseHex(Req(e, "start").GetString() ?? throw new FormatException("color.start が必要。"));
        uint end = e.TryGetProperty("end", out JsonElement ee) ? ParseHex(ee.GetString()!) : start;
        return new ParticleColor(start, end, ParseEase(OptString(e, "ease")));
    }

    private static ParticleShape ParseShape(JsonElement r)
        => OptString(r, "shape")?.ToLowerInvariant() == "circle" ? ParticleShape.Circle : ParticleShape.Quad;

    private static (float, float) Pair(JsonElement arr)
    {
        if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 2)
            throw new FormatException("[min,max] の 2 要素配列が必要。");
        return ((float)arr[0].GetDouble(), (float)arr[1].GetDouble());
    }

    private static ICurve? ParseEase(string? name) => name?.ToLowerInvariant().Replace("-", "") switch
    {
        null or "" or "linear" => null,
        "ease" => CubicBezierCurve.Ease,
        "easein" => CubicBezierCurve.EaseIn,
        "easeout" => CubicBezierCurve.EaseOut,
        "easeinout" => CubicBezierCurve.EaseInOut,
        _ => null,
    };

    private static uint ParseHex(string s)
    {
        s = s.TrimStart('#');
        if (s.Length == 6) s += "FF";   // #RRGGBB は不透明
        if (s.Length != 8) throw new FormatException($"色は #RRGGBB(AA) 形式: '{s}'");
        byte r = Byte(s, 0), g = Byte(s, 2), b = Byte(s, 4), a = Byte(s, 6);
        return (uint)(r | (g << 8) | (b << 16) | (a << 24));   // Color2D.Rgba と同じ並び (R が下位)
        static byte Byte(string s, int i) => byte.Parse(s.AsSpan(i, 2), NumberStyles.HexNumber);
    }

    // ---- シリアライズ ----

    private static void WriteValue(Utf8JsonWriter w, string name, ParticleValue v)
    {
        w.WritePropertyName(name);
        w.WriteStartObject();
        switch (v.Kind)
        {
            case ParticleValueKind.Const:
                w.WriteNumber("const", v.A);
                break;
            case ParticleValueKind.Range:
                w.WritePropertyName("range");
                w.WriteStartArray();
                w.WriteNumberValue(v.A);
                w.WriteNumberValue(v.B);
                w.WriteEndArray();
                break;
            case ParticleValueKind.Curve:
                w.WritePropertyName("curve");
                w.WriteStartArray();
                w.WriteNumberValue(v.A);
                w.WriteNumberValue(v.B);
                w.WriteEndArray();
                if (EaseName(v.Curve) is { } en) w.WriteString("ease", en);
                break;
        }
        w.WriteEndObject();
    }

    private static string Hex(uint c)
    {
        byte r = (byte)(c & 0xFF), g = (byte)((c >> 8) & 0xFF), b = (byte)((c >> 16) & 0xFF), a = (byte)((c >> 24) & 0xFF);
        return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    private static string? EaseName(ICurve? c) => c switch
    {
        null => null,
        CubicBezierCurve b => b switch
        {
            { X1: 0.42f, Y1: 0f, X2: 1f, Y2: 1f } => "easeIn",
            { X1: 0f, Y1: 0f, X2: 0.58f, Y2: 1f } => "easeOut",
            { X1: 0.42f, Y1: 0f, X2: 0.58f, Y2: 1f } => "easeInOut",
            { X1: 0.25f, Y1: 0.1f, X2: 0.25f, Y2: 1f } => "ease",
            _ => null,
        },
        _ => null,
    };

    // ---- 小物 ----

    private static JsonElement Req(JsonElement e, string prop)
        => e.TryGetProperty(prop, out JsonElement v) ? v : throw new FormatException($"必須プロパティ \"{prop}\" がありません。");

    private static float OptFloat(JsonElement e, string prop, float dflt)
        => e.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : dflt;

    private static string? OptString(JsonElement e, string prop)
        => e.TryGetProperty(prop, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
