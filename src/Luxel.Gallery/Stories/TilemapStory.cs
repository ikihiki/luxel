using System.Numerics;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// タイルマップ (タスク 18) のデモ — <see cref="TileMap"/> を <see cref="TileMapLayer"/> (保持型・可視チャンク実体化) で
/// 描き、ズーム/スクロールする <see cref="Camera2D"/> でワールドを覗く。地面 (grass/dirt)・浮き床・壁柱を配置。
/// プレイヤー (箱) は壁へ右移動する意図量 (アウトライン) と <see cref="TileMap.Sweep"/> で切り詰めた解決位置
/// (塗り) を並べ、AABB グリッド衝突が壁の手前で止めるのを可視化する。アトラスは手続き生成 (決定的)。
/// docs の Docs/TwoD 「タイルマップ」節から参照される。
/// </summary>
public static class TilemapStories
{
    [Story("Demos/2D/Tilemap", Height = 260, Order = 120)]
    public static Widget Tilemap(StoryContext ctx) => ctx.Snap(Frame(GpuView(384, 192, new TilemapScene(), animated: false)));

    private sealed class TilemapScene : GpuSceneBase
    {
        private const int Tile = 16, MapW = 16, MapH = 10;

        private Rasterizer2D _raster = null!;
        private GpuBuffer _atlas = null!;
        private RetainedCanvas _canvas = null!;

        protected override bool NeedsColorTarget => false;

        protected override void OnInit()
        {
            _raster = Track(new Rasterizer2D(Device));

            // --- 手続きアトラス 32×32 (16px タイル 4 種): grass / wall / dirt ---
            const int aw = 32, ah = 32;
            _atlas = Track(Device.Malloc(aw * ah * 4, GpuMemoryKind.HostMapped));
            Span<byte> px = _atlas.Span<byte>(aw * ah * 4);
            (int Ox, int Oy, byte R, byte G, byte B)[] cells =
            [
                (0, 0, 70, 175, 85),     // grass (緑)
                (16, 0, 120, 122, 135),  // wall  (石)
                (0, 16, 140, 92, 52),    // dirt  (茶)
            ];
            foreach (var (ox, oy, r, g, b) in cells)
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++)
                    {
                        int i = ((oy + y) * aw + ox + x) * 4;
                        bool edge = x == 0 || y == 0 || x == Tile - 1 || y == Tile - 1;
                        px[i] = (byte)(edge ? r * 3 / 4 : r);
                        px[i + 1] = (byte)(edge ? g * 3 / 4 : g);
                        px[i + 2] = (byte)(edge ? b * 3 / 4 : b);
                        px[i + 3] = 255;
                    }

            var atlas = new SpriteAtlas("proc://tiles", [
                new("grass", new SpriteRect(0, 0, Tile, Tile)),
                new("wall", new SpriteRect(16, 0, Tile, Tile)),
                new("dirt", new SpriteRect(0, 16, Tile, Tile)),
            ]);
            atlas.Bind(_atlas.BindlessIndex, aw, ah);

            var tileSet = new TileSet(atlas, Tile, Tile, [
                new(1, new TileDef("grass", Solid: true)),
                new(2, new TileDef("wall", Solid: true)),
                new(3, new TileDef("dirt", Solid: true)),
            ]);

            // --- マップ: 地面 (草+土)、浮き床、壁柱 ---
            var map = new TileMap(MapW, MapH, tileSet, chunkTiles: 32);
            for (int x = 0; x < MapW; x++) { map.SetTile(x, 8, 1); map.SetTile(x, 9, 3); }  // 地面
            for (int x = 3; x <= 6; x++) map.SetTile(x, 4, 2);                               // 浮き床
            for (int y = 5; y <= 7; y++) map.SetTile(10, y, 2);                              // 壁柱
            map.ClearAllDirty();

            _canvas = Track(new RetainedCanvas(_raster));

            // 空 (最背面) → タイル → プレイヤー箱 の順で子ノードを積む。
            // RetainedCanvas は塗り/ストロークの色を **node.Color** で与える (Content の色はマスク) —
            // 画像 (タイル) は node 色を無視してアトラスをサンプリングする。
            UiNode sky = _canvas.AddChild(_canvas.Root);
            sky.Content = new Scene2D().FillRect(Color2D.White, 0, 0, MapW * Tile, MapH * Tile);
            sky.Color = Color2D.Rgba(150, 195, 235);

            var layer = new TileMapLayer(_canvas, _canvas.Root, map);
            layer.Update(new RectF(0, 0, MapW * Tile, MapH * Tile));   // 全チャンク可視 (小マップ)

            // プレイヤー: (120,112) から右へ +60 動かす → 壁柱 (world x160) 手前で止まる
            var box = new RectF(120, 112, 12, 16);
            var intended = new Vector2(60, 0);
            Vector2 moved = map.Sweep(box, intended, out _, out _);

            UiNode target = _canvas.AddChild(_canvas.Root);   // 意図した移動先 (アウトライン)
            target.Content = new Scene2D().StrokeRoundedRect(Color2D.White, 1.5f, box.X + intended.X, box.Y, box.W, box.H, 3);
            target.Color = Color2D.Rgba(70, 70, 85);

            UiNode player = _canvas.AddChild(_canvas.Root);   // Sweep 解決位置 (塗り)
            player.Content = new Scene2D().FillRoundedRect(Color2D.White, box.X + moved.X, box.Y, box.W, box.H, 3);
            player.Color = Color2D.Rgba(240, 90, 110);
        }

        protected override void OnRender(float time)
        {
            // ズーム 2倍・(150,104) を画面中心に (地面・壁柱・浮き床・プレイヤーが収まる)
            Camera2D cam = Camera2D.Create(2f, new Vector2(150, 104), W, H);
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            _canvas.Render(cmd, cam, W, H, OutBuffer);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }
}
