using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Luxel.Animation;

/// <summary>
/// CSS `@keyframes` テキスト → <see cref="AnimationClip"/> のシンプルなパーサ。
///
/// サポート構文:
/// <code>
/// @keyframes pulse {
///   0% { opacity: 0; transform: translateX(0px); }
///   50% { opacity: 1; transform: translateX(100px); }
///   100% { opacity: 0; }
/// }
/// </code>
///
/// 対応プロパティ:
///   - <c>opacity</c> → float
///   - <c>transform: translateX(N px) translateY(N px) scale(N) rotate(N deg|rad)</c>
///   - <c>color</c>/<c>background-color</c> → rgba(R,G,B,A) または rgb(R,G,B) または #RGBA/#RGB
///
/// 出力 path: "{targetPrefix}/{property}"。`prefix` は <see cref="Parse"/> の引数で指定。
/// 未対応プロパティは <see cref="ImportSeverity"/>=Warn でログ (将来は DiagWarning 発行)。
/// </summary>
public static class CssKeyframesImporter
{
    private static readonly Regex KeyframesBlock = new(
        @"@keyframes\s+(?<name>[A-Za-z_-][A-Za-z0-9_-]*)\s*\{(?<body>[^@]*)\}\s*$",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex StopBlock = new(
        @"(?<key>[\w\d.%]+|from|to)\s*\{(?<decls>[^{}]*)\}",
        RegexOptions.Compiled);

    /// <summary>
    /// CSS テキストから AnimationClip を作る。<paramref name="durationSec"/> は @keyframes は時間情報を持たないため
    /// (CSS の <c>animation-duration</c>) を引数で渡す。<paramref name="targetPrefix"/> は track の path 接頭辞。
    /// </summary>
    public static AnimationClip Parse(string css, string targetPrefix, float durationSec,
                                       List<string>? warnings = null)
    {
        ArgumentNullException.ThrowIfNull(css);
        ArgumentNullException.ThrowIfNull(targetPrefix);

        Match m = KeyframesBlock.Match(css.Trim());
        if (!m.Success) throw new ArgumentException("@keyframes block が見つかりません", nameof(css));
        string name = m.Groups["name"].Value;
        string body = m.Groups["body"].Value;

        // 各 stop (0% / 50% / from / to) を抽出
        var stops = new List<(float Offset, Dictionary<string, string> Decls)>();
        foreach (Match sm in StopBlock.Matches(body))
        {
            float offset = ParseStopKey(sm.Groups["key"].Value);
            var decls = ParseDeclarations(sm.Groups["decls"].Value);
            stops.Add((offset, decls));
        }
        stops.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        // プロパティ別に keyframes を収集
        var opacityKfs = new List<Keyframe<float>>();
        var translateXKfs = new List<Keyframe<float>>();
        var translateYKfs = new List<Keyframe<float>>();
        var scaleKfs = new List<Keyframe<Vector2>>();
        var rotateKfs = new List<Keyframe<float>>();
        var colorKfs = new List<Keyframe<uint>>();

        foreach (var (offset, decls) in stops)
        {
            float t = offset * durationSec;
            if (decls.TryGetValue("opacity", out var op) && TryParseFloat(op, out float opv))
                opacityKfs.Add(new(t, opv));

            if (decls.TryGetValue("transform", out var tf))
            {
                if (TryParseLengthFn(tf, "translateX", out float tx)) translateXKfs.Add(new(t, tx));
                if (TryParseLengthFn(tf, "translateY", out float ty)) translateYKfs.Add(new(t, ty));
                if (TryParseScale(tf, out var sc)) scaleKfs.Add(new(t, sc));
                if (TryParseRotate(tf, out float rot)) rotateKfs.Add(new(t, rot));
            }

            string? colorVal = decls.TryGetValue("color", out var c) ? c
                              : decls.TryGetValue("background-color", out var bc) ? bc
                              : null;
            if (colorVal != null && TryParseColor(colorVal, out uint rgba))
                colorKfs.Add(new(t, rgba));
        }

        var tracks = new List<TrackBase>();
        if (opacityKfs.Count >= 2)
            tracks.Add(Tracks.Float($"{targetPrefix}/opacity", InterpolationKind.Linear, opacityKfs.ToArray()));
        if (translateXKfs.Count >= 2)
            tracks.Add(Tracks.Float($"{targetPrefix}/translationX", InterpolationKind.Linear, translateXKfs.ToArray()));
        if (translateYKfs.Count >= 2)
            tracks.Add(Tracks.Float($"{targetPrefix}/translationY", InterpolationKind.Linear, translateYKfs.ToArray()));
        if (scaleKfs.Count >= 2)
            tracks.Add(Tracks.Vector2($"{targetPrefix}/scale", InterpolationKind.Linear, scaleKfs.ToArray()));
        if (rotateKfs.Count >= 2)
            tracks.Add(Tracks.Float($"{targetPrefix}/rotation", InterpolationKind.Linear, rotateKfs.ToArray()));
        if (colorKfs.Count >= 2)
            tracks.Add(Tracks.Color($"{targetPrefix}/color", InterpolationKind.Linear, colorKfs.ToArray()));

        if (tracks.Count == 0)
            warnings?.Add($"@keyframes '{name}' から有効な Track を抽出できませんでした (>= 2 個の keyframe が必要)");

        return new AnimationClip(name, tracks.ToArray());
    }

    private static float ParseStopKey(string key)
    {
        key = key.Trim().ToLowerInvariant();
        if (key == "from") return 0f;
        if (key == "to") return 1f;
        if (key.EndsWith('%'))
            return float.Parse(key[..^1], CultureInfo.InvariantCulture) / 100f;
        return float.Parse(key, CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, string> ParseDeclarations(string decls)
    {
        var result = new Dictionary<string, string>();
        foreach (var pair in decls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = pair.IndexOf(':');
            if (colon <= 0) continue;
            string prop = pair[..colon].Trim().ToLowerInvariant();
            string value = pair[(colon + 1)..].Trim();
            result[prop] = value;
        }
        return result;
    }

    private static bool TryParseFloat(string s, out float v)
        => float.TryParse(s.TrimEnd('%').TrimEnd(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static bool TryParseLengthFn(string transform, string fn, out float px)
    {
        px = 0f;
        var m = Regex.Match(transform, $@"{fn}\s*\(\s*(-?[\d.]+)\s*(px)?\s*\)");
        if (!m.Success) return false;
        return float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out px);
    }

    private static bool TryParseScale(string transform, out Vector2 s)
    {
        s = Vector2.One;
        // scale(N) or scale(X,Y)
        var m2 = Regex.Match(transform, @"scale\s*\(\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)");
        if (m2.Success)
        {
            s = new Vector2(
                float.Parse(m2.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(m2.Groups[2].Value, CultureInfo.InvariantCulture));
            return true;
        }
        var m1 = Regex.Match(transform, @"scale\s*\(\s*(-?[\d.]+)\s*\)");
        if (m1.Success)
        {
            float v = float.Parse(m1.Groups[1].Value, CultureInfo.InvariantCulture);
            s = new Vector2(v, v);
            return true;
        }
        return false;
    }

    private static bool TryParseRotate(string transform, out float radians)
    {
        radians = 0f;
        var m = Regex.Match(transform, @"rotate\s*\(\s*(-?[\d.]+)\s*(deg|rad)?\s*\)");
        if (!m.Success) return false;
        float v = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        string unit = m.Groups[2].Success ? m.Groups[2].Value : "deg";
        radians = unit == "rad" ? v : v * MathF.PI / 180f;
        return true;
    }

    private static bool TryParseColor(string s, out uint rgba)
    {
        rgba = 0;
        s = s.Trim().ToLowerInvariant();
        // rgba(r,g,b,a) or rgb(r,g,b)
        var mr = Regex.Match(s, @"rgba?\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([\d.]+))?\s*\)");
        if (mr.Success)
        {
            byte r = byte.Parse(mr.Groups[1].Value);
            byte g = byte.Parse(mr.Groups[2].Value);
            byte b = byte.Parse(mr.Groups[3].Value);
            float af = mr.Groups[4].Success ? float.Parse(mr.Groups[4].Value, CultureInfo.InvariantCulture) : 1f;
            byte a = (byte)Math.Clamp(af * 255f + 0.5f, 0f, 255f);
            rgba = (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);
            return true;
        }
        // #RRGGBB or #RRGGBBAA
        var mh = Regex.Match(s, @"#([0-9a-f]{6}|[0-9a-f]{8})");
        if (mh.Success)
        {
            string hex = mh.Groups[1].Value;
            byte r = Convert.ToByte(hex[..2], 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            byte a = hex.Length == 8 ? Convert.ToByte(hex.Substring(6, 2), 16) : (byte)255;
            rgba = (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);
            return true;
        }
        return false;
    }
}
