using System.Text;

namespace Luxel.Document;

/// <summary>
/// インライン数式 (<c>$...$</c>) の TeX サブセット → Unicode 正規化。
/// ギリシャ文字・演算子コマンドの置換と、単一トークンの <c>^</c>/<c>_</c> を上付き/下付き文字へ。
/// **パース時に一度だけ正規化して文書へ焼き込む** (emoji/SmartyPants と同じ方針 —
/// 表示・検索・シリアライズのオフセットが常に一致し、round-trip は 1 回で収束する)。
/// 変換できないコマンドはそのまま残す (豆腐にしない)。本格レイアウト (分数/行列) は
/// ブロック数式 <c>$$...$$</c> = Luxel.MathText 側の担当。
/// </summary>
public static class TexText
{
    private static readonly Dictionary<string, string> Commands = new(StringComparer.Ordinal)
    {
        // ギリシャ文字 (小文字 + よく使う大文字)
        ["alpha"] = "α",
        ["beta"] = "β",
        ["gamma"] = "γ",
        ["delta"] = "δ",
        ["epsilon"] = "ε",
        ["zeta"] = "ζ",
        ["eta"] = "η",
        ["theta"] = "θ",
        ["iota"] = "ι",
        ["kappa"] = "κ",
        ["lambda"] = "λ",
        ["mu"] = "μ",
        ["nu"] = "ν",
        ["xi"] = "ξ",
        ["pi"] = "π",
        ["rho"] = "ρ",
        ["sigma"] = "σ",
        ["tau"] = "τ",
        ["upsilon"] = "υ",
        ["phi"] = "φ",
        ["chi"] = "χ",
        ["psi"] = "ψ",
        ["omega"] = "ω",
        ["Gamma"] = "Γ",
        ["Delta"] = "Δ",
        ["Theta"] = "Θ",
        ["Lambda"] = "Λ",
        ["Xi"] = "Ξ",
        ["Pi"] = "Π",
        ["Sigma"] = "Σ",
        ["Phi"] = "Φ",
        ["Psi"] = "Ψ",
        ["Omega"] = "Ω",
        // 演算子/記号
        ["cdot"] = "⋅",
        ["times"] = "×",
        ["pm"] = "±",
        ["mp"] = "∓",
        ["div"] = "÷",
        ["le"] = "≤",
        ["leq"] = "≤",
        ["ge"] = "≥",
        ["geq"] = "≥",
        ["ne"] = "≠",
        ["neq"] = "≠",
        ["approx"] = "≈",
        ["equiv"] = "≡",
        ["infty"] = "∞",
        ["partial"] = "∂",
        ["nabla"] = "∇",
        ["sum"] = "∑",
        ["prod"] = "∏",
        ["int"] = "∫",
        ["sqrt"] = "√",
        ["propto"] = "∝",
        ["in"] = "∈",
        ["notin"] = "∉",
        ["subset"] = "⊂",
        ["cup"] = "∪",
        ["cap"] = "∩",
        ["rightarrow"] = "→",
        ["to"] = "→",
        ["leftarrow"] = "←",
        ["Rightarrow"] = "⇒",
        ["dots"] = "…",
        ["cdots"] = "⋯",
        ["circ"] = "∘",
        // 関数名 (立体のままの単語)
        ["sin"] = "sin",
        ["cos"] = "cos",
        ["tan"] = "tan",
        ["log"] = "log",
        ["ln"] = "ln",
        ["exp"] = "exp",
        ["min"] = "min",
        ["max"] = "max",
    };

    private static readonly Dictionary<char, char> Sup = new()
    {
        ['0'] = '⁰',
        ['1'] = '¹',
        ['2'] = '²',
        ['3'] = '³',
        ['4'] = '⁴',
        ['5'] = '⁵',
        ['6'] = '⁶',
        ['7'] = '⁷',
        ['8'] = '⁸',
        ['9'] = '⁹',
        ['+'] = '⁺',
        ['-'] = '⁻',
        ['='] = '⁼',
        ['('] = '⁽',
        [')'] = '⁾',
        ['n'] = 'ⁿ',
        ['i'] = 'ⁱ',
        ['T'] = 'ᵀ',
    };

    private static readonly Dictionary<char, char> Sub = new()
    {
        ['0'] = '₀',
        ['1'] = '₁',
        ['2'] = '₂',
        ['3'] = '₃',
        ['4'] = '₄',
        ['5'] = '₅',
        ['6'] = '₆',
        ['7'] = '₇',
        ['8'] = '₈',
        ['9'] = '₉',
        ['+'] = '₊',
        ['-'] = '₋',
        ['='] = '₌',
        ['('] = '₍',
        [')'] = '₎',
        ['i'] = 'ᵢ',
        ['j'] = 'ⱼ',
        ['n'] = 'ₙ',
        ['x'] = 'ₓ',
        ['k'] = 'ₖ',
        ['m'] = 'ₘ',
    };

    /// <summary>TeX サブセットを Unicode へ正規化する。</summary>
    public static string ToUnicode(string tex)
    {
        string s = tex ?? "";
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\')
            {
                int j = i + 1;
                while (j < s.Length && char.IsAsciiLetter(s[j])) j++;
                string cmd = s[(i + 1)..j];
                if (Commands.TryGetValue(cmd, out string? rep)) { sb.Append(rep); i = j - 1; }
                else { sb.Append(c); }   // 未知コマンドは \ ごと残す
                continue;
            }
            if (c is '^' or '_' && i + 1 < s.Length)
            {
                Dictionary<char, char> map = c == '^' ? Sup : Sub;
                // ^{...} は中身全部、^x は 1 文字 — 全文字が変換できるときだけ置換 (半端は原文のまま)
                (string body, int next) = s[i + 1] == '{' ? ReadGroup(s, i + 1) : (s[i + 1].ToString(), i + 2);
                if (body.Length > 0 && body.All(map.ContainsKey))
                {
                    foreach (char bc in body) sb.Append(map[bc]);
                    i = next - 1;
                    continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static (string Body, int Next) ReadGroup(string s, int braceAt)
    {
        int depth = 0;
        for (int i = braceAt; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0)
                return (s[(braceAt + 1)..i], i + 1);
        }
        return ("", braceAt + 1);
    }
}
