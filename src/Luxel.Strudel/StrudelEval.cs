using System.Globalization;
using System.Text;
using Luxel.Audio.Sequencing;

namespace Luxel.Strudel;

/// <summary>評価エラー (0 始まりの文字位置付き)。ミニ記法の入れ子エラーは文字列リテラル内の
/// 位置へ換算して伝播する — REPL がそのままキャレット表示できる。</summary>
public sealed class StrudelEvalError(string message, int position)
    : Exception($"{message} (位置 {position})")
{
    public int Position { get; } = position;
}

/// <summary>評価結果 — パターン (再生内容) か、セッション設定 (cps) のどちらか。</summary>
public sealed class EvalResult
{
    /// <summary>再生するパターン (null = パターン変更なし)。</summary>
    public Pattern<ControlMap>? Pattern { get; init; }
    /// <summary>cps(x) が評価された場合のテンポ変更。</summary>
    public double? Cps { get; init; }
}

/// <summary>
/// Strudel 風チェーン式の極小インタプリタ。JS eval の代わりに
/// 「関数呼び出し + メソッドチェーン + 数値/文字列リテラル」だけを解釈する:
/// <code>
/// s("bd*2 [~ sd] hh*4").fast(2).gain(0.8)
/// note("c3 e3 g3 &lt;a3 b3&gt;").s("saw").slow(2)
/// stack(s("bd*2"), s("hh*4").gain("1 0.6"))
/// s("bd*4").every(2, rev).jux(fast(2))
/// silence / cps(0.6)
/// </code>
/// 値は 4 種: 数値 / 文字列 / パターン / 変換 (パターン→パターン)。`rev` や `fast(2)` は
/// 単独では**変換**であり、every/off/jux の引数にそのまま渡せる。
/// </summary>
public static class StrudelEval
{
    /// <summary>1 式を評価する。失敗は <see cref="StrudelEvalError"/>。</summary>
    public static EvalResult Evaluate(string code)
    {
        var p = new Parser(code);
        Val v = p.ParseExpr();
        p.ExpectEnd();
        return v switch
        {
            PatV pat => new EvalResult { Pattern = pat.P },
            CpsV c => new EvalResult { Cps = c.V },
            FnV => throw new StrudelEvalError("変換だけではパターンになりません (s() 等に適用してください)", 0),
            _ => throw new StrudelEvalError("式がパターンを返しません", 0),
        };
    }

    // ---- 値 ----
    private abstract record Val;
    private sealed record NumV(double D, Fraction F) : Val;
    private sealed record StrV(string S, int Pos) : Val;
    private sealed record PatV(Pattern<ControlMap> P) : Val;
    private sealed record FnV(Func<Pattern<ControlMap>, Pattern<ControlMap>> F) : Val;
    private sealed record CpsV(double V) : Val;

    private struct Parser(string src)
    {
        private readonly string _s = src;
        private int _i;

        private void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private char Peek() { SkipWs(); return _i < _s.Length ? _s[_i] : '\0'; }

        public void ExpectEnd()
        {
            if (Peek() != '\0') throw new StrudelEvalError($"式の後に余分な入力があります: '{_s[_i]}'", _i);
        }

        public Val ParseExpr()
        {
            Val v = ParsePrimary();
            while (Peek() == '.')
            {
                // 数値の小数点はリテラル側で消費済み — ここに来る '.' はメソッド
                _i++;
                int npos = _i;
                string name = ReadIdent();
                List<Val> args = Peek() == '(' ? ParseArgs() : [];
                v = ApplyMethod(v, name, args, npos);
            }
            return v;
        }

        private Val ParsePrimary()
        {
            char c = Peek();
            if (c == '"') return ReadString();
            if (char.IsAsciiDigit(c) || (c == '-' && _i + 1 < _s.Length && char.IsAsciiDigit(_s[_i + 1])))
                return ReadNumber();
            if (char.IsLetter(c) || c == '_')
            {
                int pos = _i;
                string name = ReadIdent();
                List<Val>? args = Peek() == '(' ? ParseArgs() : null;
                return ApplyGlobal(name, args, pos);
            }
            throw new StrudelEvalError($"式が読めません: '{c}'", _i);
        }

