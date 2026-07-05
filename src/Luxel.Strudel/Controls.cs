using Luxel.Audio.Sequencing;

namespace Luxel.Strudel;

/// <summary>
/// コントロールパターン層 — ミニ記法のトークン列を <see cref="ControlMap"/> パターンへ持ち上げ、
/// gain/pan 等の合成 (左の構造 + 右の値) を提供する。Strudel の controls.mjs 相当。
/// </summary>
public static class Controls
{
    /// <summary>音色パターン: "bd:3 sd" → Instrument=bd,N=3 / Instrument=sd。</summary>
    public static Pattern<ControlMap> S(Pattern<string> p)
        => p.Select(static tok =>
        {
            int i = tok.IndexOf(':');
            if (i < 0) return new ControlMap(Instrument: tok);
            float n = float.TryParse(tok[(i + 1)..], out float v) ? v : 0;
            return new ControlMap(Instrument: tok[..i], N: n);
        });

    /// <summary>ノートパターン: "c4 e4 g4" / "60 64 67" → Note (MIDI 番号、c4 = 60)。</summary>
    public static Pattern<ControlMap> Note(Pattern<string> p)
        => p.Select(static tok => new ControlMap(Note: ParseNote(tok)));

    /// <summary>番号パターン: "0 3 2" → N。</summary>
    public static Pattern<ControlMap> N(Pattern<string> p)
        => p.Select(static tok => new ControlMap(N: ParseFloat(tok)));

    // ---- ControlMap パターンへの後置修飾 (左の構造を保つ) ----

    public static Pattern<ControlMap> Gain(this Pattern<ControlMap> p, Pattern<float> v)
        => p.OpLeft(v, static (c, g) => c with { Gain = g });

    public static Pattern<ControlMap> Pan(this Pattern<ControlMap> p, Pattern<float> v)
        => p.OpLeft(v, static (c, x) => c with { Pan = x });

    public static Pattern<ControlMap> Speed(this Pattern<ControlMap> p, Pattern<float> v)
        => p.OpLeft(v, static (c, x) => c with { Speed = x });

    public static Pattern<ControlMap> Sound(this Pattern<ControlMap> p, Pattern<string> v)
        => p.OpLeft(v, static (c, s) => c with { Instrument = s });

    public static Pattern<ControlMap> NoteSet(this Pattern<ControlMap> p, Pattern<string> v)
        => p.OpLeft(v, static (c, s) => c with { Note = ParseNote(s) });

    /// <summary>左右分離 (jux): 原音を左、f 適用後を右へ。</summary>
    public static Pattern<ControlMap> Jux(this Pattern<ControlMap> p, Func<Pattern<ControlMap>, Pattern<ControlMap>> f)
        => Pat.Stack(
            p.Select(static c => c with { Pan = -1f }),
            f(p.Select(static c => c with { Pan = 1f })));

    /// <summary>数値ミニ記法 ("1 0.5 &lt;0.2 0.8&gt;") を float パターンへ。</summary>
    public static Pattern<float> FloatPattern(string mini)
        => MiniNotation.Parse(mini).Select(ParseFloat);

    /// <summary>ノート名/番号 → MIDI 番号 (c4 = 60)。"c4"/"c#4"/"eb3"/"60"/"60.5"。不明は例外。</summary>
    public static float ParseNote(string tok)
    {
        if (float.TryParse(tok, System.Globalization.CultureInfo.InvariantCulture, out float direct))
            return direct;
        string s = tok.Trim().ToLowerInvariant();
        if (s.Length < 2) throw new FormatException($"ノートが読めません: '{tok}'");
        int semitone = s[0] switch
        {
            'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5, 'g' => 7, 'a' => 9, 'b' => 11,
            _ => throw new FormatException($"ノートが読めません: '{tok}'"),
        };
        int i = 1;
        if (i < s.Length && s[i] == '#') { semitone++; i++; }
        else if (i < s.Length && s[i] == 'b') { semitone--; i++; }
        if (i >= s.Length || !int.TryParse(s[i..], out int oct))
            throw new FormatException($"ノートのオクターブが読めません: '{tok}'");
        return semitone + (oct + 1) * 12;
    }

    private static float ParseFloat(string tok)
        => float.TryParse(tok, System.Globalization.CultureInfo.InvariantCulture, out float v)
            ? v : throw new FormatException($"数値が読めません: '{tok}'");
}
