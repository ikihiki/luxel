using Luxel.Controls;
using Luxel.Document;
using Luxel.Editor;
using Luxel.UI;

namespace Luxel.Tests;

/// <summary>エディタ新スタック S6 (ToDo 22) — コード装飾プロバイダの単体テスト (canvas 不要)。
/// syntax → Mark.Foreground、診断 → Mark.Underline (行/桁→オフセット変換)、現在行 → LineDecoration。</summary>
public class EditorProviderTests
{
    private static Theme T => UiTheme.Current.Peek();

    private sealed class FakeHighlighter : ISyntaxHighlighter
    {
        public bool Supports(string lang) => true;
        public SyntaxToken[] Tokenize(string lang, string code) =>
            [new SyntaxToken(0, 6, TokenKind.Keyword), new SyntaxToken(7, 1, TokenKind.Variable)];
    }

    private sealed class FakeLang(CodeDiagnostic[] diags) : ICodeLanguage
    {
        public IReadOnlyList<CodeCompletion> Complete(string code, int position) => [];
        public IReadOnlyList<CodeDiagnostic> Diagnose(string code) => diags;
        public string? Hover(string code, int position) => null;
    }

    [Fact]
    public void Syntax_EmitsForegroundMarks()
    {
        var p = new SyntaxHighlightProvider(new FakeHighlighter(), "csharp", () => T);
        DecorationSet set = p.Provide(EditorState.Create("public x"));
        Assert.Equal(2, set.Count);
        var m0 = (MarkDecoration)set.Decorations[0];
        Assert.Equal(0, m0.From);
        Assert.Equal(6, m0.To);
        Assert.Equal(CodeDecorations.TokenColor(T, TokenKind.Keyword), m0.Foreground);
        Assert.True(m0.AffectsLayout);   // 前景色はレイアウトに効く
    }

    [Fact]
    public void Syntax_CachesByText()
    {
        var p = new SyntaxHighlightProvider(new FakeHighlighter(), "csharp", () => T);
        var s = EditorState.Create("public x");
        DecorationSet a = p.Provide(s);
        DecorationSet b = p.Provide(s);
        Assert.Same(a, b);               // テキスト不変ならキャッシュ (選択移動で再トークン化しない)
    }

    [Fact]
    public void Diagnostics_MapsLineColumnToOffset()
    {
        // "ab\ncdefg": 2 行目 "cdefg" は offset 3 開始。診断 (Line 2, Col 5, Len 3) → offset 7..8 (行末クランプ)
        var lang = new FakeLang([new CodeDiagnostic(2, 5, 3, "err", IsError: true)]);
        var p = new DiagnosticsProvider(lang, () => T);
        DecorationSet set = p.Provide(EditorState.Create("ab\ncdefg"));
        Assert.Equal(1, set.Count);
        var m = (MarkDecoration)set.Decorations[0];
        Assert.Equal(7, m.From);
        Assert.Equal(8, m.To);           // 7+3=10 だが行末 8 にクランプ
        Assert.NotNull(m.Underline);
        Assert.True(m.Underline!.Value.Wavy);
        Assert.False(m.AffectsLayout);   // 波線はオーバーレイのみ
        Assert.Equal(1, p.Count);
    }

    [Fact]
    public void CurrentLine_TracksCaret()
    {
        var p = new CurrentLineProvider(() => T);
        var s = EditorState.Create("ab\ncd", EditorSelection.Cursor(4));   // 2 行目
        DecorationSet set = p.Provide(s);
        var ld = (LineDecoration)set.Decorations[0];
        Assert.Equal(4, ld.At);
        Assert.False(ld.AffectsLayout);
    }
}
