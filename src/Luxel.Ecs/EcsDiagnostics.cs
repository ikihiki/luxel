using Friflo.Engine.ECS;
using Luxel.Diagnostics;

namespace Luxel.Ecs;

/// <summary>
/// ECS スナップショットの構築 (DevTools 向け)。ゲーム規模 (数百〜数千 entity) で全量シリアライズが
/// 破綻するため、「一覧は軽量 (<see cref="BuildSummary"/> = id + 名前 + アーキタイプ)、詳細は選択したものだけ
/// (<see cref="BuildDetail"/>)」に分ける。GPU 非依存で単体テスト可能。
/// </summary>
public static class EcsDiagnostics
{
    /// <summary>選択が無くても全量詳細を出す entity 数の上限。これ以下なら従来通り全量フォールバック。</summary>
    public const int FullFallbackThreshold = 64;

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,   // struct field も出す (Matrix4x4/Vector3/Vector4 等の public field)
        WriteIndented = false,
    };

    /// <summary>全 world の軽量サマリ (値なし)。<paramref name="filter"/> は名前/Id/component 型名の部分一致
    /// (null/空は全件)。フィルタで絞った結果だけ一覧に載せる — 二正面 UI の DOM/行数爆発を emit 側で抑える。</summary>
    public static DiagEcsSummary BuildSummary(IReadOnlyList<World> worlds, string? filter)
    {
        var ws = new DiagWorldSummary[worlds.Count];
        for (int i = 0; i < worlds.Count; i++)
        {
            var w = worlds[i];
            var list = new List<DiagEntitySummary>();
            foreach (var e in w.Store.Entities)
            {
                string name = EntityName(e);
                string[] arch = Archetype(e);
                if (!FilterMatch(name, e.Id, arch, filter)) continue;
                list.Add(new DiagEntitySummary(e.Id, name, arch));
            }
            ws[i] = new DiagWorldSummary($"World#{i}", w.Store.Count, list.Count, list.ToArray());
        }
        return new DiagEcsSummary(ws);
    }

    /// <summary>詳細スナップショット (値付き JSON)。選択 (<paramref name="selEntity"/> ≥ 0) があればその 1 entity だけ、
    /// 無ければ world ごとに entity 数が <see cref="FullFallbackThreshold"/> 以下なら全量、超過なら空 (要選択)。</summary>
    public static DiagEcs BuildDetail(IReadOnlyList<World> worlds, int selWorld, int selEntity)
    {
        bool hasSel = selEntity >= 0;
        var ws = new DiagWorld[worlds.Count];
        for (int i = 0; i < worlds.Count; i++)
        {
            var w = worlds[i];
            var entities = new List<DiagEntity>();
            if (hasSel)
            {
                if (i == selWorld)
                {
                    var e = w.Store.GetEntityById(selEntity);
                    if (!e.IsNull) entities.Add(SerializeEntity(e));
                }
            }
            else if (w.Store.Count <= FullFallbackThreshold)
            {
                foreach (var e in w.Store.Entities) entities.Add(SerializeEntity(e));
            }
            ws[i] = new DiagWorld($"World#{i}", w.Store.Count, entities.ToArray());
        }
        return new DiagEcs(ws);
    }

    /// <summary>フィルタ一致判定: 空フィルタは全件。名前 / Id 文字列 / component 型名のいずれかに部分一致 (大小無視)。</summary>
    public static bool FilterMatch(string name, int id, string[] archetype, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        string f = filter.Trim();
        if (name.Contains(f, StringComparison.OrdinalIgnoreCase)) return true;
        if (id.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(f, StringComparison.Ordinal)) return true;
        foreach (string a in archetype)
            if (a.Contains(f, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary><c>DebugName</c> component があればその名、無ければ空文字。</summary>
    public static string EntityName(Entity e)
        => e.HasComponent<DebugName>() ? e.GetComponent<DebugName>().Name ?? "" : "";

    /// <summary>所持 component 型名 + tag 型名の列 (アーキタイプ表示用)。</summary>
    public static string[] Archetype(Entity e)
    {
        var arch = new List<string>();
        foreach (var c in e.Components) arch.Add(c.Type.Type.Name);
        foreach (var t in e.Tags) arch.Add(t.Type.Name);
        return arch.ToArray();
    }

    private static DiagEntity SerializeEntity(Entity e)
    {
        var comps = new List<DiagComponent>();
        foreach (var c in e.Components)
        {
            string json;
            // c.Value は Debugger 表示用に obsolete 指定されているが、DevTools でも同じ用途
#pragma warning disable CS0618
            try { json = System.Text.Json.JsonSerializer.Serialize(c.Value, JsonOptions); }
            catch (Exception ex) { json = $"\"<serialize failed: {ex.GetType().Name}>\""; }
#pragma warning restore CS0618
            comps.Add(new DiagComponent(c.Type.Type.Name, json));
        }
        var tagTypes = new List<string>();
        foreach (var t in e.Tags) tagTypes.Add(t.Type.Name);
        return new DiagEntity(e.Id, comps.ToArray(), tagTypes.ToArray());
    }
}
