using System.Numerics;
using Luxel.Graphics.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>Learn/Graphics/2D から埋め込む browser-WASM 対応の決定的な 2D Widget Story。</summary>
public static class TwoDBrowserStories
{
    private const string BrowserNote = "Runs through the Gallery browser-WASM WebGPU runtime.";

    [Story("Examples/2D/SceneRender", Width = 480, Height = 300, Order = 109, CapabilityNote = BrowserNote)]
    public static Widget SceneRender(StoryContext ctx) => Snapshot(ctx, Canvas2D(420, 220, draw: scene =>
    {
        scene.FillRect(Color2D.Rgba(18, 24, 38), 0, 0, 420, 220);
        scene.FillRoundedRect(Color2D.Rgba(47, 111, 237), 28, 30, 150, 86, 18);
        scene.FillCircle(Color2D.Rgba(245, 180, 55), 276, 76, 46);
        scene.BeginFill(Color2D.Rgba(236, 72, 100)).MoveTo(92, 142).LineTo(172, 204).LineTo(26, 204).Close().End();
        scene.StrokeLine(Color2D.Rgba(90, 210, 150), 6, 216, 190, 382, 138);
    }));

    [Story("Examples/2D/Shapes", Width = 480, Height = 300, Order = 110, CapabilityNote = BrowserNote)]
    public static Widget Shapes(StoryContext ctx) => Snapshot(ctx, Canvas2D(420, 220, draw: scene =>
    {
        scene.FillRect(Color2D.Rgba(248, 250, 252), 0, 0, 420, 220);
        scene.FillRoundedRect(Color2D.Rgba(59, 130, 246), 18, 18, 138, 82, 16);
        scene.FillCircle(Color2D.Rgba(245, 158, 11), 232, 62, 42);
        scene.StrokeRoundedRect(Color2D.Rgba(34, 197, 94), 4, 300, 20, 92, 80, 12);
        scene.BeginFill(Color2D.Rgba(239, 68, 68)).MoveTo(62, 128).LineTo(148, 204).LineTo(16, 204).Close().End();
        scene.StrokePolyline(Color2D.Rgba(71, 85, 105), 3,
            new Vector2(178, 202), new Vector2(224, 132), new Vector2(282, 190), new Vector2(398, 126));
    }));

    [Story("Examples/2D/VectorPaths", Width = 360, Height = 330, Order = 112, CapabilityNote = BrowserNote)]
    public static Widget VectorPaths(StoryContext ctx) => Snapshot(ctx, Canvas2D(280, 260, draw: scene =>
    {
        scene.FillRect(Color2D.Rgba(245, 245, 248), 0, 0, 280, 260);
        scene.BeginFill(Color2D.Rgba(45, 105, 220), FillRule.EvenOdd);
        AddCircle(scene, 180, 142, 72);
        AddCircle(scene, 180, 142, 34);
        scene.EndFill();
        scene.BeginStroke(Color2D.Rgba(239, 130, 35), 9).MoveTo(20, 226).QuadTo(76, 110, 136, 220).End();
        scene.BeginFill(Color2D.Rgba(40, 185, 105)).MoveTo(24, 32).LineTo(126, 32).LineTo(106, 104).LineTo(42, 88).Close().End();
    }));

    [Story("Examples/2D/CameraRig", Width = 540, Height = 360, Order = 118, CapabilityNote = BrowserNote)]
    public static Widget CameraTransform(StoryContext ctx) => Snapshot(ctx, VStack(6)[
        Muted("同じ world geometry を identity / translated / zoomed Camera2D で比較"),
        Frame(Canvas2D(460, 84, draw: scene => DrawCameraStage(scene, Camera2D.Pixels))),
        Frame(Canvas2D(460, 84, draw: scene => DrawCameraStage(scene, Camera2D.Create(1f, new Vector2(80, 10), 460, 84)))),
        Frame(Canvas2D(460, 84, draw: scene => DrawCameraStage(scene, Camera2D.Create(1.6f, new Vector2(160, 42), 460, 84))))
    ]);

    [Story("Examples/2D/Sprites", Width = 480, Height = 300, Order = 119, CapabilityNote = BrowserNote)]
    public static Widget Sprites(StoryContext ctx) => Snapshot(ctx, VStack(6)[
        Muted("手続き atlas の 4 frame と、sub-rect を拡大した描画結果"),
        Frame(Canvas2D(400, 210, draw: scene =>
        {
            scene.FillRect(Color2D.Rgba(26, 31, 42), 0, 0, 400, 210);
            uint[] colors = [Color2D.Rgba(60, 130, 240), Color2D.Rgba(230, 80, 100), Color2D.Rgba(40, 200, 120), Color2D.Rgba(235, 200, 50)];
            for (int i = 0; i < 4; i++)
            {
                float x = 18 + i * 54;
                scene.FillRect(colors[i], x, 20, 42, 42);
                scene.StrokeRoundedRect(Color2D.Rgba(245, 245, 245), 2, x, 20, 42, 42, 2);
                scene.FillRect(Color2D.Rgba(245, 245, 245), x + (i % 2 == 0 ? 5 : 27), 25 + (i < 2 ? 0 : 22), 10, 10);
            }
            scene.FillRoundedRect(colors[2], 258, 82, 112, 112, 8);
            scene.StrokeRoundedRect(Color2D.Rgba(245, 245, 245), 4, 258, 82, 112, 112, 8);
            scene.FillRect(Color2D.Rgba(245, 245, 245), 272, 144, 28, 28);
        }))
    ]);

