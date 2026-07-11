using System.Numerics;
using System.Text;
using Luxel.Player;
using Luxel.Resources;
using Luxel.SceneEdit;

namespace Luxel.Tests;

/// <summary>Luxel.Player GE-3 S1 (ToDo 27 / ADR-0015) の単体テスト — SceneCompiler の 2D 展開
/// (transform 第一級化・データ袋・タイル素通し)、3D 未対応、PlayerLoader (VFS からの読込)、
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

    [Fact]
    public void Compile2D_ExpandsTransformAndKeepsData()
    {
        Player2DWorld w = SceneCompiler.Compile(SampleScene());
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
    public void Compile_3DIsNotSupportedYet()
        => Assert.Throws<NotSupportedException>(() => SceneCompiler.Compile(SceneDoc.Empty(SceneSpace.ThreeD)));

    [Fact]
    public void Entity_SetFieldMutatesRuntimeOnly()
    {
        Player2DWorld w = SceneCompiler.Compile(SampleScene());
        PlayerEntity e = w.Entity(2);
        e.SetField("enemy", "speed", SceneValue.Of(90f));
        Assert.Equal(90f, e.Field("enemy", "speed")!.Value.AsFloat());
        Assert.Throws<KeyNotFoundException>(() => e.SetField("nope", "x", SceneValue.Of(1)));
        // Update は時間を積む (固定 dt)
        w.Update(1f / 60f); w.Update(1f / 60f);
        Assert.Equal(2f / 60f, w.Time, 3);
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
        // 無いファイルはパス付きで分かる
        Assert.Throws<FileNotFoundException>(() => PlayerLoader.LoadStart(new MemoryFileSystem()));
    }
}
