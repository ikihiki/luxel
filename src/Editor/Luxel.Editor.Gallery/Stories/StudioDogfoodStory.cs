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
using static Luxel.Editor.Gallery.StoryKit;

using Luxel.Typography.TwoD;
namespace Luxel.Gallery.Stories;

/// <summary>Luxel Studio dogfood (ToDo 27 GE-7) — 北極星シナリオの通し実演: ほぼ空のプロジェクトから
/// **エディタ操作だけ**でコイン集めミニゲームを作る (タイル描き → エンティティ追加 → 保存 →
/// **保存したファイルから** ▶ 実行 → csx がコイン取得を判定)。出荷は同レイアウトを ship-verify.ps1 が
/// 実機検証済み (GE-6)。フル DockHost シェル化は M12 以降。</summary>
[StoryMeta("Apps/Studio")]
public static class StudioDogfoodStory
{
    private static readonly Lazy<VectorFont> Font = new(() => GalleryFonts.Load(GalleryFonts.Regular));

    [Story]
    public static StoryResult CoinGame(StoryContext ctx)
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

    [Story]
    public static StoryResult Mixed3D(StoryContext ctx)
    {
        var storage = new MemoryFileStorage();
        SceneComponent T2(float x, float y) => SceneSchemas.NewComponent(SceneSchemas.Transform2D).With("pos", SceneValue.Of(new Vector2(x, y)));
        SceneComponent T3(float x, float y, float z, float sx = 1, float sy = 1, float sz = 1)
            => SceneSchemas.NewComponent(SceneSchemas.Transform3D)
                .With("pos", SceneValue.Of(new Vector3(x, y, z)))
                .With("scale", SceneValue.Of(new Vector3(sx, sy, sz)));
        SceneComponent Tint(float r, float g, float b) => SceneComponent.Of("tint", ("color", SceneValue.Of(new Vector4(r, g, b, 1))));
        SceneComponent Mesh(string p) => SceneSchemas.NewComponent(SceneSchemas.Mesh3D).With("asset", SceneValue.Of(p));
        SceneComponent Script(string p) => SceneSchemas.NewComponent(SceneSchemas.Behaviour).With("script", SceneValue.Of(p));

        var title = SceneDoc.Of(SceneSpace.TwoD,
            [SceneEntity.Of(1, "START 3D", T2(250, 154), Tint(0.55f, 0.78f, 0.95f), Script("res://scripts/title.csx"))],
            [TileLayer.Of(1, "title", "res://atlas/title.atlas.json", 32, 16, 10)]);
        var cam = SceneSchemas.NewComponent(SceneSchemas.Camera3D)
            .With("target", SceneValue.Of(new Vector3(0, 0.5f, 0)))
            .With("distance", SceneValue.Of(7.5f))
            .With("yaw", SceneValue.Of(0.64f))
            .With("pitch", SceneValue.Of(0.38f));
        var arena = SceneDoc.Of(SceneSpace.ThreeD,
            [
                SceneEntity.Of(1, "Runner", T3(-2.2f, 0.45f, 0), Mesh("res://assets/cube.glb"), Script("res://scripts/runner3d.csx")),
                SceneEntity.Of(2, "Gate", T3(1.7f, 0.7f, 0, 0.4f, 1.4f, 1.6f), Mesh("res://assets/cube.glb")),
                SceneEntity.Of(3, "Camera", cam),
            ]);
        storage.Write("project.luxel", GameProjectJson.Serialize(new GameProject("Mixed 3D", "res://scenes/title.scene.json", 520, 320)));
        storage.Write("scenes/title.scene.json", SceneJson.Serialize(title));
        storage.Write("scenes/arena.scene.json", SceneJson.Serialize(arena));
        storage.Write("scripts/title.csx", "Update = (self, world, dt) => { if (world.Time > 0.20f) world.RequestScene(\"res://scenes/arena.scene.json\"); };");
        storage.Write("scripts/runner3d.csx", "Update = (self, world, dt) => { self.Pos3D.X += 1.0f * dt; self.Pos3D.Z = 0.45f * MathF.Sin(world.Time * 3f); };");
        storage.Write("assets/cube.glb", "glTF");

        SceneEditorView arenaEditor = SceneEditorView(source: arena, viewWidth: 280f, viewHeight: 280f);
        SceneInspector insp = SceneInspector(editor: arenaEditor, schemas: SceneSchemas.BuiltIns(), width: 164f);
        PlayerGame? game = null;
        Signal<string> status = ctx.Signal("mixed3dStatus", "停止中");

        MemoryFileSystem ToFs()
        {
            var fs = new MemoryFileSystem();
            foreach (string path in storage.List()) fs.Set(path, Encoding.UTF8.GetBytes(storage.Read(path)!));
            return fs;
        }

        void SaveArena()
        {
            storage.Write("scenes/arena.scene.json", SceneJson.Serialize(arenaEditor.Scene.Doc));
            status.Value = "保存済み";
        }

        void Play()
        {
            game = PlayerLoader.LoadStart(ToFs());
            status.Value = "title → 3D";
        }

        Button addBeacon = Button(_ => arenaEditor.ApplyEdit(new AddEntity(
            SceneEntity.Of(SceneCommands.NextEntityId(arenaEditor.Scene.Doc), "Beacon", T3(0, 1.1f, -1.5f, 0.35f, 0.35f, 0.35f), Mesh("res://assets/cube.glb")))), "+Beacon");
        Button save = Button(_ => SaveArena(), "保存");
        Button play = Button(_ => Play(), "▶ 混在プレイ");
        Button stop = Button(_ => { game = null; status.Value = "停止中"; }, "停止");

        Canvas2D view = Canvas2D(520f, 320f, animate: (s, _) =>
        {
            if (game is null)
            {
                s.FillRect(TilePalette.Pack(18, 23, 31), 0, 0, 520, 320);
                Font.Value.AppendText(s, "(2D title -> 3D arena)", 178, 164, 13, TilePalette.Pack(150, 158, 174));
                return;
            }
            game.World.Update(1f / 60f);
            game.ApplySceneRequest();
            game.World.Render(s, 520, 320, Font.Value);
        });

        ctx.Play("mixed3d", async d =>
        {
            await d.Snap();
            await d.Click(addBeacon);
            await d.Click(save);
            await d.Expect(() => storage.Read("scenes/arena.scene.json")!.Contains("Beacon"), "3D シーン編集を保存");
            await d.Click(play);
            await d.Step(4);
            await d.Expect(() => game!.World is Player2DWorld, "開始は 2D タイトル");
            await d.Step(20);
            await d.Expect(() => game!.ScenePath == "res://scenes/arena.scene.json", "csx が 3D シーンへ遷移");
            await d.Expect(() => game!.World is Player3DWorld, "遷移後は 3D world");
            await d.Expect(() => game!.World.Find("Beacon") is not null, "保存した 3D エンティティがプレイに出る");
            float x = game!.World.Entity(1).Pos3D.X;
            await d.Step(30);
            await d.Expect(() => game!.World.Entity(1).Pos3D.X > x + 0.45f, "3D csx が Runner を動かす");
            await d.Snap("arena");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))[
            VStack(8)[
                Heading("Luxel Studio dogfood — 2D タイトル + 3D アリーナ"),
                Muted("混在プロジェクト: startScene は 2D タイトル、csx の RequestScene で 3D アリーナへ遷移。3D エディタで追加した Beacon を保存し、Player が同じプロジェクトから読み直して実行する。"),
                HStack(6)[addBeacon, save, play, stop, Text($"{status}", 12, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(8, 5, 0, 0))],
                HStack(10)[arenaEditor, insp],
                view]];
    }
}
