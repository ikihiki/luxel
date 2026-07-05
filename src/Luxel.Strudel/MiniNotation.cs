using System.Text;

namespace Luxel.Strudel;

/// <summary>ミニ記法のパースエラー (0 始まりの文字位置付き — REPL がインライン表示に使う)。</summary>
public sealed class MiniNotationError(string message, int position)
    : Exception($"{message} (位置 {position})")
{
    /// <summary>ソース内の 0 始まり文字位置。</summary>
    public int Position { get; } = position;
}

/// <summary>
/// Tidal/Strudel ミニ記法のパーサ (v1 サブセット)。
/// <code>
/// bd sd hh          シーケンス (1 サイクルを均等分割)
/// ~                 休符
/// [bd sd]           グループ (1 ステップに圧縮)、[a, b] はグループ内スタック
/// a*2  a/2          そのステップだけ速く/遅く
/// &lt;a b c&gt;           サイクルごとに交互 (1 サイクル 1 つ)
/// a@3 b             重み (a がサイクルの 3/4)
/// a?  a?0.3         確率で間引く (既定 0.5 — 時間ハッシュで決定的)
/// bd:3              サンプル番号 (トークンのまま — 分解は s() 側)
/// a , b             トップレベルのカンマ = スタック
/// </code>
/// 出力は <c>Pattern&lt;string&gt;</c> — 値の解釈 (音色名/ノート/数値) は上位層が行う。
/// </summary>
public static class MiniNotation
{
    /// <summary>パースして Pattern を返す。失敗は <see cref="MiniNotationError"/>。</summary>
    public static Pattern<string> Parse(string source)
    {
        var p = new Parser(source);
        Pattern<string> pat = p.ParseTop(closer: '\0');
        return pat;
    }

    private ref struct Parser(string src)
    {
        private readonly string _s = src;
        private int _i;

        private void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
        private char Peek() { SkipWs(); return _i < _s.Length ? _s[_i] : '\0'; }

        /// <summary>カンマ区切りのシーケンス列 (= スタック) を closer までパースする。</summary>
        public Pattern<string> ParseTop(char closer)
        {
            var layers = new List<Pattern<string>>();
            while (true)
            {
                layers.Add(ParseSequence(closer));
                char c = Peek();
                if (c == ',') { _i++; continue; }
                if (c == closer || (closer == '\0' && c == '\0')) break;
                if (c == '\0') throw new MiniNotationError($"'{closer}' が閉じていません", _i);
                throw new MiniNotationError($"予期しない文字 '{c}'", _i);
            }
            if (closer != '\0')
            {
                if (Peek() != closer) throw new MiniNotationError($"'{closer}' が閉じていません", _i);
                _i++;
            }
            return layers.Count == 1 ? layers[0] : Pat.Stack(layers);
        }

        /// <summary>空白区切りのステップ列 → 重み付き TimeCat。</summary>
        private Pattern<string> ParseSequence(char closer)
        {
            var parts = new List<(Fraction Weight, Pattern<string> P)>();
            while (true)
            {
                char c = Peek();
                if (c == '\0' || c == ',' || c == closer || c == ']' || c == '>') break;
                parts.Add(ParseTerm());
            }
            if (parts.Count == 0) return Pat.Silence<string>();
            if (parts.Count == 1 && parts[0].Weight == Fraction.One) return parts[0].P;
            return Pat.TimeCat(parts);
        }

        /// <summary>1 ステップ = 因子 + 後置修飾 (* / @ ?)。</summary>
        private (Fraction Weight, Pattern<string> P) ParseTerm()
        {
            Pattern<string> p = ParseFactor();
            Fraction weight = Fraction.One;
            while (true)
            {
                char c = _i < _s.Length ? _s[_i] : '\0';   // 後置は空白を挟まない
                if (c == '*') { _i++; p = p.Fast(ReadNumber("* の後")); }
                else if (c == '/') { _i++; p = p.Slow(ReadNumber("/ の後")); }
                else if (c == '@') { _i++; weight = ReadNumber("@ の後"); }
                else if (c == '?')
                {
                    _i++;
                    double amount = TryReadNumber(out Fraction f) ? f.ToDouble() : 0.5;
                    p = p.Degrade(amount);
                }
                else break;
            }
            return (weight, p);
        }

        private Pattern<string> ParseFactor()
        {
            char c = Peek();
            switch (c)
            {
                case '~': _i++; return Pat.Silence<string>();
                case '[': _i++; return ParseTop(closer: ']');
                case '<': _i++; return ParseAlternation();
                case '\0': throw new MiniNotationError("ステップがありません", _i);
                default: return Pat.Pure(ReadWord());
            }
        }

        /// <summary>&lt;a b c&gt; — サイクルごとに 1 ステップ (slowcat)。重みは無視 (v1)。</summary>
        private Pattern<string> ParseAlternation()
        {
            var steps = new List<Pattern<string>>();
            while (true)
            {
                char c = Peek();
                if (c == '>') { _i++; break; }
                if (c == '\0' || c == ',' || c == ']')
                    throw new MiniNotationError("'>' が閉じていません", _i);
                steps.Add(ParseTerm().P);
            }
            if (steps.Count == 0) throw new MiniNotationError("<> が空です", _i);
            return Pat.Cat(steps);
        }

        private string ReadWord()
        {
            SkipWs();
            var sb = new StringBuilder();
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] is ':' or '.' or '#' or '-' or '_'))
                sb.Append(_s[_i++]);
            if (sb.Length == 0) throw new MiniNotationError($"予期しない文字 '{_s[_i]}'", _i);
            return sb.ToString();
        }

        private Fraction ReadNumber(string ctx)
            => TryReadNumber(out Fraction f) ? f : throw new MiniNotationError($"{ctx}に数値が必要です", _i);

        /// <summary>整数/小数を有理数として読む (1.5 → 3/2 — float を経由しない)。</summary>
        private bool TryReadNumber(out Fraction f)
        {
            int start = _i;
            long num = 0; bool any = false;
            while (_i < _s.Length && char.IsAsciiDigit(_s[_i])) { num = num * 10 + (_s[_i++] - '0'); any = true; }
            long den = 1;
            if (_i < _s.Length && _s[_i] == '.')
            {
                _i++;
                while (_i < _s.Length && char.IsAsciiDigit(_s[_i]))
                {
                    num = num * 10 + (_s[_i++] - '0');
                    den *= 10;
                    any = true;
                }
            }
            if (!any) { _i = start; f = Fraction.Zero; return false; }
            f = new Fraction(num, den);
            return true;
        }
    }
}
