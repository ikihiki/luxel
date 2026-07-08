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

    /// <summary>コードパターン: "C Am F G" → 各ステップを複数 Note (同時発音) へ展開する。</summary>
    public static Pattern<ControlMap> Chord(Pattern<string> p)
        => p.FlatMapValues(static tok => ChordNotes(tok).Select(static note => new ControlMap(Note: note)));

    // ---- スケール / コード ----

    /// <summary>度数 (N) → スケール上の Note へ写像する (Note を設定し N は消す)。
    /// spec は "C:minor" / "c4:dorian" / "major" (ルート省略時は c4=60)。</summary>
    public static Pattern<ControlMap> Scale(this Pattern<ControlMap> p, string spec)
    {
        (int rootMidi, int[] steps) = ParseScale(spec);
        return p.Select(c => c with
        {
            Note = DegreeToNote(rootMidi, steps, (int)MathF.Round(c.N ?? 0f)),
            N = null,
        });
    }

    /// <summary>スケール表 (1 オクターブ内の半音オフセット)。</summary>
    private static readonly Dictionary<string, int[]> ScaleTable = new(StringComparer.Ordinal)
    {
        ["major"] = [0, 2, 4, 5, 7, 9, 11],
        ["ionian"] = [0, 2, 4, 5, 7, 9, 11],
        ["minor"] = [0, 2, 3, 5, 7, 8, 10],
        ["aeolian"] = [0, 2, 3, 5, 7, 8, 10],
        ["dorian"] = [0, 2, 3, 5, 7, 9, 10],
        ["phrygian"] = [0, 1, 3, 5, 7, 8, 10],
        ["lydian"] = [0, 2, 4, 6, 7, 9, 11],
        ["mixolydian"] = [0, 2, 4, 5, 7, 9, 10],
        ["locrian"] = [0, 1, 3, 5, 6, 8, 10],
        ["harmonicminor"] = [0, 2, 3, 5, 7, 8, 11],
        ["melodicminor"] = [0, 2, 3, 5, 7, 9, 11],
        ["majorpentatonic"] = [0, 2, 4, 7, 9],
        ["minorpentatonic"] = [0, 3, 5, 7, 10],
        ["chromatic"] = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
        ["wholetone"] = [0, 2, 4, 6, 8, 10],
    };

    /// <summary>コード品質 → ルートからの半音オフセット。接尾辞なし = メジャー。</summary>
    private static readonly Dictionary<string, int[]> ChordTable = new(StringComparer.Ordinal)
    {
        [""] = [0, 4, 7],
        ["maj"] = [0, 4, 7],
        ["m"] = [0, 3, 7],
        ["min"] = [0, 3, 7],
        ["dim"] = [0, 3, 6],
        ["aug"] = [0, 4, 8],
        ["sus2"] = [0, 2, 7],
        ["sus4"] = [0, 5, 7],
        ["6"] = [0, 4, 7, 9],
        ["m6"] = [0, 3, 7, 9],
        ["7"] = [0, 4, 7, 10],
        ["maj7"] = [0, 4, 7, 11],
        ["m7"] = [0, 3, 7, 10],
        ["min7"] = [0, 3, 7, 10],
        ["dim7"] = [0, 3, 6, 9],
        ["m7b5"] = [0, 3, 6, 10],
        ["9"] = [0, 4, 7, 10, 14],
        ["maj9"] = [0, 4, 7, 11, 14],
        ["m9"] = [0, 3, 7, 10, 14],
        ["add9"] = [0, 4, 7, 14],
    };

    private static (int RootMidi, int[] Steps) ParseScale(string spec)
    {
        string root = "c";
        string mode = spec.Trim().ToLowerInvariant();
        int colon = spec.IndexOf(':');
        if (colon >= 0)
        {
            root = spec[..colon];
            mode = spec[(colon + 1)..].Trim().ToLowerInvariant();
        }
        mode = mode.Replace(" ", "");
        if (!ScaleTable.TryGetValue(mode, out int[]? steps))
            throw new FormatException($"未知のスケール: '{mode}'");
        return (RootMidi(root), steps);
    }

    /// <summary>コードトークン ("C" / "Am" / "F#maj7") → 絶対 MIDI ノート群。</summary>
    private static IEnumerable<float> ChordNotes(string tok)
    {
        int i = 1;
        if (i < tok.Length && (tok[i] == '#' || tok[i] == 'b')) i++;
        string rootStr = tok[..i];
        string quality = tok[i..];
        if (!ChordTable.TryGetValue(quality, out int[]? offsets))
            throw new FormatException($"未知のコード: '{tok}'");
        int rootMidi = RootMidi(rootStr);
        return offsets.Select(o => (float)(rootMidi + o));
    }

    /// <summary>ルート表記 → MIDI。オクターブ省略時は c4 = 60 の帯。</summary>
    private static int RootMidi(string root)
    {
        root = root.Trim();
        if (root.Length == 0) return 60;
        return char.IsAsciiDigit(root[^1]) ? (int)ParseNote(root) : (int)ParseNote(root + "4");
    }

    /// <summary>度数 → 絶対 MIDI (負/オクターブ跨ぎは floor 除算で巻き上げ)。</summary>
    private static float DegreeToNote(int rootMidi, int[] steps, int degree)
    {
        int n = steps.Length;
        int idx = ((degree % n) + n) % n;
        int oct = (degree - idx) / n;
        return rootMidi + (oct * 12) + steps[idx];
    }

    // ---- ControlMap パターンへの後置修飾 (左の構造を保つ) ----

    public static Pattern<ControlMap> Gain(this Pattern<ControlMap> p, Pattern<float> v)
        => p.OpLeft(v, static (c, g) => c with { Gain = g });

    public static Pattern<ControlMap> Pan(this Pattern<ControlMap> p, Pattern<float> v)
        => p.OpLeft(v, static (c, x) => c with { Pan = x });

    public static Pattern<ControlMap> Speed(this Pattern<ControlMap> p, Pattern<float> v)
        => p.OpLeft(v, static (c, x) => c with { Speed = x });

    public static Pattern<ControlMap> Lpf(this Pattern<ControlMap> p, Pattern<float> v)
        => p.OpLeft(v, static (c, x) => c with { Cutoff = x });

    public static Pattern<ControlMap> Resonance(this Pattern<ControlMap> p, Pattern<float> v)
        => p.OpLeft(v, static (c, x) => c with { Resonance = x });

    public static Pattern<ControlMap> Delay(this Pattern<ControlMap> p, Pattern<float> v)
        => p.OpLeft(v, static (c, x) => c with { DelayTime = x });

    public static Pattern<ControlMap> DelayFeedback(this Pattern<ControlMap> p, Pattern<float> v)
        => p.OpLeft(v, static (c, x) => c with { DelayFeedback = x });

    public static Pattern<ControlMap> DelayMix(this Pattern<ControlMap> p, Pattern<float> v)
        => p.OpLeft(v, static (c, x) => c with { DelayMix = x });

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
            'c' => 0,
            'd' => 2,
            'e' => 4,
            'f' => 5,
            'g' => 7,
            'a' => 9,
            'b' => 11,
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
