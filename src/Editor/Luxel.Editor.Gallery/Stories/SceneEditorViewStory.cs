using System.Numerics;
using Luxel.Controls;
using Luxel.SceneEdit;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;
using static Luxel.Controls.EditorKit;
using static Luxel.Editor.Gallery.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>SceneEditorView — シーンエディタ (ADR-0016 / ToDo 27 GE-1) のビュー。エンティティの選択/移動/複製/削除を
/// 編集する。編集意味論は canvas 非依存の Luxel.SceneEdit (Transaction スタック 3 本目)、空間の知識 (座標変換/ヒット/
/// カメラ/描画) は ISceneSpaceAdapter に閉じる — M11 は 2D アダプタ、3D アダプタは M12 でシェル無改修で足す。</summary>
[StoryMeta("Controls/Editor/SceneEditorView")]
public static class SceneEditorViewStory
{
    // Player / Enemy / Coin の 3 エンティティ (transform2d のみ — 見た目はプレースホルダボックス)
    private static SceneDoc SampleScene()
    {
        SceneEntity E(int id, string name, float x, float y)
            => SceneEntity.Of(id, name, SceneSchemas.NewComponent(SceneSchemas.Transform2D)
                .With("pos", SceneValue.Of(new Vector2(x, y))));
        return SceneDoc.Of(SceneSpace.TwoD, [E(1, "Player", 120, 100), E(2, "Enemy", 300, 180), E(3, "Coin", 470, 120)]);
    }

    [Story]
    public static StoryResult Basic(StoryContext ctx)
    {
        SceneEditorView ed = SceneEditorView(source: SampleScene(), viewWidth: 620f, viewHeight: 360f);

        ctx.Play("basic", async d =>
        {
            await d.Snap();                              // 3 エンティティ + グリッド
            Vector2 p2 = ed.EntityScreenCenter(2);
            await d.Click(p2.X, p2.Y);                   // Enemy を選択 → 軸ハンドルが出る
            await d.Expect(() => ed.IsSelected(2), "クリックで選択");
            await d.Snap("selected");
            Vector2 from = ed.EntityScreenCenter(2);
            await d.Drag(from.X, from.Y, from.X + 60, from.Y + 60);   // 本体ドラッグ = 自由移動
            await d.Expect(() => ed.EntityPos2D(2) == new Vector2(360, 240), "ドラッグで移動");
            await d.Snap("moved");
            await d.Key(Key.Z, ctrl: true);
            await d.Expect(() => ed.EntityPos2D(2) == new Vector2(300, 180), "undo で戻る (1 undo)");
        });

        ctx.Play("handle", async d =>
        {
            Vector2 p1 = ed.EntityScreenCenter(1);
            await d.Click(p1.X, p1.Y);                   // Player を選択
            await d.Expect(() => ed.IsSelected(1), "選択でハンドル表示");
            // X 軸ハンドル (中心の右 30px) を掴んで斜めにドラッグ → X だけ動く
            await d.Drag(p1.X + 30, p1.Y, p1.X + 80, p1.Y + 40);
            await d.Expect(() => ed.EntityPos2D(1) == new Vector2(170, 100), "X 軸拘束移動 (Y 不変)");
            await d.Snap("axis");
        });

        ctx.Play("marquee", async d =>
        {
            // 空白から Player + Enemy を囲む範囲選択 (Coin は外)
            Vector2 a = ed.ClientOf(new Vector2(10, 10));
            Vector2 b = ed.ClientOf(new Vector2(400, 250));
            await d.Drag(a.X, a.Y, b.X, b.Y);
            await d.Expect(() => ed.SelectionCount == 2, "範囲選択で 2 エンティティ");
            await d.Snap("box");
        });

        ctx.Play("keys", async d =>
        {
            Vector2 p3 = ed.EntityScreenCenter(3);
            await d.Click(p3.X, p3.Y);                   // フォーカス
            await d.Key(Key.A, ctrl: true);
            await d.Expect(() => ed.SelectionCount == 3, "Ctrl+A で全選択");
            await d.Key(Key.Delete);
            await d.Expect(() => ed.EntityCount == 0, "Delete で削除 (1 undo)");
            await d.Key(Key.Z, ctrl: true);
            await d.Expect(() => ed.EntityCount == 3, "undo で復活 (選択も戻る)");
            await d.Key(Key.D, ctrl: true);              // 複製 (+24,+24 オフセット)
            await d.Expect(() => ed.EntityCount == 6, "Ctrl+D で複製");
            await d.Snap("duplicated");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("SceneEditorView (シーンエディタ)"),
                Muted("Luxel.SceneEdit (不変 + Transaction) を空間アダプタ経由で描く薄いシェル。クリック/範囲選択・本体ドラッグ移動・軸ハンドル (X=赤/Y=緑)・Ctrl+D 複製・ホイールズーム・中ボタン pan。"),
                ed]];
    }

