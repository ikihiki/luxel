using System.Linq;
using System.Reflection;
using Luxel.Ecs;
using Luxel.Scripting;
using Luxel.Scripting.Framework;

namespace Luxel.Tests;

/// <summary>
/// ScriptSystem (タスク 01) の GPU 不要・決定的テスト: MemoryScriptSource による hot reload —
/// system が動く / Reload 成功で挙動が変わる / Reload 失敗 (構文エラー) で旧が生き診断が出る /
/// スクリプト実行時例外を捕捉してゲームを止めない / 裸の Action 返却 / Attach でフェーズ実行。
/// </summary>
public class ScriptSystemTests
{
    private static ScriptHost NewHost() => new(
        references:
        [
            typeof(object).Assembly, typeof(Enumerable).Assembly,
            typeof(World).Assembly,                                   // Luxel.Ecs
            typeof(Friflo.Engine.ECS.Entity).Assembly,               // Friflo
            typeof(Luxel.GpuDevice).Assembly,                        // Luxel core
            typeof(ScriptGameGlobals).Assembly,                      // ブリッジ
        ],
        usings: ["System", "System.Linq", "Luxel.Ecs", "Luxel.Scripting.Framework"],
        globalsType: typeof(ScriptGameGlobals));

    private const string V1 = """Systems(update: (w, dt) => Log("v1"))""";
    private const string V2 = """Systems(update: (w, dt) => Log("v2"))""";

    [Fact]
    public void InitialScript_Runs()
    {
        var log = new List<string>();
        using var world = new World();
        var src = new MemoryScriptSource(V1);
        using var sys = new ScriptSystem(NewHost(), src, new ScriptGameGlobals(log.Add));

        Assert.True(sys.LastResult!.Success, string.Join(" | ", sys.LastResult.Diagnostics.Select(d => d.Message)));
        sys.RunUpdate(world, 1f / 60);
        Assert.Equal(["v1"], log);
    }

    [Fact]
    public void Reload_Success_SwapsBehavior()
    {
        var log = new List<string>();
        using var world = new World();
        var src = new MemoryScriptSource(V1);
        using var sys = new ScriptSystem(NewHost(), src, new ScriptGameGlobals(log.Add));

        src.Set(V2);        // Changed 発火 → dirty
        sys.PollReload();   // フレーム先頭で反映
        Assert.True(sys.LastResult!.Success);

        log.Clear();
        sys.RunUpdate(world, 1f / 60);
        Assert.Equal(["v2"], log);   // 新しい挙動
    }

    [Fact]
    public void Reload_CompileError_KeepsOld_AndExposesDiagnostics()
    {
        var log = new List<string>();
        using var world = new World();
        var src = new MemoryScriptSource(V1);
        using var sys = new ScriptSystem(NewHost(), src, new ScriptGameGlobals(log.Add));

        src.Set("""Systems(update: (w, dt) => Log("v3" +))""");   // 構文エラー
        sys.PollReload();

        Assert.False(sys.LastResult!.Success);
        Assert.True(sys.HasError);
        Assert.Contains(sys.LastResult.Diagnostics, d => d.IsError);

        log.Clear();
        sys.RunUpdate(world, 1f / 60);
        Assert.Equal(["v1"], log);   // 旧ロジックが生き続ける (ゲームを止めない)
    }

    [Fact]
    public void RuntimeException_IsCaught_DoesNotCrash()
    {
        var log = new List<string>();
        using var world = new World();
        var src = new MemoryScriptSource("""Systems(update: (w, dt) => throw new System.Exception("boom"))""");
        using var sys = new ScriptSystem(NewHost(), src, new ScriptGameGlobals(log.Add));

        Assert.True(sys.LastResult!.Success);   // コンパイルは成功 (例外はデリゲート内)
        sys.RunUpdate(world, 1f / 60);          // 例外は Guard が捕捉
        Assert.NotNull(sys.RuntimeException);
        Assert.True(sys.HasError);
        sys.RunUpdate(world, 1f / 60);          // 以後 no-op (毎フレーム例外を投げない)
    }

    [Fact]
    public void BareActionReturn_TreatedAsUpdate()
    {
        var log = new List<string>();
        using var world = new World();
        var src = new MemoryScriptSource("""(Action<World, float>)((w, dt) => Log("bare"))""");
        using var sys = new ScriptSystem(NewHost(), src, new ScriptGameGlobals(log.Add));

        Assert.True(sys.LastResult!.Success, string.Join(" | ", sys.LastResult.Diagnostics.Select(d => d.Message)));
        sys.RunUpdate(world, 1f / 60);
        Assert.Equal(["bare"], log);
    }

    [Fact]
    public void NonDescriptorReturn_KeepsEmpty_NoThrow()
    {
        var log = new List<string>();
        using var world = new World();
        var src = new MemoryScriptSource("42");   // 記述子でない
        using var sys = new ScriptSystem(NewHost(), src, new ScriptGameGlobals(log.Add));

        Assert.True(sys.LastResult!.Success);   // コンパイル/実行は成功
        sys.RunUpdate(world, 1f / 60);          // 登録デリゲート無し → no-op
        Assert.Empty(log);
    }
}
