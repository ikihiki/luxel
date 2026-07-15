using System.Numerics;
using System.Text;
using Luxel.Controls;
using Luxel.Player;
using Luxel.Resources;
using Luxel.SceneEdit;
using Luxel.Typography;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>Luxel Studio dogfood (ToDo 27 GE-7) — 北極星シナリオの通し実演: ほぼ空のプロジェクトから
/// **エディタ操作だけ**でコイン集めミニゲームを作る (タイル描き → エンティティ追加 → 保存 →
/// **保存したファイルから** ▶ 実行 → csx がコイン取得を判定)。出荷は同レイアウトを ship-verify.ps1 が
/// 実機検証済み (GE-6)。フル DockHost シェル化は M12 以降。</summary>
public static class StudioDogfoodStory
{
    private static readonly Lazy<VectorFont> Font = new(() => GalleryFonts.Load(GalleryFonts.Regular));

    [Story("Apps/Studio/CoinGame", Height = 700, Order = 152)]
    public static Widget CoinGame(StoryContext ctx)
    {
        // プロジェクトフォルダ (エディタ側 = 書ける IFileStorage)。開始点はほぼ空:
        // 空シーン (空のタイルレイヤ 1 枚) + csx 2 本だけ (コード編集は ScriptEditor story で実証済み)
        var storage = new MemoryFileStorage();
        storage.Write("project.luxel", GameProjectJson.Serialize(new GameProject("Coin Game", "res://scenes/main.scene.json", 480, 288)));
        storage.Write("scenes/main.scene.json", SceneJson.Serialize(SceneDoc.Of(SceneSpace.TwoD, [],
            [TileLayer.Of(1, "ground", "res://atlas/tiles.atlas.json", 32, 15, 9)])));
        storage.Write("scripts/walk.csx", "Update = (self, world, dt) => { self.Pos.X += 60f * dt; };");
        storage.Write("scripts/coin.csx",
            "Update = (self, world, dt) => { var p = world.Find(\"Player\"); " +
            "if (p != null && MathF.Abs(p.Pos.X - self.Pos.X) < 40f && MathF.Abs(p.Pos.Y - self.Pos.Y) < 40f) self.Pos.X += 200f; };");

        SceneEditorView ed = SceneEditorView(
            source: SceneJson.Deserialize(storage.Read("scenes/main.scene.json")!), viewWidth: 280f, viewHeight: 280f);
        SceneInspector insp = SceneInspector(editor: ed, schemas: SceneSchemas.BuiltIns(), width: 164f);

        // ツール/エンティティ追加 (Studio シェルのメニュー相当)
        Button rectDirt = Button(_ => { ed.Tool = SceneTool.Rect; ed.ActiveTile = 2; }, "矩形:土");
        Button selectT = Button(_ => ed.Tool = SceneTool.Select, "選択");
        SceneComponent T2(float x, float y) => SceneSchemas.NewComponent(SceneSchemas.Transform2D).With("pos", SceneValue.Of(new Vector2(x, y)));
        SceneComponent Tint(float r, float g, float b) => SceneComponent.Of("tint", ("color", SceneValue.Of(new Vector4(r, g, b, 1))));
        SceneComponent Script(string p) => SceneSchemas.NewComponent(SceneSchemas.Behaviour).With("script", SceneValue.Of(p));
        Button addPlayer = Button(_ => ed.ApplyEdit(new AddEntity(
            SceneEntity.Of(SceneCommands.NextEntityId(ed.Scene.Doc), "Player", T2(80, 150), Tint(0.45f, 0.66f, 0.95f), Script("res://scripts/walk.csx")))), "+プレイヤー");
        Button addCoin = Button(_ => ed.ApplyEdit(new AddEntity(
            SceneEntity.Of(SceneCommands.NextEntityId(ed.Scene.Doc), "Coin", T2(240, 150), Tint(0.9f, 0.78f, 0.35f), Script("res://scripts/coin.csx")))), "+コイン");

        Button save = Button(_ => storage.Write("scenes/main.scene.json", SceneJson.Serialize(ed.Scene.Doc)), "保存");

        // ▶ は「保存したファイル」から起動 — エディタ → 保存 → ロード → 実行の縦串を毎回通す
        Player2DWorld? world = null;
        void Play()
        {
            var fs = new MemoryFileSystem();
            foreach (string path in storage.List()) fs.Set(path, Encoding.UTF8.GetBytes(storage.Read(path)!));
            world = PlayerLoader.LoadStart(fs).World2D;
        }
        Button play = Button(_ => Play(), "▶ 実行"), stop = Button(_ => world = null, "停止");

        Canvas2D view = Canvas2D(448f, 288f, animate: (s, _) =>
        {
            if (world is null)
            {
                s.FillRect(TilePalette.Pack(22, 26, 34), 0, 0, 448, 288);
                Font.Value.AppendText(s, "(stopped — 保存して ▶)", 140, 148, 13, TilePalette.Pack(120, 126, 140));
                return;
            }
            world.Update(1f / 60f);
            world.Render(s, 448, 288, Font.Value);
        });

        ctx.Play("dogfood", async d =>
        {
            await d.Snap();                                        // ほぼ空のプロジェクト
            // ① タイルで床を描く (矩形:土)
            await d.Click(rectDirt);
            Vector2 a = ed.CellClient(0, 8), b = ed.CellClient(14, 8);
            await d.Drag(a.X, a.Y, b.X, b.Y);
            await d.Expect(() => ed.TileAt(0, 8) == 2 && ed.TileAt(14, 8) == 2, "床を矩形ペイント");
            await d.Click(selectT);
            // ② エンティティを配置 (behaviour/tint はスキーマ既定 + プリセット)
            await d.Click(addPlayer);
            await d.Click(addCoin);
            await d.Expect(() => ed.EntityCount == 2, "プレイヤー + コイン配置");
            Vector2 c2 = ed.EntityScreenCenter(2);
            await d.Click(c2.X, c2.Y);                             // コイン選択 → インスペクタに behaviour が見える
            await d.Snap("authored");
            // ③ 保存 → 保存物が有効なプロジェクトであること
            await d.Click(save);
            await d.Expect(() => storage.Read("scenes/main.scene.json")!.Contains("coin.csx"), "シーンを保存");
            // ④ 保存したファイルから ▶ — csx がコイン取得を判定するまで回す
            await d.Click(play);
            await d.Step(150);                                     // 2.5s: Player が歩いて Coin に接触
            await d.Expect(() => world!.Find("Coin")!.Pos.X > 400f, "接触でコインが跳ぶ (coin.csx)");
            await d.Expect(() => ed.EntityPos2D(1).X == 80f, "編集 doc は不変 (ADR-0017)");
            await d.Snap("playing");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))[
            VStack(8)[
                Heading("Luxel Studio dogfood — コイン集めをエディタ操作だけで"),
                Muted("タイル描き → エンティティ追加 → 保存 → 保存ファイルから ▶ 実行 (coin.csx が接触判定)。出荷は ship-verify.ps1 が同レイアウトを検証済み。"),
                HStack(6)[rectDirt, selectT, addPlayer, addCoin, save, play, stop],
                HStack(10)[ed, insp],
                view]];
    }
}