    // タイルレイヤ 18x9 (セル 32): 地面 1 行 + 石 2 個
    private static SceneDoc TileScene()
    {
        var cells = new int[18 * 9];
        for (int x = 0; x < 18; x++) cells[8 * 18 + x] = 1;   // 最下段 = 草
        cells[7 * 18 + 4] = 3; cells[7 * 18 + 5] = 3;          // 石
        return SceneDoc.Of(SceneSpace.TwoD, [],
            [TileLayer.Of(1, "ground", "res://atlas/tiles.json", 32, 18, 9, cells)]);
    }

    [Story]
    public static StoryResult Tiles(StoryContext ctx)
    {
        SceneEditorView ed = SceneEditorView(source: TileScene(), viewWidth: 620f, viewHeight: 320f);
        Signal<string> status = ctx.Signal("status", "ツール: 選択 / タイル: 草");

        Button Tool(string label, SceneTool tool) => Button(_ =>
        {
            ed.Tool = tool;
            status.Value = $"ツール: {label} / タイル: {ed.ActiveTile}";
        }, label);
        Button Tile(string label, int tile) => Button(_ =>
        {
            ed.ActiveTile = tile;
            status.Value = $"ツール: {ed.Tool} / タイル: {label}";
        }, label);

        Button brush = Tool("ブラシ", SceneTool.Brush), rect = Tool("矩形", SceneTool.Rect),
               erase = Tool("消しゴム", SceneTool.Eraser), pick = Tool("スポイト", SceneTool.Picker),
               select = Tool("選択", SceneTool.Select);
        Button grass = Tile("草", 1), dirt = Tile("土", 2), stone = Tile("石", 3), gold = Tile("金", 4);

        ctx.Play("brush", async d =>
        {
            await d.Snap();                              // 初期: 地面 1 行 + 石 2 個 + レイヤ境界
            await d.Click(dirt);
            await d.Click(brush);
            Vector2 a = ed.CellClient(2, 3), b = ed.CellClient(8, 5);
            await d.Drag(a.X, a.Y, b.X, b.Y);            // ブラシで斜めに 1 ストローク
            await d.Expect(() => ed.TileAt(2, 3) == 2 && ed.TileAt(8, 5) == 2 && ed.TileAt(5, 4) == 2, "ブラシは補間しながら塗る");
            await d.Snap("painted");
            await d.Key(Key.Z, ctrl: true);
            await d.Expect(() => ed.TileAt(2, 3) == 0 && ed.TileAt(8, 5) == 0, "ストローク = 1 undo");
        });

        ctx.Play("rect", async d =>
        {
            await d.Click(stone);
            await d.Click(rect);
            Vector2 a = ed.CellClient(11, 2), b = ed.CellClient(15, 5);
            await d.Drag(a.X, a.Y, b.X, b.Y);            // 矩形塗り潰し
            await d.Expect(() => ed.TileAt(11, 2) == 3 && ed.TileAt(15, 5) == 3 && ed.TileAt(13, 3) == 3, "矩形ツールで塗り潰し");
            await d.Snap("block");
        });

        ctx.Play("pick-erase", async d =>
        {
            await d.Click(pick);
            Vector2 g = ed.CellClient(0, 8);             // 地面 (草=1) をスポイト
            await d.Click(g.X, g.Y);
            await d.Expect(() => ed.ActiveTile == 1, "スポイトでタイルを取る");
            await d.Click(erase);
            Vector2 a = ed.CellClient(2, 8), b = ed.CellClient(5, 8);
            await d.Drag(a.X, a.Y, b.X, b.Y);            // 地面を消す
            await d.Expect(() => ed.TileAt(3, 8) == 0 && ed.TileAt(0, 8) == 1, "消しゴムで 0 に");
            await d.Snap("erased");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("SceneEditorView — タイル描き込み"),
                Muted("PaintTiles change (1 ストローク = 1 undo)。ブラシは前回セルから直線補間、矩形は範囲塗り潰し、スポイトはセルのタイルを ActiveTile へ。タイル色はエディタ用プレースホルダ (実アトラスは GE-2 で配線)。"),
                HStack(6)[select, brush, rect, erase, pick, grass, dirt, stone, gold],
                Text($"{status}", 13, color: Bind.From(() => UiTheme.T.TextMuted)),
                ed]];
    }

