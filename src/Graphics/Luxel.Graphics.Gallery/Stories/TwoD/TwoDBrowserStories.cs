using System.Numerics;
using Luxel.Controls;
using Luxel.Graphics;
using Luxel.Graphics.TwoD;
using Luxel.Mathematics;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>Learn/Graphics/2D から埋め込む browser-WASM 対応の決定的な 2D Widget Story。</summary>
[StoryMeta("Examples/2D")]
public static class TwoDBrowserStories
{
    private const string BrowserNote = "Runs through the Gallery browser-WASM WebGPU runtime.";

    public static StoryResult SceneRender(StoryContext ctx)
    {
        var scene = new Scene2D();
        scene.FillRect(Color2D.Rgba(18, 24, 38), 0, 0, 420, 220);
        scene.FillRoundedRect(Color2D.Rgba(47, 111, 237), 28, 30, 150, 86, 18);
        scene.FillCircle(Color2D.Rgba(245, 180, 55), 276, 76, 46);
        scene.BeginFill(Color2D.Rgba(236, 72, 100)).MoveTo(92, 142).LineTo(172, 204).LineTo(26, 204).Close().End();
        scene.StrokeLine(Color2D.Rgba(90, 210, 150), 6, 216, 190, 382, 138);
        return Snapshot(ctx, RasterView(ctx, 420, 220, scene, Camera2D.Pixels));
    }

    public static StoryResult Shapes(StoryContext ctx) => Snapshot(ctx, Canvas2D(420, 220, draw: scene =>
    {
        scene.FillRect(Color2D.Rgba(248, 250, 252), 0, 0, 420, 220);
        scene.FillRoundedRect(Color2D.Rgba(59, 130, 246), 18, 18, 138, 82, 16);
        scene.FillCircle(Color2D.Rgba(245, 158, 11), 232, 62, 42);
        scene.StrokeRoundedRect(Color2D.Rgba(34, 197, 94), 4, 300, 20, 92, 80, 12);
        scene.BeginFill(Color2D.Rgba(239, 68, 68)).MoveTo(62, 128).LineTo(148, 204).LineTo(16, 204).Close().End();
        scene.StrokePolyline(Color2D.Rgba(71, 85, 105), 3,
            new Vector2(178, 202), new Vector2(224, 132), new Vector2(282, 190), new Vector2(398, 126));
    }));

    public static StoryResult VectorPaths(StoryContext ctx) => Snapshot(ctx, Canvas2D(280, 260, draw: scene =>
    {
        scene.FillRect(Color2D.Rgba(245, 245, 248), 0, 0, 280, 260);
        scene.BeginFill(Color2D.Rgba(45, 105, 220), FillRule.EvenOdd);
        AddCircle(scene, 180, 142, 72);
        AddCircle(scene, 180, 142, 34);
        scene.EndFill();
        scene.BeginStroke(Color2D.Rgba(239, 130, 35), 9).MoveTo(20, 226).QuadTo(76, 110, 136, 220).End();
        scene.BeginFill(Color2D.Rgba(40, 185, 105)).MoveTo(24, 32).LineTo(126, 32).LineTo(106, 104).LineTo(42, 88).Close().End();
    }));

    public static StoryResult CameraTransform(StoryContext ctx)
    {
        Scene2D WorldScene()
        {
            var scene = new Scene2D();
            for (float x = -120; x < 620; x += 40)
                scene.StrokeLine(Color2D.Rgba(70, 82, 105), 1, x, -80, x, 164);
            scene.FillRoundedRect(Color2D.Rgba(245, 180, 55), 220, 32, 20, 20, 4);
            return scene;
        }

        // 恒等変換: world座標をそのままscreen pixelとして描く。
        Camera2D identity = Camera2D.Pixels;
        // panのみ: worldの(80, 10)を460×84 viewportの中央(230, 42)へ移す。
        Camera2D panned = Camera2D.Create(1f, new Vector2(80, 10), 460, 84);
        // zoom + pan: worldの(160, 42)をviewport中央へ移し、その点を中心に1.6倍する。
        Camera2D zoomed = Camera2D.Create(1.6f, new Vector2(160, 42), 460, 84);

        return Snapshot(ctx, VStack(6)[
            Muted("identity — world座標 = screen pixel"),
            RasterView(ctx, 460, 84, WorldScene(), identity),
            Muted("pan — world (80, 10) → screen center (230, 42)"),
            RasterView(ctx, 460, 84, WorldScene(), panned),
            Muted("zoom + pan — world (160, 42)を中心に1.6倍"),
            RasterView(ctx, 460, 84, WorldScene(), zoomed)
        ]);
    }

