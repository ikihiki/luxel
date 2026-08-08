using Luxel.Controls;
using Luxel.Strudel;

namespace Luxel.Gallery.Stories;

/// <summary>
/// <see cref="ICodeLanguage"/> の Strudel 実装 — Strudel 式を <see cref="StrudelEval"/> でパース/評価し、
/// <see cref="StrudelEvalError"/> (MiniNotation の位置付きエラーもここへ畳まれる) を診断波線に写す。
/// <para>診断は<b>音を出さない</b> — <see cref="StrudelEval.Evaluate"/> はパターンを組むだけで
/// スケジューラへは載せない (再生は <see cref="StrudelStory"/> 側の Run が担う)。補完は音色名/関数の
/// 静的リスト (文脈は「クォート内=音色 / <c>.</c> の直後=メソッド / それ以外=関数」で切替)。</para>
/// </summary>
public sealed class StrudelCodeLanguage : ICodeLanguage
{
    /// <summary>共有インスタンス (状態を持たない)。</summary>
    public static readonly StrudelCodeLanguage Instance = new();

    // StrudelKit のバンク音色 + オシレータ (クォート内で提案)。
    private static readonly string[] Sounds =
        ["bd", "sd", "hh", "oh", "cp", "rim", "lt", "mt", "ht", "sine", "tri", "saw", "square"];
    // トップレベル関数 (StrudelEval.ApplyGlobal が受ける語彙)。
    private static readonly string[] Functions =
        ["s", "sound", "note", "n", "stack", "cat", "silence", "cps", "rev", "degrade", "fast", "slow", "early", "late"];
    // チェーンメソッド (StrudelEval.ApplyMethod が受ける語彙)。
    private static readonly string[] Methods =
        ["fast", "slow", "early", "late", "rev", "iter", "degrade", "every", "off", "jux", "gain", "pan", "speed", "s", "sound", "note"];

    public IReadOnlyList<CodeDiagnostic> Diagnose(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return [];
        try
        {
            StrudelEval.Evaluate(code);   // パターンを組むだけ — 音は出ない
            return [];
        }
        catch (StrudelEvalError e)
        {
            (int line, int col) = LineCol(code, e.Position);
            return [new CodeDiagnostic(line, col, LenToLineEnd(code, e.Position), e.Message, true)];
        }
        catch (Exception e)   // 保険: 未知例外もエディタに出す (クラッシュさせない)
        {
            return [new CodeDiagnostic(1, 1, 1, e.Message, true)];
        }
    }

    public IReadOnlyList<CodeCompletion> Complete(string code, int position)
    {
        int pos = Math.Clamp(position, 0, code.Length);
        string[] pool =
            InString(code, pos) ? Sounds :
            AfterDot(code, pos) ? Methods :
            Functions;
        var outp = new List<CodeCompletion>(pool.Length);
        string kind = ReferenceEquals(pool, Sounds) ? "sound" : ReferenceEquals(pool, Methods) ? "method" : "function";
        foreach (string s in pool) outp.Add(new CodeCompletion(s, s, kind));
        return outp;
    }

    public string? Hover(string code, int position) => null;

    // ---- 文脈判定 ----

    // pos より前に閉じていない " があればミニ記法文字列の中。
    private static bool InString(string code, int pos)
    {
        int quotes = 0;
        for (int i = 0; i < pos; i++) if (code[i] == '"') quotes++;
        return (quotes & 1) == 1;
    }

    // 識別子接頭辞を飛ばした直前の非空白が '.' ならメソッド文脈。
    private static bool AfterDot(string code, int pos)
    {
        int i = pos - 1;
        while (i >= 0 && (char.IsLetterOrDigit(code[i]) || code[i] == '_')) i--;
        while (i >= 0 && code[i] == ' ') i--;
        return i >= 0 && code[i] == '.';
    }

    // ---- 位置換算 (フラットオフセット → 1 始まり行/桁 + その行末までの長さ) ----

    private static (int Line, int Col) LineCol(string code, int pos)
    {
        int p = Math.Clamp(pos, 0, code.Length);
        int line = 1, col = 1;
        for (int i = 0; i < p; i++)
        {
            if (code[i] == '\n') { line++; col = 1; } else col++;
        }
        return (line, col);
    }

    private static int LenToLineEnd(string code, int pos)
    {
        int p = Math.Clamp(pos, 0, code.Length);
        int end = code.IndexOf('\n', p);
        if (end < 0) end = code.Length;
        return Math.Max(1, end - p);
    }
}
