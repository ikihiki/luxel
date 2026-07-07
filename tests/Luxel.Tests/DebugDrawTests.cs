using System.Linq;
using System.Numerics;
using Luxel.Ecs;
using Luxel.Particles;
using Luxel.Particles.TwoD;
using Luxel.Physics;
using Luxel.Physics.Gizmos;
using Luxel.TwoD;

namespace Luxel.Tests;

/// <summary>
/// DebugDraw (gizmo コア) の GPU 不要テスト: カテゴリ ON/OFF のゼロ割り当て、
/// Flush が Scene2D へ正しい図形を出すこと、worldToScreen 投影、テキスト委譲。
/// </summary>
[Collection("GlobalGizmo")]   // グローバル DebugDraw を触るテストを直列化 (CavernDevOverlayTests と干渉しない)
public class DebugDrawTests
{
    public DebugDrawTests() => DebugDraw.Reset();   // 静的状態をテスト間で分離

    [Fact]
    public void Disabled_Category_IsZeroCost_NoCommands()
    {
        // 何も Enable していない → 全 API が即 return、コマンドは溜まらない
        DebugDraw.Line(new Vector2(0, 0), new Vector2(1, 1), 0xFF0000FF);
        DebugDraw.Rect(0, 0, 10, 10, 0xFF00FF00);
        DebugDraw.Circle(5, 5, 3, 0xFFFF0000);
        DebugDraw.Text(0, 0, "x", 0xFFFFFFFF);
        Assert.Equal(0, DebugDraw.PendingCount);
    }

    [Fact]
    public void Enable_OnlyMatchingCategory_Accumulates()
    {
        DebugDraw.Enable("physics");
        DebugDraw.Line(new Vector2(0, 0), new Vector2(1, 1), 0u, kind: "physics");  // 溜まる
        DebugDraw.Line(new Vector2(0, 0), new Vector2(1, 1), 0u, kind: "camera");   // 出ない
        Assert.Equal(1, DebugDraw.PendingCount);
        Assert.True(DebugDraw.IsEnabled("physics"));
        Assert.False(DebugDraw.IsEnabled("camera"));
    }

    [Fact]
    public void All_Category_EnablesEverything()
    {
        DebugDraw.Enable(DebugDraw.All);
        Assert.True(DebugDraw.IsEnabled("anything"));
        DebugDraw.Line(new Vector2(0, 0), new Vector2(1, 1), 0u, kind: "whatever");
        Assert.Equal(1, DebugDraw.PendingCount);
    }

    [Fact]
    public void Flush_EmitsShapes_ProjectsWorldToScreen_AndClears()
    {
        DebugDraw.Enable("demo");
        DebugDraw.Line(new Vector2(0, 0), new Vector2(10, 0), 0xFF112233, kind: "demo");
        DebugDraw.Rect(0, 0, 4, 4, 0xFF445566, kind: "demo");        // 矩形アウトライン → 1 stroke (5 点)
        DebugDraw.Circle(0, 0, 2, 0xFF778899, width: 1f, segments: 8, kind: "demo");

        var scene = new Scene2D();
        // world→screen: +100,+50 平行移動でスクリーンへ (投影が効いていることを座標で確認)
        DebugDraw.Flush(scene, w => new Vector2(w.X + 100, w.Y + 50));

        // 線 + 矩形 + 円 = 3 shape (全て stroke)
        Assert.Equal(3, scene.Shapes.Count);
        // Flush 後はコマンドが空になる
        Assert.Equal(0, DebugDraw.PendingCount);
    }

    [Fact]
    public void Flush_Text_OnlyWhenDrawerProvided()
    {
        DebugDraw.Enable("demo");
        DebugDraw.Text(new Vector3(1, 2, 0), "hi", 0xFFFFFFFF, size: 12f, kind: "demo");

        // テキスト委譲なし → テキストは描かれない (図形 0)
        var noText = new Scene2D();
        DebugDraw.Flush(noText, w => new Vector2(w.X, w.Y), text: null);
        Assert.Empty(noText.Shapes);

        // 委譲あり → 呼ばれ、投影済み座標が渡る
        DebugDraw.Enable("demo");
        DebugDraw.Text(new Vector3(1, 2, 0), "hi", 0xFFFFFFFF, size: 12f, kind: "demo");
        var withText = new Scene2D();
        string? got = null;
        (float gx, float gy, float gh) = (0, 0, 0);
        DebugDraw.Flush(withText, w => new Vector2(w.X + 10, w.Y + 20),
            (sc, t, x, y, h, c) => { got = t; gx = x; gy = y; gh = h; });
        Assert.Equal("hi", got);
        Assert.Equal(11f, gx);   // 1 + 10
        Assert.Equal(22f, gy);   // 2 + 20
        Assert.Equal(12f, gh);
    }