    public static StoryResult Sprites(StoryContext ctx)
    {
        if (ctx.DeviceOrNull is not { } device) return Snapshot(ctx, Muted("GPU runtime required"));
        const int atlasW = 64, atlasH = 64, cell = 32;
        GpuBuffer atlasBuffer = device.Malloc(atlasW * atlasH * 4, GpuMemoryKind.HostMapped);
        FillAtlas(atlasBuffer.Span<byte>(atlasW * atlasH * 4), atlasW, cell);
        var atlas = new SpriteAtlas("proc://2d-course", [
            new("f_0", new SpriteRect(0, 0, cell, cell)),
            new("f_1", new SpriteRect(cell, 0, cell, cell)),
            new("f_2", new SpriteRect(0, cell, cell, cell)),
            new("f_3", new SpriteRect(cell, cell, cell, cell)),
        ]);
        atlas.Bind(atlasBuffer.BindlessIndex, atlasW, atlasH);

        const int viewWidth = 400, viewHeight = 210;
        var scene = new Scene2D();
        scene.FillRect(Color2D.Rgba(26, 31, 42), 0, 0, viewWidth, viewHeight);
        // 左上: 64×64 atlas全体。
        scene.ImageRect(atlasBuffer.BindlessIndex, atlasW, atlasW, atlasH, 16, 18, 64, 64);
        // 中央上: atlas右上の32×32 sub-rectだけを2倍表示。
        scene.ImageSubRect(atlasBuffer.BindlessIndex, atlasW, 32, 0, 32, 32, 112, 18, 64, 64);
        // 下段: 名前付きSpriteRectをatlasから選択。
        for (int frame = 0; frame < 4; frame++)
            scene.DrawSprite(atlas, $"f_{frame}", 16 + frame * 52, 112, scale: 1.5f);
        // 右側: SpriteAnimationが0.30秒時点で選ぶframeを拡大表示。
        var animation = new SpriteAnimation("f_", frameCount: 4, fps: 8f);
        animation.Update(0.30f);
        scene.DrawSprite(atlas, animation, 286, 94, scale: 3f);

        return Snapshot(ctx, VStack(4)[
            Muted("上: atlas全体 / sub-rect　下: 名前付きsprite　右: animation frame"),
            RasterView(ctx, viewWidth, viewHeight, scene, Camera2D.Pixels, atlasBuffer)
        ]);
    }

    [Story(CapabilityNote = BrowserNote)]
    public static StoryResult AlphaMasks(StoryContext ctx)
    {
        if (ctx.DeviceOrNull is not { } device) return Snapshot(ctx, Muted("GPU runtime required"));

        const int atlasWidth = 40, atlasHeight = 24;
        int stride = AlphaMaskAtlas.RequiredRowStride(atlasWidth);
        GpuBuffer maskBuffer = device.Malloc((ulong)(stride * atlasHeight), GpuMemoryKind.HostMapped);
        FillMaskAtlas(maskBuffer.Span<byte>(stride * atlasHeight), stride);

        var atlas = new AlphaMaskAtlas();
        atlas.Bind(maskBuffer.BindlessIndex, atlasWidth, atlasHeight, stride);

        var scene = new Scene2D();
        scene.FillRect(Color2D.Rgba(245, 247, 250), 0, 0, 420, 220);
        scene.DrawMask(atlas, new MaskRect(2, 2, 16, 20), new RectF(24, 28, 16, 20),
            Color2D.Rgba(28, 40, 64), MaskSampling.Nearest);
        scene.DrawMask(atlas, new MaskRect(2, 2, 16, 20), new RectF(76, 26, 80, 100),
            Color2D.Rgba(47, 111, 237), MaskSampling.Nearest);
        scene.DrawMask(atlas, new MaskRect(2, 2, 16, 20), new RectF(190, 26, 80, 100),
            Color2D.Rgba(236, 72, 100), MaskSampling.Linear);
        scene.DrawMask(atlas, new MaskRect(20, 2, 18, 18), new RectF(310, 38, 72, 72),
            Color2D.Rgba(34, 197, 94), MaskSampling.Linear);

        return Snapshot(ctx, VStack(4)[
            Muted("R8 mask — 1:1 / nearest / linear / soft coverage"),
            RasterView(ctx, 420, 220, scene, Camera2D.Pixels, maskBuffer)
        ]);
    }

