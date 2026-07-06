using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// スプライトアトラス (タスク 18) のデモ — 1 枚のアトラスバッファから <see cref="SpriteAtlas.DrawSprite"/> で
/// **サブ矩形** を選んで複数スプライトを描く (GpuPath の image シェイプ + srcX/srcY 原点オフセット)。
/// 上段: 4 セルを別々のスプライトとして拡大描画 (各セルが別領域からサンプリングされる証拠)。
/// 下段: フレーム列 (フィルムストリップ) + <see cref="SpriteAnimation"/> で選んだ現フレームを拡大。
/// アトラスは手続き生成 (外部アセット不要・決定的)。Image シェイプは GPU 専用 (Skia CPU は非対応) のため
/// golden は vk/dx のみ。docs の Docs/TwoD 「スプライトアトラス」節から参照される。
/// </summary>
public static class SpriteStories
{
    [Story("Demos/TwoD/Sprites", Height = 260, Order = 119)]
    public static Widget Sprites(StoryContext ctx) => ctx.Snap(Frame(GpuView(384, 192, new SpriteScene(), animated: false)));

    private sealed class SpriteScene : GpuSceneBase
    {
        private const int AtlasW = 64, AtlasH = 64, Cell = 32;

        private Rasterizer2D _raster = null!;
        private GpuBuffer _atlas = null!;
        private EncodedScene _encoded = null!;

        protected override bool NeedsColorTarget => false;   // Scene2D を直接 OutBuffer へラスタライズ

        protected override void OnInit()
        {
            _raster = Track(new Rasterizer2D(Device));

            // --- 手続きアトラス: 2×2 の 32px セル。各セルは基色 + 暗い枠 + フレーム毎に位置が違う白マーカ ---
            _atlas = Track(Device.Malloc(AtlasW * AtlasH * 4, GpuMemoryKind.HostMapped));
            Span<byte> px = _atlas.Span<byte>(AtlasW * AtlasH * 4);
            (byte R, byte G, byte B)[] baseCol = [(60, 130, 240), (230, 80, 100), (40, 200, 120), (235, 200, 50)];
            (int X, int Y)[] org = [(0, 0), (Cell, 0), (0, Cell), (Cell, Cell)];
            (int X, int Y)[] marker = [(2, 2), (Cell - 10, 2), (Cell - 10, Cell - 10), (2, Cell - 10)];
            for (int f = 0; f < 4; f++)
            {
                for (int y = 0; y < Cell; y++)
                    for (int x = 0; x < Cell; x++)
                    {
                        int ax = org[f].X + x, ay = org[f].Y + y, i = (ay * AtlasW + ax) * 4;
                        byte r = baseCol[f].R, g = baseCol[f].G, b = baseCol[f].B;
                        bool border = x < 2 || y < 2 || x >= Cell - 2 || y >= Cell - 2;
                        bool mark = x >= marker[f].X && x < marker[f].X + 8 && y >= marker[f].Y && y < marker[f].Y + 8;
                        if (border) { r = 20; g = 24; b = 30; }
                        if (mark) { r = 245; g = 245; b = 245; }
                        px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = 255;
                    }
            }

            var atlas = new SpriteAtlas("proc://sprites", [
                new("f_0", new SpriteRect(0, 0, Cell, Cell)),
                new("f_1", new SpriteRect(Cell, 0, Cell, Cell)),
                new("f_2", new SpriteRect(0, Cell, Cell, Cell)),
                new("f_3", new SpriteRect(Cell, Cell, Cell, Cell)),
            ]);
            atlas.Bind(_atlas.BindlessIndex, AtlasW, AtlasH);

            var scene = new Scene2D();
            scene.FillRect(Color2D.Rgba(28, 32, 40), 0, 0, W, H);   // 暗い背景 (合成は白背景の上)

            // 上段: 4 セルを別スプライトとして 2 倍描画 (16..352)
            for (int f = 0; f < 4; f++)
                scene.DrawSprite(atlas, $"f_{f}", 16 + f * 90, 16, scale: 2f);

            // 下段左: フレーム列 (等倍フィルムストリップ)
            for (int f = 0; f < 4; f++)
                scene.DrawSprite(atlas, $"f_{f}", 16 + f * 40, 120, scale: 1f);

            // 下段右: SpriteAnimation で選んだ現フレームを拡大 (8fps, 0.30s → floor(2.4)=frame 2)
            var anim = new SpriteAnimation("f_", frameCount: 4, fps: 8f);
            anim.Update(0.30f);
            scene.DrawSprite(atlas, anim, 288, 108, scale: 2.5f);

            _encoded = Track(_raster.Encode(scene));
        }

        protected override void OnRender(float time)
        {
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            _raster.Render(cmd, _encoded, Camera2D.Pixels, W, H, OutBuffer);
            cmd.Finish();
            Device.MainQueue.SubmitAndWait(cmd);
        }
    }
}
