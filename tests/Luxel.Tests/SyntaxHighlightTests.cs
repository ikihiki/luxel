using Luxel.Document;
using Luxel.Highlight;
using Xunit;

namespace Luxel.Tests;

/// <summary>SH: TextMate トークナイザ (scope → TokenKind 写像) と HighlightQueue。</summary>
public class SyntaxHighlightTests
{
    [Fact]
    public void CSharp_TokenizesKeywordsStringsCommentsNumbers()
    {
        var hl = TextMateHighlighter.Instance;
        Assert.True(hl.Supports("csharp"));
        Assert.True(hl.Supports("cs"));
        Assert.False(hl.Supports("nope"));

        const string code = "// note\nvar x = \"hi\" + 42;";
        SyntaxToken[] toks = hl.Tokenize("csharp", code);
        Assert.NotEmpty(toks);

        string Of(SyntaxToken t) => code.Substring(t.Start, t.Length);
        Assert.Contains(toks, t => t.Kind == TokenKind.Comment && Of(t).Contains("note"));
        Assert.Contains(toks, t => t.Kind == TokenKind.Keyword && Of(t) == "var");
        Assert.Contains(toks, t => t.Kind == TokenKind.String && Of(t).Contains("hi"));
        Assert.Contains(toks, t => t.Kind == TokenKind.Number && Of(t) == "42");
    }

    [Fact]
    public void CSharp_FineGrained_ControlFunctionVariableConstant()
    {
        // VS Code 相当の細分類 (SH2): 制御キーワード/関数名/変数/言語定数/エスケープ
        var hl = TextMateHighlighter.Instance;
        const string code = "if (ok) Foo(x, true);\nvar s = \"a\\n\";";
        SyntaxToken[] toks = hl.Tokenize("csharp", code);
        string Of(SyntaxToken t) => code.Substring(t.Start, t.Length);
        Assert.Contains(toks, t => t.Kind == TokenKind.KeywordControl && Of(t) == "if");
        Assert.Contains(toks, t => t.Kind == TokenKind.Function && Of(t) == "Foo");
        Assert.Contains(toks, t => t.Kind == TokenKind.Constant && Of(t) == "true");
        Assert.Contains(toks, t => t.Kind == TokenKind.Variable);
        Assert.Contains(toks, t => t.Kind == TokenKind.Escape && Of(t) == "\\n");
    }

    [Fact]
    public void Xml_TagsAndAttributes()
    {
        var hl = TextMateHighlighter.Instance;
        SyntaxToken[] toks = hl.Tokenize("xml", "<node attr=\"v\" />");
        Assert.Contains(toks, t => t.Kind == TokenKind.Tag);
        Assert.Contains(toks, t => t.Kind == TokenKind.Attribute);
    }

    [Fact]
    public void MultiLine_BlockComment_SpansLines()
    {
        var hl = TextMateHighlighter.Instance;
        const string code = "int a;\n/* b\nc */\nint d;";
        SyntaxToken[] toks = hl.Tokenize("csharp", code);
        // 2 行目と 3 行目がコメント (行単位トークンなので複数トークンに分かれる)
        Assert.Contains(toks, t => t.Kind == TokenKind.Comment && code.Substring(t.Start, t.Length).Contains('b'));
        Assert.Contains(toks, t => t.Kind == TokenKind.Comment && code.Substring(t.Start, t.Length).Contains('c'));
    }

    [Fact]
    public void Json_TokenizesStringsAndNumbers()
    {
        var hl = TextMateHighlighter.Instance;
        SyntaxToken[] toks = hl.Tokenize("json", "{ \"name\": \"x\", \"n\": 12 }");
        Assert.Contains(toks, t => t.Kind == TokenKind.String);
        Assert.Contains(toks, t => t.Kind == TokenKind.Number);
    }

    [Fact]
    public void HighlightQueue_RunsJobOnWorker_AndWaitIdle()
    {
        int uiThread = Environment.CurrentManagedThreadId;
        int jobThread = 0;
        Luxel.Controls.HighlightQueue.Enqueue(() => jobThread = Environment.CurrentManagedThreadId);
        Assert.True(Luxel.Controls.HighlightQueue.WaitIdle(5000));
        Assert.NotEqual(0, jobThread);
        Assert.NotEqual(uiThread, jobThread);   // 別スレッドで実行された
    }
}
