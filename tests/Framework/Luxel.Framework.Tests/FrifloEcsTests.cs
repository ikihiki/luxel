using System.Numerics;
using Friflo.Engine.ECS;
using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.Ecs;
using Luxel.Ecs.Signal;
using Luxel.UI;

namespace Luxel.Tests;

public class FrifloEcsTests
{
    public struct Position : IComponent { public float X, Y, Z; }
    public struct Velocity : IComponent { public float X, Y, Z; }

    [Fact]
    public void CreateEntity_StoresComponents()
    {
        var world = new Luxel.Ecs.World();
        var e = world.CreateEntity(new Position { X = 1, Y = 2, Z = 3 });
        Assert.True(e.HasComponent<Position>());
        var p = e.GetComponent<Position>();
        Assert.Equal(1, p.X);
        Assert.Equal(2, p.Y);
        Assert.Equal(3, p.Z);
    }

    [Fact]
    public void Query_TwoComponents_IteratesMatching()
    {
        var world = new Luxel.Ecs.World();
        world.CreateEntity(new Position { X = 1 }, new Velocity { X = 10 });
        world.CreateEntity(new Position { X = 2 }, new Velocity { X = 20 });
        world.CreateEntity(new Position { X = 3 }); // Velocity 無し

        int count = 0;
        float sumPx = 0, sumVx = 0;
        world.Query<Position, Velocity>().ForEachEntity((ref Position p, ref Velocity v, Entity e) =>
        {
            count++;
            sumPx += p.X;
            sumVx += v.X;
        });
        Assert.Equal(2, count);
        Assert.Equal(3, sumPx);  // 1 + 2
        Assert.Equal(30, sumVx); // 10 + 20
    }

    [Fact]
    public void Signal_CachedPerEntity()
    {
        var world = new Luxel.Ecs.World();
        var e = world.CreateEntity(new Position { X = 5 });

        var s1 = world.Signal<Position>(e);
        var s2 = world.Signal<Position>(e);
        Assert.Same(s1, s2);  // 同じ entity + component なら同一 Signal
    }

    [Fact]
    public void Signal_ReflectsInitialComponentValue()
    {
        var world = new Luxel.Ecs.World();
        var e = world.CreateEntity(new Position { X = 42 });
        var sig = world.Signal<Position>(e);
        Assert.Equal(42, sig.Value.X);
    }

    [Fact]
    public void TransformPropagateSystem_Hierarchy()
    {
        var world = new Luxel.Ecs.World();
        var root = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(1, 0, 0)));
        var mid = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, 2, 0)));
        var leaf = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, 0, 3)));
        mid.AddComponent(new Parent(root));
        leaf.AddComponent(new Parent(mid));

        TransformPropagateSystem.Run(world);

        // leaf の world 位置 = (1, 2, 3)
        var worldMat = leaf.GetComponent<GlobalTransform>().Matrix;
        Vector3 pos = Vector3.Transform(Vector3.Zero, worldMat);
        Assert.Equal(1f, pos.X, precision: 4);
        Assert.Equal(2f, pos.Y, precision: 4);
        Assert.Equal(3f, pos.Z, precision: 4);
    }

    [Fact]
    public void ScheduleRoot_HasFivePhases()
    {
        var world = new Luxel.Ecs.World();
        var root = ScheduleRoot.Create(world);
        // SystemRoot は SystemGroup を継承し、ChildSystems で子を取れる
        Assert.True(root.ChildSystems.Count >= 5);
    }

    [Fact]
    public void Signal_ReactiveEffect_PicksUpComponentChange()
    {
        var world = new Luxel.Ecs.World();
        var e = world.CreateEntity(new Position { X = 10 });
        var sig = world.Signal<Position>(e);

        int notifyCount = 0;
        Position observed = default;
        Luxel.UI.Reactive.Effect(() =>
        {
            observed = sig.Value;
            notifyCount++;
        });
        Assert.Equal(1, notifyCount);
        Assert.Equal(10, observed.X);

        // Friflo の component 変更 (remove + add で OnComponentChanged 発火)
        e.RemoveComponent<Position>();
        e.AddComponent(new Position { X = 99 });

        // Signal が更新され、Reactive.Effect が再実行されたか
        Assert.Equal(99, observed.X);
        Assert.True(notifyCount >= 2, $"notify >= 2 expected, got {notifyCount}");
    }
}
