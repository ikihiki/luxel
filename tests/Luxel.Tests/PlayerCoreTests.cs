using System.Numerics;
using System.Text;
using Luxel.Player;
using Luxel.Resources;
using Luxel.SceneEdit;

namespace Luxel.Tests;

/// <summary>Luxel.Player GE-3 S1 (ToDo 27 / ADR-0015) の単体テスト — SceneCompiler の 2D 展開
/// (transform 第一級化・データ袋・タイル素通し)、3D バックエンド、PlayerLoader (VFS からの読込)、
/// PlayerEntity の Field/SetField。canvas 不要 (純データ)。</summary>
public class PlayerCoreTests
{
    private static SceneDoc SampleScene()
    {
        var enemy = SceneComponent.Of("enemy", ("speed", SceneValue.Of(60f)), ("patrol", SceneValue.Of(true)));
        var t = SceneSchemas.NewComponent(SceneSchemas.Transform2D).With("pos", SceneValue.Of(new Vector2(100, 80)));
        return SceneDoc.Of(SceneSpace.TwoD,
            [
                SceneEntity.Of(1, "Player", t),
                SceneEntity.Of(2, "Enemy", SceneSchemas.NewComponent(SceneSchemas.Transform2D), enemy),
                SceneEntity.Of(3, "GameRules"),   // transform 無し = 論理のみ
            ],
            [TileLayer.Of(1, "ground", "res://atlas/t.json", 32, 4, 2, [0, 0, 0, 0, 1, 1, 2, 1])]);
    }

    private static SceneDoc Sample3DScene(string asset = "res://assets/cube.glb")
    {
        var t = SceneSchemas.NewComponent(SceneSchemas.Transform3D)
            .With("pos", SceneValue.Of(new Vector3(1, 0.5f, 0)))
            .With("scale", SceneValue.Of(new Vector3(2, 1, 1)));
        var mesh = SceneSchemas.NewComponent(SceneSchemas.Mesh3D).With("asset", SceneValue.Of(asset));
        var cam = SceneSchemas.NewComponent(SceneSchemas.Camera3D)
            .With("target", SceneValue.Of(new Vector3(1, 0.4f, 0)))
            .With("distance", SceneValue.Of(6f))
            .With("yaw", SceneValue.Of(0.7f))
            .With("pitch", SceneValue.Of(0.3f));
        return SceneDoc.Of(SceneSpace.ThreeD,
            [
                SceneEntity.Of(1, "Crate", t, mesh),
                SceneEntity.Of(2, "Camera", cam),
            ]);
    }

    [Fact]
    public void Compile2D_ExpandsTransformAndKeepsData()
    {
        Player2DWorld w = SceneCompiler.Compile2D(SampleScene());
        Assert.Equal(3, w.Entities.Count);
        PlayerEntity p = w.Entity(1);
        Assert.True(p.HasTransform);
        Assert.Equal(new Vector2(100, 80), p.Pos);
        Assert.Equal(Vector2.One, p.Scale);
        // transform2d はデータ袋に残らない (二重の真実を避ける)
        Assert.False(p.Has("transform2d"));
        // データコンポーネントは形のまま
        PlayerEntity e = w.Entity(2);
        Assert.Equal(60f, e.Field("enemy", "speed")!.Value.AsFloat());
        Assert.True(e.Field("enemy", "patrol")!.Value.AsBool());
        // transform 無し
        Assert.False(w.Entity(3).HasTransform);
        // タイル素通し + Find
        Assert.Equal(2, w.TileAt(1, 2, 1));
        Assert.Equal(2, w.Find("Enemy")!.Id);
        Assert.Null(w.Find("nope"));
    }

    [Fact]
    public void Compile3D_ExpandsTransformMeshCameraAndQueries()
    {
        Player3DWorld w = SceneCompiler.Compile3D(Sample3DScene());
        Assert.IsType<Player3DWorld>(SceneCompiler.Compile(Sample3DScene()));
        PlayerEntity e = w.Entity(1);
        Assert.True(e.HasTransform3D);
        Assert.Equal(new Vector3(1, 0.5f, 0), e.Pos3D);
        Assert.Equal(new Vector3(2, 1, 1), e.Scale3D);
        Assert.Equal("res://assets/cube.glb", e.MeshAsset);
        Assert.Contains("res://assets/cube.glb", w.MeshAssets);
        Assert.Equal(6f, w.Camera.Distance);
        Assert.Contains(e, w.QueryAabb(new Vector3(0.5f, 0, -0.2f), new Vector3(1.5f, 1, 0.2f)));
        Assert.True(w.RayCast(new Vector3(1, 0.5f, -5), Vector3.UnitZ, out Player3DHit hit));
        Assert.Equal(1, hit.Entity.Id);
    }

