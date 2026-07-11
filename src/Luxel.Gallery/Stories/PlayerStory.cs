using System.Numerics;
using System.Text;
using Luxel.Player;
using Luxel.Resources;
using Luxel.SceneEdit;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>Luxel.Player — データ駆動ゲームランタイム (ADR-0015 / ToDo 27 GE-3)。エディタが吐く
/// プロジェクトフォルダ (project.luxel + scenes) を VFS から読み、SceneCompiler が world へ一方向に
/// 構築して固定 dt で駆動する。見た目はエディタと同じプレースホルダ (TilePalette 共有) —
/// SceneEditorView で編集した絵がそのまま動く。csx ビヘイビアは S2、exe は S3。</summary>
public static class PlayerStory
{
    private static readonly Lazy<VectorFont> Font = new(() => GalleryFonts.Load(GalleryFonts.Regular));

    // fixture プロジェクト: エディタ形式そのままの JSON を MemoryFileSystem に置く
    private static IVirtualFileSystem FixtureProject()
    {
        SceneComponent T2(float x, float y)
            => SceneSchemas.NewComponent(SceneSchemas.Transform2D).With("pos", SceneValue.Of(new Vector2(x, y)));
        SceneComponent Tint(float r, float g, float b)
            => SceneComponent.Of("tint", ("color", SceneValue.Of(new Vector4(r, g, b, 1))));

        var cells = new int[15 * 9];
        for (int x = 0; x < 15; x++) cells[8 * 15 + x] = 1;                    // 地面 = 草
        cells[6 * 15 + 9] = 3; cells[6 * 15 + 10] = 3; cells[6 * 15 + 11] = 3; // 浮き足場 = 石
        var scene = SceneDoc.Of(SceneSpace.TwoD,
            [
                SceneEntity.Of(1, "Player", T2(110, 216), Tint(0.45f, 0.66f, 0.95f)),
                SceneEntity.Of(2, "Coin", T2(330, 150), Tint(0.9f, 0.78f, 0.35f)),
            ],
            [TileLayer.Of(1, "ground", "res://atlas/tiles.atlas.json", 32, 15, 9, cells)]);

        var fs = new MemoryFileSystem();
        void Put(string path, string text) => fs.Set(path, Encoding.UTF8.GetBytes(text));
        Put("project.luxel", GameProjectJson.Serialize(new GameProject("Player デモ", "res://scenes/main.scene.json", 480, 288)));
        Put("scenes/main.scene.json", SceneJson.Serialize(scene));
        return fs;
    }

    [Story("Apps/Player/Basic", Height = 420, Order = 148)]
    public static Widget Basic(StoryContext ctx)
    {
        PlayerGame game = PlayerLoader.LoadStart(FixtureProject());
        Player2DWorld world = game.World;
        float w = game.Project.WindowWidth, h = game.Project.WindowHeight;

        // Canvas2D の animate = Tick 累積 (wall-clock 禁止) — snap の固定ステップで決定的
        Luxel.Controls.Canvas2D view = Canvas2D(w, h, animate: (s, _) => world.Render(s, w, h, Font.Value));

        ctx.Play("run", async d =>
        {
            await d.Snap();                              // 読み込んだプロジェクトの初期状態 (タイル + tint 付き箱)
            // S1 はランタイム API を直接叩いて「動く」ことを見る (S2 で csx がこれをやる)
            PlayerEntity player = world.Find("Player")!;
            for (int i = 0; i < 30; i++) { world.Update(1f / 60f); player.Pos.X += 2f; }
            await d.Step(1);                             // animate が再エンコード
            await d.Expect(() => player.Pos.X == 170f, "固定 dt 30 step で移動");
            await d.Expect(() => world.TileAt(1, 0, 8) == 1, "タイルはランタイムへ素通し");
            await d.Snap("stepped");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("Luxel.Player — データ駆動ランタイム"),
                Muted("project.luxel + scenes/*.scene.json (エディタ形式そのまま) を VFS から読み、SceneCompiler が 2D world へ一方向構築。固定 dt で駆動し、見た目はエディタと同じプレースホルダ (TilePalette 共有)。"),
                view]];
    }
}
