using System.Numerics;
using Luxel.Controls;
using Luxel.SceneEdit;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>SceneEditorView — シーンエディタ (ADR-0016 / ToDo 27 GE-1) のビュー。エンティティの選択/移動/複製/削除を
/// 編集する。編集意味論は canvas 非依存の Luxel.SceneEdit (Transaction スタック 3 本目)、空間の知識 (座標変換/ヒット/
/// カメラ/描画) は ISceneSpaceAdapter に閉じる — M11 は 2D アダプタ、3D アダプタは M12 でシェル無改修で足す。</summary>
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

    [Story("Controls/SceneEditorView/Basic", Height = 440)]
    public static Widget Basic(StoryContext ctx)
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

    [Story("Controls/SceneEditorView/Tiles", Height = 470, Order = 2)]
    public static Widget Tiles(StoryContext ctx)
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
}