        private List<Val> ParseArgs()
        {
            _i++;   // '('
            var args = new List<Val>();
            if (Peek() == ')') { _i++; return args; }
            while (true)
            {
                args.Add(ParseExpr());
                char c = Peek();
                if (c == ',') { _i++; continue; }
                if (c == ')') { _i++; return args; }
                throw new StrudelEvalError("')' が閉じていません", _i);
            }
        }

        // ---- グローバル関数 (args == null は裸の識別子) ----
        private Val ApplyGlobal(string name, List<Val>? args, int pos)
        {
            switch (name)
            {
                case "silence" when args is null or []: return new PatV(Pat.Silence<ControlMap>());
                case "rev" when args is null or []: return new FnV(static p => p.Rev());
                case "degrade" when args is null: return new FnV(static p => p.Degrade());

                case "s" or "sound": return new PatV(Controls.S(MiniArg(name, args, pos)));
                case "note": return new PatV(Controls.Note(MiniArg(name, args, pos)));
                case "n": return new PatV(Controls.N(MiniArg(name, args, pos)));
                case "chord": return new PatV(Controls.Chord(MiniArg(name, args, pos)));

                case "stack" or "cat":
                    {
                        var pats = new List<Pattern<ControlMap>>();
                        foreach (Val a in args ?? [])
                            pats.Add(a is PatV pv ? pv.P
                                : throw new StrudelEvalError($"{name}() の引数はパターンです", pos));
                        return new PatV(name == "stack" ? Pat.Stack(pats) : Pat.Cat(pats));
                    }

                case "cps":
                    return args is [NumV n2]
                        ? new CpsV(n2.D)
                        : throw new StrudelEvalError("cps(数値) の形で使います", pos);

                // 単独では「変換」を返す (every/off/jux の引数用)
                case "fast": return new FnV(TimeFn(name, args, pos, static (p, k) => p.Fast(k)));
                case "slow": return new FnV(TimeFn(name, args, pos, static (p, k) => p.Slow(k)));
                case "early": return new FnV(TimeFn(name, args, pos, static (p, k) => p.Early(k)));
                case "late": return new FnV(TimeFn(name, args, pos, static (p, k) => p.Late(k)));

                default:
                    throw new StrudelEvalError($"未知の関数 '{name}'", pos);
            }
        }

        private static Func<Pattern<ControlMap>, Pattern<ControlMap>> TimeFn(
            string name, List<Val>? args, int pos,
            Func<Pattern<ControlMap>, Fraction, Pattern<ControlMap>> f)
            => args is [NumV n]
                ? p => f(p, n.F)
                : throw new StrudelEvalError($"{name}(数値) の形で使います", pos);

        /// <summary>ミニ記法引数 (文字列リテラル 1 つ)。入れ子エラーは元コードの位置へ換算。</summary>
        private static Pattern<string> MiniArg(string name, List<Val>? args, int pos)
        {
            if (args is not [StrV s])
                throw new StrudelEvalError($"{name}(\"…\") の形で使います", pos);
            try { return MiniNotation.Parse(s.S); }
            catch (MiniNotationError e)
            {
                throw new StrudelEvalError(e.Message, s.Pos + 1 + e.Position);
            }
        }