    private static SceneDoc SampleScene3D()
    {
        SceneEntity E(int id, string name, float x, float y, float z, Vector3? scale = null)
            => SceneEntity.Of(id, name, SceneSchemas.NewComponent(SceneSchemas.Transform3D)
                .With("pos", SceneValue.Of(new Vector3(x, y, z)))
                .With("scale", SceneValue.Of(scale ?? Vector3.One)));
        return SceneDoc.Of(SceneSpace.ThreeD,
            [E(1, "Player", 0, 0, 0), E(2, "Crate", 2.2f, 0, 0.6f), E(3, "Gate", -2.1f, 0, -1.4f, new Vector3(1.4f, 1.5f, 0.4f))]);
    }

    [Story]
    public static StoryResult ThreeD(StoryContext ctx)
    {
        SceneEditorView ed = SceneEditorView(source: SampleScene3D(), viewWidth: 620f, viewHeight: 360f);

        ctx.Play("basic", async d =>
        {
            await d.Snap();                              // 地面グリッド + transform3d AABB
            ed.Pan(new Vector2(36, -18));                // 3D では Pan API = OrbitCamera orbit
            await d.Step(1);
            await d.Snap("orbit");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            VStack(10)[
                Heading("SceneEditorView — 3D 空間アダプタ"),
                Muted("同じ SceneEditorView シェルに SceneSpace3DAdapter を差し込み、OrbitCamera 投影でグリッド/AABB/XYZ ハンドルを描く。中ボタンドラッグは orbit、ホイールは dolly。"),
                ed]];
    }

    // ゲーム固有スキーマの例 (敵 AI + 着色) — 登録するだけでインスペクタに出る
    private static readonly IComponentSchema EnemySchema = new ComponentSchema(
        "enemy", "Enemy AI", SceneSpaces.TwoD,
        [
            new SceneFieldDef("speed", SceneFieldType.Float, SceneValue.Of(60f)),
            new SceneFieldDef("patrol", SceneFieldType.Bool, SceneValue.Of(false)),
            new SceneFieldDef("mode", SceneFieldType.Enum, SceneValue.Of("idle"), ["idle", "chase"]),
        ]);

    private static readonly IComponentSchema TintSchema = new ComponentSchema(
        "tint", "Tint", SceneSpaces.Both,
        [new SceneFieldDef("color", SceneFieldType.Color, SceneValue.Of(new Vector4(1, 1, 1, 1)))]);

    // snap は幅 480 なので、golden に写したいもの (インスペクタ) を左に置く
    [Story]
    public static StoryResult Inspector(StoryContext ctx)
    {
        SchemaRegistry reg = SceneSchemas.BuiltIns().Add(EnemySchema).Add(TintSchema);

        SceneEntity E(int id, string name, float x, float y, params SceneComponent[] extra)
            => SceneEntity.Of(id, name, extra.Prepend(
                SceneSchemas.NewComponent(SceneSchemas.Transform2D).With("pos", SceneValue.Of(new Vector2(x, y)))));
        var scene = SceneDoc.Of(SceneSpace.TwoD,
            [E(1, "Player", 100, 90), E(2, "Enemy", 180, 190, SceneSchemas.NewComponent(EnemySchema))]);

        SceneEditorView ed = SceneEditorView(source: scene, viewWidth: 300f, viewHeight: 350f);
        SceneInspector insp = SceneInspector(editor: ed, schemas: reg, width: 230f);

        ctx.Play("inspect", async d =>
        {
            Vector2 p2 = ed.EntityScreenCenter(2);
            await d.Click(p2.X, p2.Y);                   // Enemy を選択 → インスペクタに 2 コンポーネント
            await d.Expect(() => insp.EditorOf("enemy", "patrol") is not null, "スキーマの行が出る");
            await d.Snap();
            await d.Click(insp.EditorOf("enemy", "patrol")!);   // Check をトグル = SetField 1 undo
            bool Patrol() => ed.Scene.Doc.Entity(2).Component("enemy")!.Get("patrol")!.Value.AsBool();
            await d.Expect(() => Patrol(), "インスペクタ編集が Transaction で反映");
            await d.Snap("edited");
            await d.Click(p2.X, p2.Y);                   // キャンバスへフォーカス
            await d.Key(Key.Z, ctrl: true);
            await d.Expect(() => !Patrol(), "インスペクタ編集も undo できる");
        });

        ctx.Play("components", async d =>
        {
            Vector2 p1 = ed.EntityScreenCenter(1);
            await d.Click(p1.X, p1.Y);                   // Player を選択
            insp.AddComponent("tint");                   // 追加ボタンと同じ経路
            await d.Step(1);
            await d.Expect(() => ed.Scene.Doc.Entity(1).Component("tint") is not null, "コンポーネント追加");
            await d.Snap("added");
            insp.RemoveComponent("tint");
            await d.Step(1);
            await d.Expect(() => ed.Scene.Doc.Entity(1).Component("tint") is null, "× で削除");
            await d.Key(Key.Z, ctrl: true);              // 削除も undo できる (フォーカスはキャンバスのまま)
            await d.Expect(() => ed.Scene.Doc.Entity(1).Component("tint") is not null, "削除の undo");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))[
            VStack(8)[
                Heading("SceneEditorView — インスペクタ"),
                Muted("SceneInspector = IComponentSchema 駆動 (space で出し分け)。編集は ApplyEdit の Transaction 経由なので undo 可。スキーマ外のコンポーネントは読み取り表示で保全。"),
                HStack(12)[insp, ed]]];
    }

    [Story]
    public static StoryResult Assets(StoryContext ctx)
    {
        // プロジェクトフォルダ相当 (golden は実 FS watch を持ち込まない = MemoryFileStorage)
        var storage = new MemoryFileStorage();
        storage.Write("project.luxel", GameProjectJson.Serialize(new GameProject("デモ", "res://scenes/main.scene.json")));
        storage.Write("scenes/main.scene.json", SceneJson.Serialize(SceneDoc.Empty(SceneSpace.TwoD)));
        storage.Write("assets/tiles.png", "(png)");
        storage.Write("atlas/tiles.atlas.json", AtlasDefJson.Serialize(
            new AtlasDef { Image = "res://assets/tiles.png", TileWidth = 16, TileHeight = 16 }));
        storage.Write("sfx/coin.wav", "(wav)");

        // アトラス定義エディタ (最小): 開いた *.atlas.json を PropertyGrid で編集 → 変更のたび決定的 JSON で保存
        AtlasDef? atlasDef = null;
        Signal<string> atlasPath = ctx.Signal("atlasPath", "(atlas 未選択)");
        PropertyGrid atlasGrid = PropertyGrid(width: 210f, onChanged: (_, _, _) =>
        {
            if (atlasDef is not null && storage.Exists(atlasPath.Peek()))
                storage.Write(atlasPath.Peek(), AtlasDefJson.Serialize(atlasDef));
        });
        void OpenAtlas(string path)
        {
            atlasDef = AtlasDefJson.Deserialize(storage.Read(path)!);
            atlasPath.Value = path;
            atlasGrid.Target.SetBase(new Bindable<object?>(() => atlasDef));
            atlasGrid.Refresh();
        }

        AssetBrowser browser = AssetBrowser(storage: storage,
            expanded: new HashSet<string> { "assets", "atlas", "scenes", "sfx" },
            onOpen: (_, p) => { if (p.EndsWith(".atlas.json")) OpenAtlas(p); });

        // 取り込み (v1: ファイルドロップ API が無いのでパス入力 → ファイル作成で代替)
        Signal<string> importName = ctx.Signal("importName", "assets/hero.png");
        Button importBtn = Button(_ =>
        {
            string p = importName.Peek().Trim();
            if (p.Length > 0 && !storage.Exists(p)) { storage.Write(p, ""); browser.Refresh(); }
        }, "取り込み");

        ctx.Play("atlas", async d =>
        {
            await d.Snap();                              // ブラウザ + 未選択の atlas ペイン
            OpenAtlas("atlas/tiles.atlas.json");         // ブラウザのクリックと同じ経路
            await d.Step(1);
            await d.Expect(() => atlasGrid.EditorOf("TileWidth") is not null, "atlas 定義が PropertyGrid に出る");
            await d.Snap("opened");
            await d.Click(atlasGrid.EditorOf("TileWidth")!);
            await d.Key(Key.End);
            await d.Type("0");                           // 16 → 160
            await d.Expect(() => storage.Read("atlas/tiles.atlas.json")!.Contains("\"tileWidth\": 160"),
                "編集のたび決定的 JSON で保存");
            await d.Snap("saved");
        });

        ctx.Play("import", async d =>
        {
            await d.Expect(() => !storage.Exists("assets/hero.png"), "取り込み前は無い");
            await d.Click(importBtn);
            await d.Expect(() => storage.Exists("assets/hero.png"), "パス入力の取り込みで追加");
            await d.Snap("imported");
        });

        return Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(16))[
            VStack(8)[
                Heading("SceneEditorView — アセット + atlas 定義"),
                Muted("AssetBrowser = IFileStorage.List のツリー。*.atlas.json を PropertyGrid で編集し AtlasDefJson (決定的) で保存。取り込みは v1 はパス入力 (ファイルドロップ API なし)。"),
                HStack(12)[
                    VStack(6)[
                        Muted("アセット"),
                        browser,
                        HStack(6)[TextField(importName, width: 110), importBtn]],
                    VStack(6)[
                        Text($"{atlasPath}", 11, color: Bind.From(() => UiTheme.T.TextMuted)),
                        atlasGrid]]]];
    }
}
