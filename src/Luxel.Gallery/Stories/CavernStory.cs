using System.Numerics;
using LuxelCavern.Core;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// **capstone ① 「Luxel Cavern」** (タスク 19 ステージ B) — タイルマップ + プレイヤー物理 (走り/ジャンプ/Sweep 衝突) +
/// カメラ追従 + **収集物 (コイン/鍵)・扉・トゲ・巡回敵** (<see cref="CavernSim"/>)。sim を固定 dt で決定的に事前実行し、
/// タイル + エンティティ + プレイヤーを追従カメラで描く (golden)。手続きアトラス (外部アセット不要)。
/// HUD (日本語)・実時間 exe・Audio・パーティクル演出・セーブは後段。
/// </summary>
public static class CavernStories
{
    private const int Steps = 78;

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

            // --- 手続きアトラス 32×32 (grass(0,0)/dirt(16,0)/wall(0,16)/spike(16,16)) ---
            const int aw = 32, ah = 32;
            _atlasBuf = Track(Device.Malloc(aw * ah * 4, GpuMemoryKind.HostMapped));
            Span<byte> px = _atlasBuf.Span<byte>(aw * ah * 4);
            (int Ox, int Oy, byte R, byte G, byte B)[] cells =
            [
                (0, 0, 70, 175, 85),      // grass
                (16, 0, 140, 92, 52),     // dirt
                (0, 16, 120, 122, 135),   // wall
                (16, 16, 210, 70, 70),    // spike (赤)
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

            CavernSim sim = CavernLevel.CreateSim();
            sim.Map.TileSet.Atlas.Bind(_atlasBuf.BindlessIndex, aw, ah);

            // プレイヤー物理を固定 dt で事前実行 (落下 → 右へ走ってコインを拾う)
            for (int f = 0; f < Steps; f++)
                sim.Step(1f / 60, f >= 12 ? 1f : 0f, jumpPressed: false);
            _cameraCenter = sim.PlayerCenter;

            _canvas = Track(new RetainedCanvas(_raster));

            UiNode sky = _canvas.AddChild(_canvas.Root);
            sky.Content = new Scene2D().FillRect(Color2D.White, 0, 0, CavernLevel.Width * Tile, CavernLevel.Height * Tile);
            sky.Color = Color2D.Rgba(120, 170, 220);

            var layer = new TileMapLayer(_canvas, _canvas.Root, sim.Map);
            layer.Update(new RectF(0, 0, CavernLevel.Width * Tile, CavernLevel.Height * Tile));

            // エンティティ + プレイヤー (per-shape 色 = ContentColors)
            UiNode ents = _canvas.AddChild(_canvas.Root);
            ents.ContentColors = true;
            var es = new Scene2D();

            uint door = sim.DoorOpen ? Color2D.Rgba(90, 200, 120) : Color2D.Rgba(120, 80, 50);
            es.FillRoundedRect(door, sim.DoorPos.X, sim.DoorPos.Y, sim.DoorSize.X, sim.DoorSize.Y, 3);

            foreach (Pickup p in sim.Pickups)
            {
                if (p.Collected) continue;
                uint c = p.IsKey ? Color2D.Rgba(240, 210, 90) : Color2D.Rgba(250, 225, 70);
                es.FillCircle(c, p.Pos.X + p.Size * 0.5f, p.Pos.Y + p.Size * 0.5f, p.Size * 0.5f, 12);
            }
            foreach (Walker w in sim.Enemies)
                if (w.Alive)
                    es.FillRoundedRect(Color2D.Rgba(220, 80, 90), w.Pos.X, w.Pos.Y, w.Size.X, w.Size.Y, 2);

            es.FillRoundedRect(Color2D.Rgba(90, 170, 245), sim.PlayerPos.X, sim.PlayerPos.Y, sim.PlayerSize.X, sim.PlayerSize.Y, 3);
            ents.Content = es;
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