        // ---- メソッド ----
        private Val ApplyMethod(Val target, string name, List<Val> args, int pos)
        {
            if (target is not PatV pv)
                throw new StrudelEvalError($".{name} はパターンにだけ使えます", pos);
            Pattern<ControlMap> p = pv.P;

            Pattern<ControlMap> r = name switch
            {
                "fast" => p.Fast(FracArg(name, args, pos)),
                "slow" => p.Slow(FracArg(name, args, pos)),
                "early" => p.Early(FracArg(name, args, pos)),
                "late" => p.Late(FracArg(name, args, pos)),
                "rev" => p.Rev(),
                "iter" => p.Iter((int)FracArg(name, args, pos).ToDouble()),
                "degrade" => p.Degrade(args is [NumV a] ? a.D : 0.5),
                "every" => args is [NumV n, FnV f]
                    ? p.Every((int)n.D, f.F)
                    : throw new StrudelEvalError("every(回数, 変換) の形で使います", pos),
                "off" => args is [NumV t, FnV f2]
                    ? p.Off(t.F, f2.F)
                    : throw new StrudelEvalError("off(ずれ, 変換) の形で使います", pos),
                "jux" => args is [FnV f3]
                    ? p.Jux(f3.F)
                    : throw new StrudelEvalError("jux(変換) の形で使います", pos),
                "gain" => p.Gain(FloatPatArg(name, args, pos)),
                "pan" => p.Pan(FloatPatArg(name, args, pos)),
                "speed" => p.Speed(FloatPatArg(name, args, pos)),
                "s" or "sound" => p.Sound(MiniArg(name, args, pos)),
                "note" => p.NoteSet(MiniArg(name, args, pos)),
                "scale" => ScaleMethod(p, name, args, pos),
                _ => throw new StrudelEvalError($"未知のメソッド '.{name}'", pos),
            };
            return new PatV(r);
        }

        private static Pattern<ControlMap> ScaleMethod(Pattern<ControlMap> p, string name, List<Val> args, int pos)
        {
            if (args is not [StrV s])
                throw new StrudelEvalError($".{name}(\"…\") の形で使います", pos);
            try { return p.Scale(s.S); }
            catch (FormatException e) { throw new StrudelEvalError(e.Message, s.Pos); }
        }

        private static Fraction FracArg(string name, List<Val> args, int pos)
            => args is [NumV n] ? n.F
             : throw new StrudelEvalError($".{name}(数値) の形で使います", pos);

        private static Pattern<float> FloatPatArg(string name, List<Val> args, int pos)
        {
            if (args is [NumV n]) return Pat.Pure((float)n.D);
            if (args is [StrV s])
            {
                try { return Controls.FloatPattern(s.S); }
                catch (MiniNotationError e) { throw new StrudelEvalError(e.Message, s.Pos + 1 + e.Position); }
                catch (FormatException e) { throw new StrudelEvalError(e.Message, s.Pos); }
            }
            throw new StrudelEvalError($".{name}(数値|\"パターン\") の形で使います", pos);
        }

        // ---- 字句 ----
        private string ReadIdent()
        {
            SkipWs();
            var sb = new StringBuilder();
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_'))
                sb.Append(_s[_i++]);
            if (sb.Length == 0) throw new StrudelEvalError("識別子が必要です", _i);
            return sb.ToString();
        }

        private StrV ReadString()
        {
            SkipWs();
            int start = _i;
            _i++;   // '"'
            var sb = new StringBuilder();
            while (_i < _s.Length && _s[_i] != '"') sb.Append(_s[_i++]);
            if (_i >= _s.Length) throw new StrudelEvalError("文字列が閉じていません", start);
            _i++;
            return new StrV(sb.ToString(), start);
        }

        private NumV ReadNumber()
        {
            SkipWs();
            bool neg = _s[_i] == '-';
            if (neg) _i++;
            long num = 0, den = 1;
            while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) num = num * 10 + (_s[_i++] - '0');
            if (_i < _s.Length && _s[_i] == '.' && _i + 1 < _s.Length && char.IsAsciiDigit(_s[_i + 1]))
            {
                _i++;
                while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) { num = num * 10 + (_s[_i++] - '0'); den *= 10; }
            }
            if (neg) num = -num;
            var f = new Fraction(num, den);
            return new NumV(f.ToDouble(), f);
        }
    }
}