    [Fact]
    public void Entity_SetFieldMutatesRuntimeOnly()
    {
        Player2DWorld w = SceneCompiler.Compile2D(SampleScene());
        PlayerEntity e = w.Entity(2);
        e.SetField("enemy", "speed", SceneValue.Of(90f));
        Assert.Equal(90f, e.Field("enemy", "speed")!.Value.AsFloat());
        Assert.Throws<KeyNotFoundException>(() => e.SetField("nope", "x", SceneValue.Of(1)));
        // Update は時間を積む (固定 dt)
        w.Update(1f / 60f); w.Update(1f / 60f);
        Assert.Equal(2f / 60f, w.Time, 3);
    }

    [Fact]
    public void Shipper_CopyProject_LayoutAndValidation()
    {
        string src = Path.Combine(Path.GetTempPath(), $"luxel-ship-src-{Guid.NewGuid():N}");
        string dst = Path.Combine(Path.GetTempPath(), $"luxel-ship-dst-{Guid.NewGuid():N}", "project");
        try
        {
            Directory.CreateDirectory(Path.Combine(src, "scripts"));
            File.WriteAllText(Path.Combine(src, "project.luxel"), "{}");
            File.WriteAllText(Path.Combine(src, "scripts", "a.csx"), "// a");
            PlayerShipper.CopyProject(src, dst);
            Assert.True(File.Exists(Path.Combine(dst, "project.luxel")));
            Assert.Equal("// a", File.ReadAllText(Path.Combine(dst, "scripts", "a.csx")));
            // 再コピー = 入れ替え (残骸が残らない)
            File.Delete(Path.Combine(src, "scripts", "a.csx"));
            File.WriteAllText(Path.Combine(src, "scripts", "b.csx"), "// b");
            PlayerShipper.CopyProject(src, dst);
            Assert.False(File.Exists(Path.Combine(dst, "scripts", "a.csx")));
            // project.luxel の無いフォルダは拒否
            File.Delete(Path.Combine(src, "project.luxel"));
            Assert.Throws<InvalidOperationException>(() => PlayerShipper.CopyProject(src, dst));
        }
        finally
        {
            try { Directory.Delete(src, true); Directory.Delete(Path.GetDirectoryName(dst)!, true); } catch { }
        }
    }

    [Fact]
    public void Loader_LoadsProjectAndStartScene()
    {
        var fs = new MemoryFileSystem();
        void Put(string path, string text) => fs.Set(path, Encoding.UTF8.GetBytes(text));
        Put("project.luxel", GameProjectJson.Serialize(new GameProject("デモ", "res://scenes/main.scene.json", 480, 300)));
        Put("scenes/main.scene.json", SceneJson.Serialize(SampleScene()));

        PlayerGame game = PlayerLoader.LoadStart(fs);
        Assert.Equal("デモ", game.Project.Name);
        Assert.Equal(480, game.Project.WindowWidth);
        Assert.Equal(new Vector2(100, 80), game.World.Entity(1).Pos);
        Assert.IsType<Player2DWorld>(game.World);
        // 無いファイルはパス付きで分かる
        Assert.Throws<FileNotFoundException>(() => PlayerLoader.LoadStart(new MemoryFileSystem()));
    }

    [Fact]
    public void Loader3D_ValidatesGlbAssetRefs()
    {
        var fs = new MemoryFileSystem();
        void Put(string path, string text) => fs.Set(path, Encoding.UTF8.GetBytes(text));
        Put("project.luxel", GameProjectJson.Serialize(new GameProject("3d", "res://scenes/main.scene.json")));
        Put("scenes/main.scene.json", SceneJson.Serialize(Sample3DScene()));

        Player3DWorld missing = PlayerLoader.LoadStart(fs).World3D;
        Assert.Contains("res://assets/cube.glb", missing.MissingAssets);

        fs.Set("assets/cube.glb", [0x67, 0x6c, 0x54, 0x46]);
        Player3DWorld ok = PlayerLoader.LoadStart(fs).World3D;
        Assert.Empty(ok.MissingAssets);
    }
}
