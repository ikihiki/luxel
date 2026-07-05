using Luxel.Scripting;

namespace Luxel.Tests;

/// <summary>Luxel.Scripting P2 言語サービス: 補完/ホバー (in-proc Roslyn)。GPU 不要。</summary>
public class ScriptWorkspaceTests
{
    private static ScriptWorkspace NewWs() => new(
        references: [typeof(object).Assembly, typeof(Enumerable).Assembly, typeof(System.Text.StringBuilder).Assembly],
        usings: ["System", "System.Linq", "System.Text"]);

    [Fact]
    public void Complete_AfterDot_ListsMembers()
    {
        using var ws = NewWs();
        const string code = "\"hello\".";
        var items = ws.Complete(code, code.Length);   // 文字列のメンバー
        Assert.Contains(items, i => i.Label == "Length");
        Assert.Contains(items, i => i.Label == "ToUpper");
    }

    [Fact]
    public void Complete_TypeName_FromUsing()
    {
        using var ws = NewWs();
        const string code = "var sb = new StringB";
        var items = ws.Complete(code, code.Length);   // using System.Text; が効く
        Assert.Contains(items, i => i.Label == "StringBuilder");
    }

    [Fact]
    public void Complete_LinqExtension_OnEnumerable()
    {
        using var ws = NewWs();
        const string code = "Enumerable.Range(1, 3).Su";
        var items = ws.Complete(code, code.Length);   // 拡張メソッドが候補に出る
        Assert.Contains(items, i => i.Label == "Sum" && i.Kind == "ExtensionMethod");
    }

    [Fact]
    public void Complete_Empty_HasKeywords()
    {
        using var ws = NewWs();
        var items = ws.Complete("va", 2);
        Assert.Contains(items, i => i.Label == "var");
    }

    [Fact]
    public void Hover_OnMethod_ShowsSignature()
    {
        using var ws = NewWs();
        const string code = "\"hi\".ToUpper()";
        HoverInfo? info = ws.Hover(code, code.IndexOf("ToUpper", StringComparison.Ordinal) + 1);
        Assert.NotNull(info);
        Assert.Contains("ToUpper", info!.Text);
        Assert.Contains("string", info.Text);       // 戻り型 string が出る
    }

    [Fact]
    public void Hover_OnUnknown_ReturnsNullOrEmpty()
    {
        using var ws = NewWs();
        HoverInfo? info = ws.Hover("   ", 1);        // 空白位置 — シンボルなし
        Assert.Null(info);
    }

    [Fact]
    public void Reused_AcrossCalls()
    {
        using var ws = NewWs();
        Assert.Contains(ws.Complete("\"a\".", 4), i => i.Label == "Length");
        Assert.Contains(ws.Complete("Enumerable.Ra", 13), i => i.Label == "Range");
        Assert.Contains(ws.Complete("\"b\".", 4), i => i.Label == "Substring");   // 同一 ws で再利用
    }
}