    public static StoryResult InputPaths(StoryContext ctx) => Diagnostic(ctx, "open stroke / closed fill", scene =>
    {
        scene.BeginStroke(Color2D.Rgba(245, 158, 11), 7).MoveTo(26, 168).LineTo(96, 52).LineTo(168, 168).End();
        scene.BeginFill(Color2D.Rgba(59, 130, 246), FillRule.EvenOdd).MoveTo(224, 174).LineTo(286, 48).LineTo(352, 174).Close().End();
        for (int i = 0; i < 3; i++) scene.FillCircle(Color2D.Rgba(248, 250, 252), 224 + i * 64, i == 1 ? 48 : 174, 5);
    });

    [Story(CapabilityNote = BrowserNote)]
    public static StoryResult EncodedScene(StoryContext ctx) => Diagnostic(ctx, "shape range → segment range", scene =>
    {
        uint[] colors = [Color2D.Rgba(59, 130, 246), Color2D.Rgba(34, 197, 94), Color2D.Rgba(239, 68, 68)];
        for (int i = 0; i < 3; i++)
        {
            scene.FillRoundedRect(colors[i], 24, 34 + i * 55, 108, 36, 7);
            for (int j = 0; j <= i + 1; j++) scene.FillRect(colors[i], 188 + j * 34, 40 + i * 55, 24, 24);
            scene.StrokeLine(Color2D.Rgba(148, 163, 184), 2, 136, 52 + i * 55, 184, 52 + i * 55);
        }
    });

    [Story(CapabilityNote = BrowserNote)]
    public static StoryResult Bounds(StoryContext ctx) => Diagnostic(ctx, "geometry と screen-space bounds", scene =>
    {
        scene.BeginFill(Color2D.Rgba(59, 130, 246)).MoveTo(82, 38).CubicTo(210, 12, 116, 190, 312, 146).LineTo(248, 190).Close().End();
        scene.StrokeRoundedRect(Color2D.Rgba(239, 68, 68), 3, 79, 30, 238, 164, 2);
    });

    [Story(CapabilityNote = BrowserNote)]
    public static StoryResult TileBins(StoryContext ctx) => Diagnostic(ctx, "16×16 tile と primitive membership", scene =>
    {
        scene.FillCircle(Color2D.Rgba(59, 130, 246, 180), 180, 108, 72);
        scene.FillRoundedRect(Color2D.Rgba(245, 158, 11, 190), 222, 62, 124, 104, 18);
        for (int x = 16; x < 400; x += 32) scene.StrokeLine(Color2D.Rgba(148, 163, 184, 130), 1, x, 20, x, 204);
        for (int y = 20; y < 220; y += 32) scene.StrokeLine(Color2D.Rgba(148, 163, 184, 130), 1, 16, y, 400, y);
    });

    [Story(CapabilityNote = BrowserNote)]
    public static StoryResult Coverage(StoryContext ctx) => Diagnostic(ctx, "EvenOdd coverage と 4×4 sample", scene =>
    {
        scene.BeginFill(Color2D.Rgba(59, 130, 246), FillRule.EvenOdd); AddCircle(scene, 132, 112, 78); AddCircle(scene, 132, 112, 38); scene.EndFill();
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            scene.FillCircle(Color2D.Rgba(245, 158, 11), 272 + x * 28, 70 + y * 28, 4);
    });