    [Story("Examples/2D/Rasterizer/InputPathsLive", Width = 520, Height = 300, Order = 200, CapabilityNote = BrowserNote)]
    public static Widget InputPaths(StoryContext ctx) => Diagnostic(ctx, "open stroke / closed fill", scene =>
    {
        scene.BeginStroke(Color2D.Rgba(245, 158, 11), 7).MoveTo(26, 168).LineTo(96, 52).LineTo(168, 168).End();
        scene.BeginFill(Color2D.Rgba(59, 130, 246), FillRule.EvenOdd).MoveTo(224, 174).LineTo(286, 48).LineTo(352, 174).Close().End();
        for (int i = 0; i < 3; i++) scene.FillCircle(Color2D.Rgba(248, 250, 252), 224 + i * 64, i == 1 ? 48 : 174, 5);
    });

    [Story("Examples/2D/Rasterizer/EncodedSceneLive", Width = 520, Height = 300, Order = 201, CapabilityNote = BrowserNote)]
    public static Widget EncodedScene(StoryContext ctx) => Diagnostic(ctx, "shape range → segment range", scene =>
    {
        uint[] colors = [Color2D.Rgba(59, 130, 246), Color2D.Rgba(34, 197, 94), Color2D.Rgba(239, 68, 68)];
        for (int i = 0; i < 3; i++)
        {
            scene.FillRoundedRect(colors[i], 24, 34 + i * 55, 108, 36, 7);
            for (int j = 0; j <= i + 1; j++) scene.FillRect(colors[i], 188 + j * 34, 40 + i * 55, 24, 24);
            scene.StrokeLine(Color2D.Rgba(148, 163, 184), 2, 136, 52 + i * 55, 184, 52 + i * 55);
        }
    });

    [Story("Examples/2D/Rasterizer/BoundsLive", Width = 520, Height = 300, Order = 202, CapabilityNote = BrowserNote)]
    public static Widget Bounds(StoryContext ctx) => Diagnostic(ctx, "geometry と screen-space bounds", scene =>
    {
        scene.BeginFill(Color2D.Rgba(59, 130, 246)).MoveTo(82, 38).CubicTo(210, 12, 116, 190, 312, 146).LineTo(248, 190).Close().End();
        scene.StrokeRoundedRect(Color2D.Rgba(239, 68, 68), 3, 79, 30, 238, 164, 2);
    });

    [Story("Examples/2D/Rasterizer/TileBinsLive", Width = 520, Height = 300, Order = 203, CapabilityNote = BrowserNote)]
    public static Widget TileBins(StoryContext ctx) => Diagnostic(ctx, "16×16 tile と primitive membership", scene =>
    {
        scene.FillCircle(Color2D.Rgba(59, 130, 246, 180), 180, 108, 72);
        scene.FillRoundedRect(Color2D.Rgba(245, 158, 11, 190), 222, 62, 124, 104, 18);
        for (int x = 16; x < 400; x += 32) scene.StrokeLine(Color2D.Rgba(148, 163, 184, 130), 1, x, 20, x, 204);
        for (int y = 20; y < 220; y += 32) scene.StrokeLine(Color2D.Rgba(148, 163, 184, 130), 1, 16, y, 400, y);
    });

    [Story("Examples/2D/Rasterizer/CoverageLive", Width = 520, Height = 300, Order = 204, CapabilityNote = BrowserNote)]
    public static Widget Coverage(StoryContext ctx) => Diagnostic(ctx, "EvenOdd coverage と 4×4 sample", scene =>
    {
        scene.BeginFill(Color2D.Rgba(59, 130, 246), FillRule.EvenOdd); AddCircle(scene, 132, 112, 78); AddCircle(scene, 132, 112, 38); scene.EndFill();
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            scene.FillCircle(Color2D.Rgba(245, 158, 11), 272 + x * 28, 70 + y * 28, 4);
    });

    [Story("Examples/2D/Rasterizer/StrokeLive", Width = 520, Height = 300, Order = 205, CapabilityNote = BrowserNote)]
    public static Widget Stroke(StoryContext ctx) => Diagnostic(ctx, "stroke width と open/closed contour", scene =>
    {
        scene.BeginStroke(Color2D.Rgba(34, 197, 94), 12).MoveTo(28, 58).LineTo(156, 176).LineTo(264, 50).End();
        scene.BeginStroke(Color2D.Rgba(239, 68, 68), 5).MoveTo(290, 54).LineTo(382, 54).LineTo(382, 176).LineTo(290, 176).Close().End();
    });

