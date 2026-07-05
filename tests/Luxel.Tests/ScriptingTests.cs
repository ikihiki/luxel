using Luxel.Scripting;

namespace Luxel.Tests;

/// <summary>Luxel.Scripting (P1): コンパイル/実行/診断行マップ/例外行/キャッシュ。GPU 不要。</summary>
public class ScriptingTests
{
    /// <summary>スクリプトから裸で見えるメンバー (csx の globals 意味論)。</summary>
    public class TestGlobals
    {
        public int X { get; set; } = 10;
        public List<string> Logged { get; } = new();
        public void Log(string s) => Logged.Add(s);
    }

    private static ScriptHost NewHost() => new(
        references: [typeof(object).Assembly, typeof(Enumerable).Assembly, typeof(ScriptingTests).Assembly],
        usings: ["System", "System.Linq", "System.Collections.Generic"],
        globalsType: typeof(TestGlobals));

    private static void AssertSuccess(ScriptResult r)
        => Assert.True(r.Success,
            $"diags: {string.Join(" | ", r.Diagnostics.Select(d => $"{d.Line}:{d.Column} {d.Message}"))} ex: {r.Exception}");

    [Fact]
    public void Run_ReturnsLastExpression()
    {
        ScriptResult r = NewHost().Run("1 + 2", new TestGlobals());
        AssertSuccess(r);
        Assert.Equal(3, r.ReturnValue);
    }

    [Fact]
    public void Globals_AreVisibleUnqualified()
    {
        var g = new TestGlobals { X = 7 };
        ScriptResult r = NewHost().Run("""
            Log($"x = {X}");
            X * 2
            """, g);
        Assert.True(r.Success);
        Assert.Equal(14, r.ReturnValue);
        Assert.Equal(["x = 7"], g.Logged);
    }

    [Fact]
    public void CompileError_MapsLineAndColumn()
    {
        ScriptResult r = NewHost().Run("var a = 1;\nvar b = a +;\nb", new TestGlobals());
        Assert.False(r.Success);
        ScriptDiagnostic d = r.Diagnostics.First(x => x.IsError);
        Assert.Equal(2, d.Line);          // 2 行目の構文エラー
        Assert.Null(r.Exception);          // コンパイル失敗 — 実行していない
    }

    [Fact]
    public void RuntimeException_ExtractsScriptLine()
    {
        ScriptResult r = NewHost().Run("""
            var ok = 1;
            Log("before");
            throw new InvalidOperationException("boom");
            """, new TestGlobals());
        Assert.False(r.Success);
        Assert.IsType<InvalidOperationException>(r.Exception);
        Assert.Equal(3, r.ExceptionLine);
    }

    [Fact]
    public void Check_ReportsWithoutRunning()
    {
        var g = new TestGlobals();
        var host = NewHost();
        var diags = host.Check("""Log("side effect"); var x = """);
        Assert.Contains(diags, d => d.IsError);
        Assert.Empty(g.Logged);            // Check は実行しない
    }

    [Fact]
    public void SameSource_IsCompiledOnce()
    {
        var host = NewHost();
        _ = host.Run("40 + 2", new TestGlobals());
        _ = host.Run("40 + 2", new TestGlobals());
        Assert.Equal(1, host.CachedScripts);
        _ = host.Run("40 + 3", new TestGlobals());
        Assert.Equal(2, host.CachedScripts);
    }

    [Fact]
    public void CollectionsAndLinq_Work()
    {
        ScriptResult r = NewHost().Run(
            "Enumerable.Range(1, 5).Where(i => i % 2 == 1).Sum()", new TestGlobals());
        Assert.True(r.Success);
        Assert.Equal(9, r.ReturnValue);
    }

    // ---- 継続 REPL セッション (P3) ----

    [Fact]
    public void Session_KeepsStateAcrossSubmits()
    {
        ScriptSession s = NewHost().OpenSession(new TestGlobals());
        Assert.True(s.Submit("var a = 21;").Success);
        ScriptResult r = s.Submit("a * 2");                 // 前の行の変数が見える
        Assert.True(r.Success);
        Assert.Equal(42, r.ReturnValue);
    }

    [Fact]
    public void Session_GlobalsVisible()
    {
        var g = new TestGlobals { X = 5 };
        ScriptSession s = NewHost().OpenSession(g);
        s.Submit("Log($\"x={X}\");");
        Assert.Equal(["x=5"], g.Logged);
    }

    [Fact]
    public void Session_SurvivesError_AndContinues()
    {
        ScriptSession s = NewHost().OpenSession(new TestGlobals());
        s.Submit("var n = 10;");
        Assert.False(s.Submit("n +").Success);              // 構文エラー — セッションは壊れない
        ScriptResult r = s.Submit("n + 1");                 // 直前の成功状態から続く
        Assert.True(r.Success);
        Assert.Equal(11, r.ReturnValue);
    }
}
