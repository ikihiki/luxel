using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Assets;
using Luxel.AssetRuntime;

namespace Luxel.Samples;

/// <summary>
/// サンプル61: Friflo の Tag + ILinkComponent (relationship) を使う demo。
/// <list type="bullet">
/// <item><see cref="Enabled"/> Tag: storage 軽量な marker、query フィルタに利用</item>
/// <item><see cref="Selected"/> Tag: UI で選択状態を表現</item>
/// <item>ILinkComponent (相互参照、自動 cleanup): 親子・ターゲット関係</item>
/// </list>
/// </summary>
public static class Sample61TagsRelations
{
    public struct Hp : IComponent { public int Value; }

    // ILinkComponent: 1 entity → 1 entity の link (双方向、削除時 auto-cleanup)
    public struct Targets : ILinkComponent
    {
        public Entity target;
        public Entity GetIndexedValue() => target;
    }

    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 61: Tags + Relations demo ===");
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");
        bool ok = true;

        var world = new Luxel.Ecs.World();

        // === Tag demo ===
        // 10 entity 作成、半数を Enabled タグ付け
        for (int i = 0; i < 10; i++)
        {
            var e = world.CreateEntity(new Hp { Value = 100 });
            if (i % 2 == 0) e.AddTag<Enabled>();
        }

        // Tag フィルタで query
        int enabledCount = 0;
        world.Query<Hp>().AllTags(Friflo.Engine.ECS.Tags.Get<Enabled>())
            .ForEachEntity((ref Hp h, Entity e) => { enabledCount++; });
        Console.WriteLine($"  Enabled entities: {enabledCount} (expect 5)");
        if (enabledCount != 5) { ok = false; Console.Error.WriteLine("FAILED: Enabled tag count"); }

        // === Relationship demo (ILinkComponent) ===
        var player = world.CreateEntity(new Hp { Value = 200 });
        var enemy1 = world.CreateEntity(new Hp { Value = 50 });
        var enemy2 = world.CreateEntity(new Hp { Value = 75 });

        player.AddComponent(new Targets { target = enemy1 });

        // link を読む
        var t = player.GetComponent<Targets>();
        Console.WriteLine($"  player targets: entity {t.target.Id}, hp={t.target.GetComponent<Hp>().Value} (expect 50)");
        if (t.target.GetComponent<Hp>().Value != 50)
        { ok = false; Console.Error.WriteLine("FAILED: link read"); }

        // target を切り替え (link は overwrite で自動更新)
        player.RemoveComponent<Targets>();
        player.AddComponent(new Targets { target = enemy2 });
        Console.WriteLine($"  player targets after switch: entity {player.GetComponent<Targets>().target.Id}, hp=75");

        // 入射 link (GetIncomingLinks): enemy2 を targets する entity を取得 (O(1))
        var incoming = enemy2.GetIncomingLinks<Targets>();
        Console.WriteLine($"  enemy2 incoming links: {incoming.Count} (expect 1)");
        if (incoming.Count != 1) { ok = false; Console.Error.WriteLine("FAILED: incoming link count"); }

        // target 削除 → link 自動 cleanup
        enemy2.DeleteEntity();
        bool hasLink = player.HasComponent<Targets>();
        Console.WriteLine($"  player.HasComponent<Targets> after enemy2 delete: {hasLink} (expect False)");
        if (hasLink) { ok = false; Console.Error.WriteLine("FAILED: auto cleanup"); }

        Console.WriteLine(ok ? "OK: ECS-B8 (Tags + ILinkComponent relations) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
