using System.Text.Json;
using Luxel.DevTools;
using Luxel.Diagnostics;
using Luxel.Ecs;

namespace Luxel.Tests;

/// <summary>
/// DevTools ゲーム規模対応のGPU不要テスト:
/// ECSサマリ/詳細/フィルタとDevStatsカスタム統計を検証する。
/// </summary>
[Collection("GlobalDiagnostics")]
public class DevToolsGameScaleTests
{
    // ==================== A: ECS スケール対応 ====================

    [Fact]
    public void EcsSummary_HasNamesAndArchetype_NoValues()
    {
        using var world = new World();
        world.CreateEntity(new LocalTransform(System.Numerics.Matrix4x4.Identity));           // 名前なし
        var named = world.CreateEntity(new LocalTransform(System.Numerics.Matrix4x4.Identity));
        named.AddComponent(new DebugName("Player"));

        var sum = EcsDiagnostics.BuildSummary(new[] { world }, filter: null);
        Assert.Single(sum.Worlds);
        DiagWorldSummary w = sum.Worlds[0];
        Assert.Equal(2, w.EntityCount);
        Assert.Equal(2, w.ShownCount);

        DiagEntitySummary player = Assert.Single(w.Entities, e => e.Name == "Player");
        Assert.Contains("DebugName", player.Archetype);
        Assert.Contains("LocalTransform", player.Archetype);

        // 名前なし entity は空文字 (UI が Id 表示)
        Assert.Contains(w.Entities, e => e.Name == "");
        // サマリは値 (JSON) を含まない — 型に値フィールドが無いことで担保 (コンパイル時保証)
    }

    [Fact]
    public void EcsSummary_Filter_KeepsOnlyMatching_AndReportsTruncation()
    {
        using var world = new World();
        var enemy = world.CreateEntity(new LocalTransform(System.Numerics.Matrix4x4.Identity));
        enemy.AddComponent(new DebugName("Enemy1"));
        var bullet = world.CreateEntity(new Color3D(System.Numerics.Vector4.One));
        bullet.AddComponent(new DebugName("Bullet"));

        var sum = EcsDiagnostics.BuildSummary(new[] { world }, filter: "Enemy");
        DiagWorldSummary w = sum.Worlds[0];
        Assert.Equal(2, w.EntityCount);       // 総数は全件
        Assert.Equal(1, w.ShownCount);        // 一覧は絞られた件数 (truncation を示せる)
        Assert.Equal("Enemy1", Assert.Single(w.Entities).Name);
    }

    [Theory]
    [InlineData("Player", 3, new[] { "LocalTransform" }, "play", true)]    // 名前 (大小無視)
    [InlineData("Player", 3, new[] { "LocalTransform" }, "Transform", true)] // component 型名
    [InlineData("Player", 42, new[] { "LocalTransform" }, "42", true)]     // Id 文字列
    [InlineData("Player", 3, new[] { "LocalTransform" }, "Enemy", false)]  // 不一致
    [InlineData("Player", 3, new[] { "LocalTransform" }, "", true)]        // 空 = 全件
    [InlineData("Player", 3, new[] { "LocalTransform" }, null, true)]      // null = 全件
    public void EcsFilterMatch(string name, int id, string[] arch, string? filter, bool expected)
        => Assert.Equal(expected, EcsDiagnostics.FilterMatch(name, id, arch, filter));

    [Fact]
    public void EcsDetail_Selection_YieldsSingleEntity()
    {
        using var world = new World();
        var a = world.CreateEntity(new LocalTransform(System.Numerics.Matrix4x4.Identity));
        var b = world.CreateEntity(new LocalTransform(System.Numerics.Matrix4x4.Identity));

        var detail = EcsDiagnostics.BuildDetail(new[] { world }, selWorld: 0, selEntity: b.Id);
        DiagWorld w = Assert.Single(detail.Worlds);
        DiagEntity only = Assert.Single(w.Entities);
        Assert.Equal(b.Id, only.Id);
        Assert.NotEmpty(only.Components);       // 詳細は値付き JSON を含む
        Assert.Contains(only.Components, c => c.Type == "LocalTransform" && c.Json.Contains("M11"));
    }

    [Fact]
    public void EcsDetail_NoSelection_SmallWorld_FullFallback()
    {
        using var world = new World();
        for (int i = 0; i < 5; i++) world.CreateEntity(new LocalTransform(System.Numerics.Matrix4x4.Identity));

        var detail = EcsDiagnostics.BuildDetail(new[] { world }, selWorld: -1, selEntity: -1);
        Assert.Equal(5, detail.Worlds[0].Entities.Length);   // 閾値以下は全量
    }

    [Fact]
    public void EcsDetail_NoSelection_LargeWorld_IsEmpty()
    {
        using var world = new World();
        for (int i = 0; i < EcsDiagnostics.FullFallbackThreshold + 10; i++)
            world.CreateEntity(new LocalTransform(System.Numerics.Matrix4x4.Identity));

        var detail = EcsDiagnostics.BuildDetail(new[] { world }, selWorld: -1, selEntity: -1);
        Assert.Empty(detail.Worlds[0].Entities);             // 大規模 & 未選択は空 (要選択)
        Assert.Equal(EcsDiagnostics.FullFallbackThreshold + 10, detail.Worlds[0].EntityCount);
    }

    // ==================== C: DevStats (ゲーム統計) ====================

    [Fact]
    public void DevStats_Set_Flush_EmitsSortedKeyValues()
    {
        DevStats.Clear();
        var cmds = new EngineCommands();
        using var listener = new DevToolsListener(cmds);   // 購読者を作ると IsEnabled(Custom)=true

        DevStats.Set("score", 1200);
        DevStats.Set("state", "Playing");
        DevStats.Set("hp", 3.5);
        DevStats.Set("alive", true);
        DevStats.Flush();

        string? json = listener.GetCustom();
        Assert.NotNull(json);
        using JsonDocument doc = JsonDocument.Parse(json!);
        JsonElement stats = doc.RootElement.GetProperty("stats");
        var map = new Dictionary<string, string>();
        foreach (JsonElement s in stats.EnumerateArray())
            map[s.GetProperty("key").GetString()!] = s.GetProperty("value").GetString()!;

        Assert.Equal("1200", map["score"]);
        Assert.Equal("Playing", map["state"]);
        Assert.Equal("3.5", map["hp"]);
        Assert.Equal("true", map["alive"]);

        // key 昇順で決定的
        var keys = new List<string>();
        foreach (JsonElement s in stats.EnumerateArray()) keys.Add(s.GetProperty("key").GetString()!);
        var sorted = new List<string>(keys);
        sorted.Sort(StringComparer.Ordinal);
        Assert.Equal(sorted, keys);
        DevStats.Clear();
    }

    [Fact]
    public void DevStats_Set_LatestValueWins()
    {
        DevStats.Clear();
        using var listener = new DevToolsListener(new EngineCommands());
        DevStats.Set("score", 1);
        DevStats.Set("score", 2);
        var stats = DevStats.Snapshot();
        Assert.Equal("2", Assert.Single(stats).Value);
        DevStats.Clear();
    }

}