    [Fact]
    public void Reset_ClearsCategoriesAndCommands()
    {
        DebugDraw.Enable("demo");
        DebugDraw.Line(new Vector2(0, 0), new Vector2(1, 1), 0u, kind: "demo");
        DebugDraw.Reset();
        Assert.False(DebugDraw.IsEnabled("demo"));
        Assert.Equal(0, DebugDraw.PendingCount);
    }

    [Fact]
    public void SetEnabled_TogglesCategory()
    {
        DebugDraw.SetEnabled("physics", true);
        Assert.True(DebugDraw.IsEnabled("physics"));
        DebugDraw.SetEnabled("physics", false);
        Assert.False(DebugDraw.IsEnabled("physics"));
    }

    // ---- Gizmos2D / ParticleGizmos (タスク 21 ステージ②、DebugDraw と同クラス = 静的状態を直列化) ----

    private static TileMap MakeMap()
    {
        var atlas = new SpriteAtlas("a", [new("wall", new SpriteRect(0, 0, 16, 16))]);
        var ts = new TileSet(atlas, 16, 16, [new(1, new TileDef("wall", Solid: true))]);
        var map = new TileMap(8, 8, ts);
        map.SetTile(2, 2, 1);
        map.SetTile(3, 2, 1);   // 衝突タイル 2 個
        return map;
    }

    [Fact]
    public void Gizmo_TileCollision_RectPerSolidTile_ZeroWhenOff()
    {
        TileMap map = MakeMap();
        Gizmos2D.TileCollision(map, new RectF(0, 0, 128, 128), 0u);   // OFF
        Assert.Equal(0, DebugDraw.PendingCount);

        DebugDraw.Enable(Gizmos2D.Tiles);
        Gizmos2D.TileCollision(map, new RectF(0, 0, 128, 128), 0u);
        Assert.Equal(2, DebugDraw.PendingCount);   // (2,2),(3,2) の 2 矩形
    }

    [Fact]
    public void Gizmo_CameraRig_DeadzoneAndBounds()
    {
        var rig = new CameraRig2D { Deadzone = new Vector2(80, 60), WorldBounds = new RectF(0, 0, 500, 400) };
        rig.Target = new Vector2(100, 100);
        rig.SnapToTarget();
        DebugDraw.Enable(Gizmos2D.Camera);
        Gizmos2D.CameraRig(rig, 0u, 0u);
        Assert.Equal(2, DebugDraw.PendingCount);   // デッドゾーン + 境界
    }

    [Fact]
    public void Gizmo_CameraRig_NoBounds_OnlyDeadzone()
    {
        var rig = new CameraRig2D { Deadzone = new Vector2(80, 60) };   // WorldBounds = null
        rig.Target = Vector2.Zero;
        rig.SnapToTarget();
        DebugDraw.Enable(Gizmos2D.Camera);
        Gizmos2D.CameraRig(rig, 0u, 0u);
        Assert.Equal(1, DebugDraw.PendingCount);
    }

    [Fact]
    public void Gizmo_ParticleEmitter_MarkerAndLabel_ZeroWhenOff()
    {
        var ps = new ParticleSystem(
            new ParticleConfig(Life: 1f, Speed: 0f, SpreadRadians: 0, BaseAngle: 0, Gravity: 0, Drag: 0,
                Size: 1f, Color: ParticleColor.Const(0xFFFFFFFF)), capacity: 16, seed: 1);
        ps.Emit(Vector3.Zero, 3);

        ParticleGizmos.Emitter(ps, new Vector2(50, 50), 0u);   // OFF
        Assert.Equal(0, DebugDraw.PendingCount);

        DebugDraw.Enable(ParticleGizmos.Emitters);
        ParticleGizmos.Emitter(ps, new Vector2(50, 50), 0u);
        Assert.Equal(4, DebugDraw.PendingCount);   // 円 + 十字 2 線 + ラベル
    }

