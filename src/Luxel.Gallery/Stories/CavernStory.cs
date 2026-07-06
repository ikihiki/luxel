using System.Numerics;
using LuxelCavern.Core;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **capstone ① 「Luxel Cavern」** (タスク 19 ステージ B) のゲームプレイ土台 — タイルマップ + プレイヤー
/// 走り/ジャンプ (<see cref="CavernSim"/> の Sweep 衝突) + カメラ追従。sim を固定 dt で決定的に事前実行し、
/// <see cref="TileMapLayer"/> + プレイヤーノードを <see cref="CameraRig2D"/> 風に追従カメラで描く (golden)。
/// 実時間 GameScene/exe ラッパ・敵/収集/セーブ/HUD/Audio は後段。アトラスは手続き生成 (外部アセット不要)。
/// </summary>
public static class CavernStories
{
    private const int Steps = 78;   // 事前実行フレーム数 (固定 dt)

    [Story("Game/Cavern", Height = 300, Order = 145)]
    public static Widget Cavern(StoryContext ctx) => ctx.Snap(Frame(GpuView(384, 256, new CavernScene(), animated: false)));

    private sealed class CavernScene : GpuSceneBase
    {
        private const int Tile = CavernLevel.Tile;
        private Rasterizer2D _raster = null!;
        private GpuBuffer _atlasBuf = null!;
        private RetainedCanvas _canvas = null!;
        private Vector2 _cameraCenter;

        protected override bool NeedsColorTarget => false;

        protected override void OnInit()
        {
            _raster = Track(new Rasterizer2D(Device));

            // --- 手続きアトラス 32×32 (grass=(0,0) / wall=(16,0)? → CavernLevel の矩形に合わせる: grass(0,0)/dirt(16,0)/wall(0,16)) ---
            const int aw = 32, ah = 32;
            _atlasBuf = Track(Device.Malloc(aw * ah * 4, GpuMemoryKind.HostMapped));
            Span<byte> px = _atlasBuf.Span<byte>(aw * ah * 4);
            (int Ox, int Oy, byte R, byte G, byte B)[] cells =
            [
                (0, 0, 70, 175, 85),      // grass (緑)
                (16, 0, 140, 92, 52),     // dirt  (茶)
                (0, 16, 120, 122, 135),   // wall  (石)
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

            SpriteAtlas atlas = CavernLevel.BuildAtlas();
            atlas.Bind(_atlasBuf.BindlessIndex, aw, ah);
            TileSet tileSet = CavernLevel.BuildTileSet(atlas);
            TileMap map = CavernLevel.Build(tileSet);

            // --- プレイヤー物理を固定 dt で事前実行 (走って壁の手前でジャンプ) ---
            var sim = new CavernSim(map, CavernLevel.Spawn, new Vector2(12, 22));
            for (int f = 0; f < Steps; f++)
            {
                float moveX = f >= 12 ? 1f : 0f;   // 落下後に右へ走る
                bool jump = f == 30;               // 途中で一度ジャンプ (snap までに着地)
                sim.Step(1f / 60, moveX, jump);
            }
            _cameraCenter = sim.PlayerCenter;

            _canvas = Track(new RetainedCanvas(_raster));

            // 空 (最背面)
            UiNode sky = _canvas.AddChild(_canvas.Root);
            sky.Content = new Scene2D().FillRect(Color2D.White, 0, 0, CavernLevel.Width * Tile, CavernLevel.Height * Tile);
            sky.Color = Color2D.Rgba(120, 170, 220);

            // タイル (可視チャンク)
            var layer = new TileMapLayer(_canvas, _canvas.Root, map);
            layer.Update(new RectF(0, 0, CavernLevel.Width * Tile, CavernLevel.Height * Tile));

            // プレイヤー箱
            UiNode player = _canvas.AddChild(_canvas.Root);
            player.Content = new Scene2D().FillRoundedRect(Color2D.White,
                sim.PlayerPos.X, sim.PlayerPos.Y, sim.PlayerSize.X, sim.PlayerSize.Y, 3);
            player.Color = Color2D.Rgba(90, 170, 245);
        }

        protected override void OnRender(float time)
        {
            Camera2D cam = Camera2D.Create(2f, _cameraCenter, W, H);
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            _canvas.Render(cmd, cam, W, H, OutBuffer);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }
}
