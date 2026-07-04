using System.Numerics;
using Friflo.Engine.ECS;
using Luxel;
using Luxel.Ecs;
using Luxel.Assets;
using Luxel.AssetRuntime;
using Luxel.Ecs.Signal;
using Luxel.UI;

namespace Luxel.Samples;

/// <summary>
/// サンプル60: Friflo.Engine.ECS 統合 demo (Luxel.Ecs)。
/// <para>
/// <list type="bullet">
/// <item>10 entity 作成 (Position + Velocity + Color3D)</item>
/// <item>SimulateSystem で位置更新</item>
/// <item>Parent/LocalTransform/GlobalTransform で hierarchy 検証</item>
/// <item><see cref="Luxel.Ecs.World.Signal{T}"/> で reactive UI ブリッジ動作確認</item>
/// </list>
/// </para>
/// </summary>
public static class Sample60FrifloEcs
{
    public struct Position : IComponent { public float X, Y, Z; }
    public struct Velocity : IComponent { public float X, Y, Z; }

    public static int Run(Func<GpuDevice> createDevice)
    {
        Console.WriteLine("=== Sample 60: Friflo ECS demo ===");
        bool ok = true;
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name} (確認のみ、ECS demo は GPU 不使用)");

        // --- 1. Basic entity creation + query ---
        var world = new Luxel.Ecs.World();
        for (int i = 0; i < 10; i++)
            world.CreateEntity(
                new Position { X = i, Y = 0, Z = 0 },
                new Velocity { X = 1, Y = 0, Z = 0 },
                new Color3D(new Vector4(1, 0, 0, 1)));

        // Simulate: position += velocity * dt
        const float dt = 0.5f;
        world.Query<Position, Velocity>().ForEachEntity((ref Position p, ref Velocity v, Entity e) =>
        {
            p.X += v.X * dt;
            p.Y += v.Y * dt;
            p.Z += v.Z * dt;
        });

        // Verify
        int count = 0;
        float sumX = 0;
        world.Query<Position>().ForEachEntity((ref Position p, Entity e) =>
        {
            count++;
            sumX += p.X;
        });
        Console.WriteLine($"  entities: {count}, sumX after dt={dt}: {sumX} (expect 50 = 0+1+...+9 + 0.5*10)");
        if (count != 10 || Math.Abs(sumX - 50f) > 1e-4)
        { ok = false; Console.Error.WriteLine($"FAILED: count/sumX"); }

        // --- 2. Hierarchy: TransformPropagateSystem ---
        var root = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(10, 0, 0)));
        var child = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, 5, 0)));
        child.AddComponent(new Parent(root));

        TransformPropagateSystem.Run(world);

        Vector3 childWorld = Vector3.Transform(Vector3.Zero, child.GetComponent<GlobalTransform>().Matrix);
        Console.WriteLine($"  child world pos: {childWorld} (expect (10, 5, 0))");
        if (Math.Abs(childWorld.X - 10) > 1e-4 || Math.Abs(childWorld.Y - 5) > 1e-4)
        { ok = false; Console.Error.WriteLine($"FAILED: hierarchy"); }

        // --- 3. Signal ブリッジ ---
        var tracked = world.CreateEntity(new Position { X = 100 });
        var sig = world.Signal<Position>(tracked);
        int notifyCount = 0;
        Position observed = default;
        Luxel.UI.Reactive.Effect(() =>
        {
            observed = sig.Value;
            notifyCount++;
        });
        Console.WriteLine($"  initial: observed.X={observed.X}, notify={notifyCount}");

        tracked.RemoveComponent<Position>();
        tracked.AddComponent(new Position { X = 999 });

        Console.WriteLine($"  after change: observed.X={observed.X}, notify={notifyCount}");
        if (Math.Abs(observed.X - 999) > 1e-4 || notifyCount < 2)
        { ok = false; Console.Error.WriteLine($"FAILED: signal bridge"); }

        Console.WriteLine(ok ? "OK: ECS-B (Friflo + Luxel.Ecs + Signal ブリッジ + Hierarchy) 動作" : "FAILED");
        return ok ? 0 : 1;
    }
}