    // ---- PhysicsGizmos (タスク 21 ステージ③ = Q14、DebugDraw と同クラス = 静的状態を直列化) ----

    [Fact]
    public void Gizmo_PhysicsColliders_WireBoxPerBody_ZeroWhenOff()
    {
        using var world = new Luxel.Ecs.World();
        world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, 3, 0)),
            Collider.Box(1, 1, 1), RigidBody.Dynamic());
        world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, -0.5f, 0)),
            Collider.Box(8, 1, 8), new StaticBody());

        PhysicsGizmos.DrawColliders(world, 0xFF00FF00, 0xFF888888, 0xFFFF0000, 0xFF00FFFF);   // OFF
        Assert.Equal(0, DebugDraw.PendingCount);

        DebugDraw.Enable(PhysicsGizmos.Colliders);
        PhysicsGizmos.DrawColliders(world, 0xFF00FF00, 0xFF888888, 0xFFFF0000, 0xFF00FFFF);
        Assert.Equal(24, DebugDraw.PendingCount);   // 箱 2 個 × 12 辺
    }

    [Fact]
    public void Gizmo_PhysicsColliders_CcdColorCoded()
    {
        using var world = new Luxel.Ecs.World();
        world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, 3, 0)),
            Collider.Box(1, 1, 1), RigidBody.Dynamic(ccd: true));   // CCD 有効 → ccdColor
        world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, -0.5f, 0)),
            Collider.Box(8, 1, 8), new StaticBody());               // 静的 → staticColor

        const uint dyn = 0xFF00FF00, stat = 0xFF888888, ccd = 0xFFFF0000, trig = 0xFF00FFFF;
        DebugDraw.Enable(PhysicsGizmos.Colliders);
        PhysicsGizmos.DrawColliders(world, dyn, stat, ccd, trig);

        var scene = new Scene2D();
        DebugDraw.Flush(scene, w => new Vector2(w.X, w.Y));   // 恒等投影 (XY)
        var colors = scene.Shapes.Select(s => s.Color).ToHashSet();
        Assert.Contains(ccd, colors);
        Assert.Contains(stat, colors);
        Assert.DoesNotContain(dyn, colors);   // CCD 有効なので通常動的色は出ない
    }

    [Fact]
    public void Gizmo_PhysicsColliders_TriggerColorCoded()
    {
        using var world = new Luxel.Ecs.World();
        world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, 1, 0)),
            Collider.Box(2, 2, 2), new Trigger());   // トリガー → triggerColor

        const uint dyn = 0xFF00FF00, stat = 0xFF888888, ccd = 0xFFFF0000, trig = 0xFF00FFFF;
        DebugDraw.Enable(PhysicsGizmos.Colliders);
        PhysicsGizmos.DrawColliders(world, dyn, stat, ccd, trig);
        Assert.Equal(12, DebugDraw.PendingCount);   // 箱 1 個 × 12 辺

        var scene = new Scene2D();
        DebugDraw.Flush(scene, w => new Vector2(w.X, w.Y));
        Assert.All(scene.Shapes, s => Assert.Equal(trig, s.Color));   // すべてトリガー色
    }

    [Fact]
    public void Gizmo_ContactMarkers_CrossPerPair_ZeroWhenOff()
    {
        using var world = new Luxel.Ecs.World();
        var a = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, 0, 0)));
        var b = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(2, 0, 0)));
        var pairs = new List<EntityPair> { new(a, b) };

        PhysicsGizmos.ContactMarkers(pairs, 0xFFFFFF00);   // OFF
        Assert.Equal(0, DebugDraw.PendingCount);

        DebugDraw.Enable(PhysicsGizmos.Contacts);
        PhysicsGizmos.ContactMarkers(pairs, 0xFFFFFF00);
        Assert.Equal(3, DebugDraw.PendingCount);   // 3D 十字 = 3 線
    }
}
