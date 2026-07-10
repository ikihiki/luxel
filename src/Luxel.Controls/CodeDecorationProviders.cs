using Luxel.Editor;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>
/// コードエディタ用の装飾プロバイダ群 — 新スタック (ADR-0006 / ToDo 22) の <see cref="IDecorationProvider"/> として、
/// 既存の <see cref="ISyntaxHighlighter"/> / <see cref="ICodeLanguage"/> を <see cref="Decoration"/> に橋渡しする。
/// <see cref="TextEditorView.Providers"/> に足すだけで色分け・波線・現在行が付く。
/// </summary>
public static class CodeDecorations
{
    /// <summary>トークン種別 → テーマ色 (VS Code の色分け粒度)。</summary>
    public static uint TokenColor(Theme t, TokenKind k) => k switch
    {
        TokenKind.Comment => t.TokComment,
        TokenKind.String => t.TokString,
        TokenKind.Escape => t.TokEscape,
        TokenKind.Regexp => t.TokRegexp,
        TokenKind.Number => t.TokNumber,
        TokenKind.Constant => t.TokConstant,
        TokenKind.Keyword => t.TokKeyword,
        TokenKind.KeywordControl => t.TokKeywordControl,
        TokenKind.Operator => t.TokOperator,
        TokenKind.Function => t.TokFunction,
        TokenKind.Type => t.TokType,
        TokenKind.Variable => t.TokVariable,
        TokenKind.Tag => t.TokTag,
        TokenKind.Attribute => t.TokAttribute,
        _ => t.Text,
    };
}

/// <summary>シンタックスハイライト — コード全文を <see cref="ISyntaxHighlighter"/> でトークン化し、
/// トークンごとに <see cref="MarkDecoration.Foreground"/> を付ける (要件: 行内の色分け)。同期。
/// テキストとテーマ (代表色) が変わらない限りキャッシュを返す (選択移動での再トークン化を避ける)。</summary>
public sealed class SyntaxHighlightProvider(ISyntaxHighlighter highlighter, string language, Func<Theme> theme) : IDecorationProvider
{
    private string? _lastText;
    private uint _lastDisc;
    private DecorationSet _cache = DecorationSet.Empty;

    /// <inheritdoc/>
    public string Owner => "syntax";

    /// <inheritdoc/>
    public DecorationSet Provide(EditorState state)
    {
        Theme t = theme();
        uint disc = CodeDecorations.TokenColor(t, TokenKind.Keyword);   // テーマ変化の検出子
        string text = state.Doc.Text;
        if (text == _lastText && disc == _lastDisc) return _cache;

        var marks = new List<Decoration>();
        try
        {
            foreach (SyntaxToken tk in highlighter.Tokenize(language, text))
            {
                if (tk.Kind == TokenKind.Text || tk.Length <= 0) continue;
                marks.Add(new MarkDecoration(tk.Start, tk.Start + tk.Length, Foreground: CodeDecorations.TokenColor(t, tk.Kind)));
            }
        }
        catch { /* トークナイザ失敗 → 無色 */ }

        _cache = new DecorationSet(marks);
        _lastText = text;
        _lastDisc = disc;
        return _cache;
    }
}

/// <summary>診断波線 — <see cref="ICodeLanguage.Diagnose"/> の結果を <see cref="MarkDecoration.Underline"/>
/// (波線、エラー=Danger / 警告=Warning) に写す (要件: 文字列に下線・波線)。同期・テキストでキャッシュ。</summary>
public sealed class DiagnosticsProvider(ICodeLanguage language, Func<Theme> theme) : IDecorationProvider
{
    private string? _lastText;
    private DecorationSet _cache = DecorationSet.Empty;

    /// <inheritdoc/>
    public string Owner => "diagnostics";

    /// <summary>直近の診断数 (テスト/上位の観測用)。</summary>
    public int Count { get; private set; }

    /// <inheritdoc/>
    public DecorationSet Provide(EditorState state)
    {
        string text = state.Doc.Text;
        if (text == _lastText) return _cache;

        Theme t = theme();
        TextDoc doc = state.Doc;
        var marks = new List<Decoration>();
        foreach (CodeDiagnostic dg in language.Diagnose(text))
        {
            int line = Math.Clamp(dg.Line - 1, 0, doc.LineCount - 1);
            int from = doc.OffsetAt(line, dg.Column - 1);
            int to = Math.Min(from + Math.Max(1, dg.Length), doc.LineEnd(line));
            marks.Add(new MarkDecoration(from, to, Underline: new UnderlineStyle(dg.IsError ? t.Danger : t.Warning, Wavy: true)));
        }
        Count = marks.Count;
        _cache = new DecorationSet(marks);
        _lastText = text;
        return _cache;
    }
}

/// <summary>現在行ハイライト — 主キャレットの行に <see cref="LineDecoration"/> を付ける。選択移動で更新される。</summary>
public sealed class CurrentLineProvider(Func<Theme> theme) : IDecorationProvider
{
    /// <inheritdoc/>
    public string Owner => "current-line";

    /// <inheritdoc/>
    public DecorationSet Provide(EditorState state)
    {
        int caret = state.Selection.Main.Head;
        uint bg = Styles.WithAlpha(theme().Primary, 20);
        return new DecorationSet([new LineDecoration(caret, bg)]);
    }
}
