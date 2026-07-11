using System.Numerics;
using System.Text;
using Luxel.Player;
using Luxel.Resources;
using Luxel.SceneEdit;

namespace Luxel.Tests;

/// <summary>csx ビヘイビア (ToDo 27 GE-3 S2 / ADR-0018) の単体テスト — スクリプトがエンティティを
/// 動かす、コンパイル失敗 = 旧維持 + 診断、実行時例外 = 無効化 + 診断 (リロードで復帰)、
/// スクリプト欠落 = 診断。Roslyn コンパイルは走るが GPU 不要。</summary>
public class PlayerBehaviourTests
{
    private const string Mover = "Update = (self, world, dt) => { self.Pos.X += 60f * dt; };";

    private static (MemoryFileSystem Fs, PlayerGame Game) Load(string script)
    {
        var fs = new MemoryFileSystem();
        void Put(string path, string text) => fs.Set(path, Encoding.UTF8.GetBytes(text));
        var scene = SceneDoc.Of(SceneSpace.TwoD,
            [SceneEntity.Of(1, "Hero",
                SceneSchemas.NewComponent(SceneSchemas.Transform2D).With("pos", SceneValue.Of(new Vector2(100, 50))),
                SceneSchemas.NewComponent(SceneSchemas.Behaviour).With("script", SceneValue.Of("res://scripts/hero.csx")))]);
        Put("project.luxel", GameProjectJson.Serialize(new GameProject("t", "res://scenes/main.scene.json")));
        Put("scenes/main.scene.json", SceneJson.Serialize(scene));
        Put("scripts/hero.csx", script);
        return (fs, PlayerLoader.LoadStart(fs));
    }

    [Fact]
    public void Script_MovesEntity_WithFixedDt()
    {
        (_, PlayerGame game) = Load(Mover);
        Assert.Empty(game.World.Behaviours!.Diagnostics);
        for (int i = 0; i < 30; i++) game.World.Update(1f / 60f);
        Assert.Equal(130f, game.World.Entity(1).Pos.X, 2);   // 60px/s × 0.5s
    }

    [Fact]
    public void CompileError_KeepsOldAndReportsDiagnostics()
    {
        (MemoryFileSystem fs, PlayerGame game) = Load(Mover);
        // 壊れたコードでリロード → 旧 Update 維持 + 診断
        fs.Set("scripts/hero.csx", Encoding.UTF8.GetBytes("Update = (self, world, dt) => { self.Pos.X += ; };"));
        game.World.Behaviours!.Reload("res://scripts/hero.csx");
        Assert.NotEmpty(game.World.Behaviours.Diagnostics);
        game.World.Update(1f / 60f);
        Assert.Equal(101f, game.World.Entity(1).Pos.X, 2);   // 旧スクリプトで動き続ける
        // 直してリロード → 診断が消える
        fs.Set("scripts/hero.csx", Encoding.UTF8.GetBytes(Mover));
        game.World.Behaviours.Reload("res://scripts/hero.csx");
        Assert.Empty(game.World.Behaviours.Diagnostics);
    }

    [Fact]
    public void RuntimeException_DisablesScriptUntilReload()
    {
        (_, PlayerGame game) = Load("Update = (self, world, dt) => { throw new System.InvalidOperationException(\"爆発\"); };");
        game.World.Update(1f / 60f);   // 例外 → 無効化 + 診断 (落ちない)
        Assert.Contains(game.World.Behaviours!.Diagnostics, d => d.Contains("爆発"));
        game.World.Update(1f / 60f);   // 以降は呼ばれない (スパムしない)
        Assert.Single(game.World.Behaviours.Diagnostics);
    }

    [Fact]
    public void MissingScriptOrNoUpdate_ReportsDiagnostics()
    {
        (MemoryFileSystem fs, PlayerGame game) = Load(Mover);
        game.World.Behaviours!.Reload("res://scripts/nope.csx");
        Assert.Contains(game.World.Behaviours.Diagnostics, d => d.Contains("スクリプトが無い"));
        // Update を設定しないスクリプトも診断
        fs.Set("scripts/hero.csx", Encoding.UTF8.GetBytes("int x = 1;"));
        game.World.Behaviours.Reload("res://scripts/hero.csx");
        Assert.Contains(game.World.Behaviours.Diagnostics, d => d.Contains("Update を設定していない"));
    }
}
