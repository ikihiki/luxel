using System.Numerics;
using Luxel.Ecs;

namespace Luxel.Tests;

/// <summary>
/// ゲーム状態セーブ/ロード (タスク 15-A) の GPU 不要テスト: 往復で component 値が一致し、
/// [ComponentKey(null)] 対象 (DebugName) が JSON に出ず、エンティティ参照 (Parent) が繋がり直り、
/// 同じ World から 2 回シリアライズして文字列一致 (決定的)。
/// </summary>
public class WorldSaveTests
{
    [Fact]
    public void RoundTrip_PreservesComponentValues()
    {
        using var world = new World();
        var e = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(1, 2, 3)));
        e.AddComponent(new Color3D(new Vector4(0.5f, 0.25f, 0.75f, 1f)));
        e.AddComponent(new Visible(true));

        string json = WorldSave.Serialize(world);

        using var loaded = new World();
        WorldSave.Deserialize(loaded, json);

        Assert.Equal(1, loaded.Store.Count);
        var r = loaded.Store.GetEntityById(e.Id);
        Assert.False(r.IsNull);
        Assert.Equal(3f, r.GetComponent<LocalTransform>().Matrix.M43);
        Assert.Equal(new Vector4(0.5f, 0.25f, 0.75f, 1f), r.GetComponent<Color3D>().Rgba);
        Assert.True(r.GetComponent<Visible>().On);
    }

    [Fact]
    public void SaveIgnored_Component_NotSerialized()
    {
        using var world = new World();
        var e = world.CreateEntity(new LocalTransform(Matrix4x4.Identity));
        e.AddComponent(new DebugName("SecretName"));   // [ComponentKey(null)] = 保存対象外

        string json = WorldSave.Serialize(world);
        Assert.DoesNotContain("DebugName", json);
        Assert.DoesNotContain("SecretName", json);

        using var loaded = new World();
        WorldSave.Deserialize(loaded, json);
        var r = loaded.Store.GetEntityById(e.Id);
        Assert.False(r.HasComponent<DebugName>());        // 復元後も付いていない
        Assert.True(r.HasComponent<LocalTransform>());    // 他は残る
    }

    [Fact]
    public void EntityReference_Parent_Reconnects()
    {
        using var world = new World();
        var parent = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(10, 0, 0)));
        var child = world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(0, 5, 0)));
        child.AddComponent(new Parent(parent));

        string json = WorldSave.Serialize(world);
        using var loaded = new World();
        WorldSave.Deserialize(loaded, json);

        var rChild = loaded.Store.GetEntityById(child.Id);
        Assert.Equal(parent.Id, rChild.GetComponent<Parent>().ParentEntity.Id);
    }

    [Fact]
    public void Serialize_IsDeterministic()
    {
        using var world = new World();
        for (int i = 0; i < 5; i++)
            world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(i, 0, 0)));
        Assert.Equal(WorldSave.Serialize(world), WorldSave.Serialize(world));
    }

    [Fact]
    public void Serialize_WrapsWithVersion()
    {
        using var world = new World();
        world.CreateEntity(new LocalTransform(Matrix4x4.Identity));
        string json = WorldSave.Serialize(world);
        Assert.Contains($"\"version\":{WorldSave.CurrentVersion}", json);
        Assert.Contains("\"entities\":", json);
    }

    [Fact]
    public void Deserialize_Clear_ReplacesExistingEntities()
    {
        using var src = new World();
        src.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(7, 7, 7)));
        string json = WorldSave.Serialize(src);

        using var dst = new World();
        dst.CreateEntity(new LocalTransform(Matrix4x4.Identity));   // 事前に別 entity 群
        dst.CreateEntity(new LocalTransform(Matrix4x4.Identity));
        WorldSave.Deserialize(dst, json, clear: true);
        Assert.Equal(1, dst.Store.Count);   // clear で丸ごと置き換わる
        Assert.Equal(7f, dst.Store.GetEntityById(1).GetComponent<LocalTransform>().Matrix.M41);
    }

    [Fact]
    public void Deserialize_NoClear_UpsertsByPid()
    {
        // clear:false は Friflo の pid キー upsert — 同 pid は上書き、新 pid は追加
        using var src = new World();
        src.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(9, 0, 0)));  // pid 1
        string json = WorldSave.Serialize(src);

        using var dst = new World();
        var existing = dst.CreateEntity(new LocalTransform(Matrix4x4.Identity));      // pid 1 (衝突)
        existing.AddComponent(new Visible(false));
        WorldSave.Deserialize(dst, json, clear: false);
        Assert.Equal(1, dst.Store.Count);   // 同 pid なので上書き (件数増えない)
        Assert.Equal(9f, dst.Store.GetEntityById(1).GetComponent<LocalTransform>().Matrix.M41);
    }

    [Fact]
    public void Deserialize_MissingVersion_RawArray_StillLoads()
    {
        // 生の Friflo 配列 (version ラッパなし) も読める
        using var world = new World();
        world.CreateEntity(new LocalTransform(Matrix4x4.CreateTranslation(2, 0, 0)));
        string full = WorldSave.Serialize(world);
        // entities 配列だけ取り出して version ラッパを外す
        int idx = full.IndexOf("\"entities\":", StringComparison.Ordinal) + "\"entities\":".Length;
        string rawArray = full.Substring(idx, full.LastIndexOf('}') - idx);

        using var loaded = new World();
        WorldSave.Deserialize(loaded, rawArray);
        Assert.Equal(1, loaded.Store.Count);
    }
}
