using System.Numerics;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

using Luxel.Typography.TwoD;
namespace Luxel.Gallery.Stories;

/// <summary>
/// 2D ベクターの描画デモ — compute ラスタライザ (三角形分割なし) が塗る EvenOdd パス、
/// ストローク、ベクターテキスト (日本語含む)。
/// </summary>
public static class Vector2DStories
{
    private static readonly Lazy<VectorFont> EnFont = new(() => GalleryFonts.Load(GalleryFonts.Regular));
    private static readonly Lazy<VectorFont> JpFont = new(() => GalleryFonts.Load(GalleryFonts.Regular));

    /// <summary>地図風シーン (日本語ラベル) + ズーム knob。ベクターなので拡大しても
    /// エッジが崩れない (実アプリでは Camera2D — 再エンコードなしのスムーズズーム)。</summary>
    [Story("Examples/2D/Map", Width = 560, Height = 460, Order = 113)]
    public static Widget Map(StoryContext ctx)
    {
        Signal<float> zoom = ctx.Signal("zoom", 1f, "拡大率 (1 = 等倍、(300,150) 中心)");
        ctx.Play(static d => d.Snap());
        return Frame(Canvas2D(512, 384, animate: (s, _) =>
        {
            float z = MathF.Max(0.2f, zoom.Value);
            var c = new Vector2(300, 150);                          // ズーム中心 (z=1 で恒等)
            Vector2 T(float x, float y) => c + (new Vector2(x, y) - c) * z;
            void Poly(uint color, float width, params Vector2[] pts)
                => s.StrokePolyline(color, width * z, pts.Select(p => T(p.X, p.Y)).ToArray());

            s.FillRect(Color2D.Rgba(238, 236, 228), 0, 0, 512, 384);   // 背景 (ベージュ)
            // 川 (青の太いストローク)
            uint water = Color2D.Rgba(150, 190, 230);
            Poly(water, 26, new(-10, 90), new(120, 140), new(260, 120), new(400, 180), new(530, 150));
            // 公園 (緑の角丸)
            Vector2 park = T(40, 215);
            s.FillRoundedRect(Color2D.Rgba(180, 215, 160), park.X, park.Y, 150 * z, 120 * z, 18 * z);
            // 街区 (灰色の矩形グリッド)
            uint block = Color2D.Rgba(222, 220, 212);
            for (int gx = 0; gx < 4; gx++)
                for (int gy = 0; gy < 2; gy++)
                {
                    Vector2 b = T(230 + gx * 70, 220 + gy * 75);
                    s.FillRect(block, b.X, b.Y, 56 * z, 60 * z);
                }
            // 道路: 太い幹線 (灰の縁取り→白) と細い道
            uint roadEdge = Color2D.Rgba(200, 200, 196), road = Color2D.White;
            Vector2[] main = [new(40, 60), new(200, 110), new(360, 90), new(500, 140)];
            Poly(roadEdge, 18, main);
            Poly(road, 12, main);
            Vector2[] main2 = [new(150, 360), new(180, 200), new(300, 120)];
            Poly(roadEdge, 16, main2);
            Poly(road, 10, main2);
            for (int i = 0; i < 5; i++)
                Poly(Color2D.Rgba(228, 226, 218), 4, new(230, 215 + i * 38), new(510, 215 + i * 38));
            // 湖 (街区の上に重ねる — painter's order)
            Vector2 lake = T(440, 305);
            s.FillCircle(water, lake.X, lake.Y, 55 * z);

            // 日本語ラベル + ラテン (ベクターテキスト — ズームしてもアウトラインのまま)
            void Label(VectorFont f, string text, float x, float y, float size, uint color)
            { Vector2 p = T(x, y); f.AppendText(s, text, p.X, p.Y, size * z, color); }
            Label(JpFont.Value, "中央公園", 60, 285, 22, Color2D.Rgba(60, 95, 55));
            Label(JpFont.Value, "東川", 250, 110, 24, Color2D.Rgba(45, 85, 130));
            Label(JpFont.Value, "本町", 250, 255, 20, Color2D.Rgba(60, 60, 60));
            Label(JpFont.Value, "みなと湖", 392, 300, 20, Color2D.Rgba(45, 85, 130));
            Label(EnFont.Value, "Luxel Map", 16, 30, 24, Color2D.Rgba(90, 90, 100));
        }));
    }

    private static void AddCircle(Scene2D s, float cx, float cy, float r, int segments = 64)
    {
        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)segments * MathF.Tau;
            float x = cx + MathF.Cos(t) * r, y = cy + MathF.Sin(t) * r;
            if (i == 0) s.MoveTo(x, y); else s.LineTo(x, y);
        }
        s.Close();
    }
}