    [Story(CapabilityNote = BrowserNote)]
    public static StoryResult Stroke(StoryContext ctx) => Diagnostic(ctx, "stroke width と open/closed contour", scene =>
    {
        scene.BeginStroke(Color2D.Rgba(34, 197, 94), 12).MoveTo(28, 58).LineTo(156, 176).LineTo(264, 50).End();
        scene.BeginStroke(Color2D.Rgba(239, 68, 68), 5).MoveTo(290, 54).LineTo(382, 54).LineTo(382, 176).LineTo(290, 176).Close().End();
    });

    public static StoryResult Composite(StoryContext ctx) => Diagnostic(ctx, "EvenOdd fill と painter-order source-over", scene =>
    {
        scene.BeginFill(Color2D.Rgba(59, 130, 246, 210), FillRule.EvenOdd);
        AddCircle(scene, 112, 110, 76);
        AddCircle(scene, 112, 110, 34);
        scene.EndFill();
        scene.FillCircle(Color2D.Rgba(239, 68, 68, 190), 226, 110, 76);
        scene.FillRoundedRect(Color2D.Rgba(34, 197, 94, 180), 248, 72, 140, 94, 20);
    });

    [Story(CapabilityNote = BrowserNote)]
    public static StoryResult Dispatch(StoryContext ctx) => Diagnostic(ctx, "bounds → bins → fine → composite", scene =>
    {
        uint[] colors = [Color2D.Rgba(59, 130, 246), Color2D.Rgba(245, 158, 11), Color2D.Rgba(34, 197, 94), Color2D.Rgba(168, 85, 247)];
        for (int i = 0; i < 4; i++)
        {
            float x = 18 + i * 96;
            scene.FillRoundedRect(colors[i], x, 78, 72, 60, 10);
            if (i < 3) scene.BeginFill(Color2D.Rgba(148, 163, 184)).MoveTo(x + 76, 100).LineTo(x + 90, 108).LineTo(x + 76, 116).Close().End();
        }
    });

    public static StoryResult RetainedUpdates(StoryContext ctx)
    {
        var canvas = new RetainedCanvas();
        UiNode card = canvas.AddChild(canvas.Root);
        card.Content = new Scene2D().FillRoundedRect(Color2D.White, 0, 0, 180, 90, 14);
        card.Transform = Affine2D.Translate(24, 24);
        card.Color = Color2D.Rgba(59, 130, 246);
        canvas.Flush(420, 220);
        card.Transform = Affine2D.Translate(190, 92);
        card.Color = Color2D.Rgba(34, 197, 94);
        canvas.Flush(420, 220);
        string counters = $"transform={canvas.LastTransformWrites}, style={canvas.LastStyleWrites}, segmentBytes={canvas.LastSegmentBytesWritten}, fullRebuild={canvas.LastWasFullRebuild}";
        return Snapshot(ctx, VStack(6)[
            Muted(counters),
            RasterView(ctx, 420, 220, canvas, Camera2D.Pixels)
        ]);
    }

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

    private static Widget RasterView(StoryContext ctx, uint width, uint height, Scene2D scene, Camera2D camera,
        params IDisposable[] resources)
        => RasterView(ctx, width, height, rasterizer => rasterizer.CreateScene(scene), camera, resources);

    private static Widget RasterView(StoryContext ctx, uint width, uint height, RetainedCanvas canvas, Camera2D camera,
        params IDisposable[] resources)
        => RasterView(ctx, width, height, rasterizer => rasterizer.CreateScene(canvas), camera, [canvas, .. resources]);