    [Story("Examples/2D/Rasterizer/CompositeLive", Width = 520, Height = 300, Order = 206, CapabilityNote = BrowserNote)]
    public static Widget Composite(StoryContext ctx) => Diagnostic(ctx, "painter order と source-over", scene =>
    {
        scene.FillCircle(Color2D.Rgba(239, 68, 68, 190), 156, 110, 76);
        scene.FillCircle(Color2D.Rgba(59, 130, 246, 190), 232, 110, 76);
        scene.FillRoundedRect(Color2D.Rgba(34, 197, 94, 180), 204, 72, 140, 94, 20);
    });

    [Story("Examples/2D/Rasterizer/DispatchLive", Width = 520, Height = 300, Order = 207, CapabilityNote = BrowserNote)]
    public static Widget Dispatch(StoryContext ctx) => Diagnostic(ctx, "bounds → bins → fine → composite", scene =>
    {
        uint[] colors = [Color2D.Rgba(59, 130, 246), Color2D.Rgba(245, 158, 11), Color2D.Rgba(34, 197, 94), Color2D.Rgba(168, 85, 247)];
        for (int i = 0; i < 4; i++)
        {
            float x = 18 + i * 96;
            scene.FillRoundedRect(colors[i], x, 78, 72, 60, 10);
            if (i < 3) scene.BeginFill(Color2D.Rgba(148, 163, 184)).MoveTo(x + 76, 100).LineTo(x + 90, 108).LineTo(x + 76, 116).Close().End();
        }
    });

    [Story("Examples/2D/Rasterizer/RetainedUpdatesLive", Width = 520, Height = 330, Order = 208, CapabilityNote = BrowserNote)]
    public static Widget RetainedUpdates(StoryContext ctx) => Snapshot(ctx, VStack(6)[
        Muted("transform/style の部分更新と geometry full rebuild の差"),
        Frame(Canvas2D(420, 205, draw: scene =>
        {
            scene.FillRoundedRect(Color2D.Rgba(59, 130, 246), 22, 28, 112, 58, 12);
            scene.FillRoundedRect(Color2D.Rgba(34, 197, 94), 154, 28, 112, 58, 12);
            scene.FillRoundedRect(Color2D.Rgba(239, 68, 68), 286, 28, 112, 58, 12);
            scene.StrokeLine(Color2D.Rgba(59, 130, 246), 5, 78, 100, 78, 176);
            scene.StrokeLine(Color2D.Rgba(34, 197, 94), 5, 210, 100, 210, 176);
            scene.StrokeLine(Color2D.Rgba(239, 68, 68), 5, 342, 100, 342, 176);
            for (int i = 0; i < 3; i++) for (int j = 0; j <= i; j++)
                scene.FillCircle(i == 2 ? Color2D.Rgba(239, 68, 68) : i == 1 ? Color2D.Rgba(34, 197, 94) : Color2D.Rgba(59, 130, 246),
                    56 + i * 132 + j * 22, 188, 6);
        }))
    ]);

    private static Widget Diagnostic(StoryContext ctx, string caption, Action<Scene2D> draw)
        => Snapshot(ctx, VStack(6)[Muted(caption), Frame(Canvas2D(420, 220, draw: scene =>
        {
            scene.FillRect(Color2D.Rgba(15, 23, 42), 0, 0, 420, 220);
            draw(scene);
        }))]);

    private static Widget Snapshot(StoryContext ctx, Widget widget) => ctx.Snap(Frame(widget));

    private static void AddCircle(Scene2D scene, float cx, float cy, float radius, int segments = 48)
    {
        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * MathF.Tau;
            float x = cx + MathF.Cos(angle) * radius;
            float y = cy + MathF.Sin(angle) * radius;
            if (i == 0) scene.MoveTo(x, y); else scene.LineTo(x, y);
        }
        scene.Close();
    }

    private static void DrawCameraStage(Scene2D scene, Camera2D camera)
    {
        scene.FillRect(Color2D.Rgba(20, 26, 38), 0, 0, 460, 84);
        Vector2 Transform(float x, float y) => new(camera.A * x + camera.C * y + camera.E, camera.B * x + camera.D * y + camera.F);
        for (float x = -120; x < 620; x += 40)
        {
            Vector2 a = Transform(x, -80), b = Transform(x, 164);
            scene.StrokeLine(Color2D.Rgba(70, 82, 105), 1, a.X, a.Y, b.X, b.Y);
        }
        Vector2 point = Transform(230, 42);
        scene.FillRoundedRect(Color2D.Rgba(245, 180, 55), point.X - 10, point.Y - 10, 20, 20, 4);
    }
}