    private static Widget RasterView(StoryContext ctx, uint width, uint height,
        Func<IRasterizer2D, IRasterScene2D> createScene, Camera2D camera, params IDisposable[] resources)
    {
        if (ctx.DeviceOrNull is not { } device) return Muted("GPU runtime required");
        IRasterizer2D rasterizer = new GpuDeviceRasterizer2D(device, RasterShader);
        IRasterScene2D encoded = createScene(rasterizer);
        return GpuView(width, height, (_, surface, _) =>
        {
            using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
            // GpuViewのframebufferは64px境界のrow strideを持つ。rasterizerの出力幅もstrideへ合わせ、
            // GpuView側はsurface.Widthだけをcrop表示する。logical widthを渡すと各行の開始位置がずれて縞状になる。
            encoded.Render(camera,
                new GpuRasterTarget2D(command, surface.Framebuffer, surface.StridePixels, surface.Height));
            command.Finish();
            device.MainQueue.Submit(command);
            return GpuViewRenderResult.Ready;
        }, animated: false, dispose: () =>
        {
            encoded.Dispose();
            rasterizer.Dispose();
            foreach (IDisposable resource in resources) resource.Dispose();
        });
    }

    private static GpuShaderCode RasterShader(string name) => new()
    {
        SpirV = ShaderResource(name + ".spv"),
        Dxil = ShaderResource(name + ".dxil"),
        Wgsl = ShaderResource(name + ".wgsl"),
    };

    private static byte[] ShaderResource(string fileName)
    {
        System.Reflection.Assembly assembly = typeof(TwoDBrowserStories).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith("Shaders." + fileName, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded raster shader is missing: {fileName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static void FillAtlas(Span<byte> pixels, int atlasWidth, int cell)
    {
        (byte R, byte G, byte B)[] colors = [(60, 130, 240), (230, 80, 100), (40, 200, 120), (235, 200, 50)];
        for (int frame = 0; frame < 4; frame++)
        {
            int originX = frame % 2 * cell, originY = frame / 2 * cell;
            for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                int offset = ((originY + y) * atlasWidth + originX + x) * 4;
                bool border = x < 2 || y < 2 || x >= cell - 2 || y >= cell - 2;
                bool marker = x >= (frame % 2 == 0 ? 4 : 20) && x < (frame % 2 == 0 ? 12 : 28)
                    && y >= (frame < 2 ? 4 : 20) && y < (frame < 2 ? 12 : 28);
                pixels[offset] = border ? (byte)20 : marker ? (byte)245 : colors[frame].R;
                pixels[offset + 1] = border ? (byte)24 : marker ? (byte)245 : colors[frame].G;
                pixels[offset + 2] = border ? (byte)30 : marker ? (byte)245 : colors[frame].B;
                pixels[offset + 3] = 255;
            }
        }
    }

    private static void FillMaskAtlas(Span<byte> pixels, int stride)
    {
        pixels.Clear();
        // 16x20 の A。4x4 supersampling で coverage を作り、低水準 mask API の入力例にする。
        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 16; x++)
        {
            int covered = 0;
            for (int sy = 0; sy < 4; sy++)
            for (int sx = 0; sx < 4; sx++)
            {
                var p = new Vector2(x + (sx + 0.5f) / 4, y + (sy + 0.5f) / 4);
                float left = DistanceToSegment(p, new Vector2(1.5f, 19), new Vector2(7.5f, 1));
                float right = DistanceToSegment(p, new Vector2(7.5f, 1), new Vector2(14, 19));
                bool bar = p.Y >= 11 && p.Y <= 13 && p.X >= 4 && p.X <= 11.5f;
                if (left <= 1.25f || right <= 1.25f || bar) covered++;
            }
            pixels[(y + 2) * stride + x + 2] = (byte)((covered * 255 + 8) / 16);
        }

        // 18x18 の soft circle。外周2pxを coverage gradient にする。
        for (int y = 0; y < 18; y++)
        for (int x = 0; x < 18; x++)
        {
            float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(9, 9));
            float coverage = Math.Clamp((9f - distance) / 2f, 0, 1);
            pixels[(y + 2) * stride + x + 20] = (byte)(coverage * 255 + 0.5f);
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Math.Clamp(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab), 0, 1);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
