using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Luxel.TwoD.Skia;

/// <summary>
/// SkiaSharp による 2D システムの CPU バックエンド。GPU コンピュートラスタライザ
/// (<see cref="Rasterizer2D"/>) と同じシーン表現 — 保持ツリー (<see cref="RetainedCanvas"/>) と
/// 即時シーン (<see cref="Scene2D"/>) — を**実デバイスなし**でラスタライズする。テスト/CI 用。
/// ヘッドレスの <c>new RetainedCanvas()</c> と組み合わせると GpuDevice が一切要らない。
///
/// 意味論は GPU 側に合わせる: 描画順 = pre-order + Z 昇順、ノード色 (パスは白描き + Color が実色、
/// AbsoluteColor/ContentColors シェイプは自色)、実効 opacity = 親 × 自分、クリップ = 祖先 AABB 交差、
/// ストローク幅 = 画面ピクセル (カメラで太らない)。
/// 出力は GPU と同じ RGBA8 (R が下位バイト)。**AA の実装が違う** (GPU = 4x4 SS / Skia = 解析的) ため
/// エッジ画素は一致しない — 検証は形状内部の色・構造・レイアウトで行うこと。
/// 制約: Image シェイプ (bindless GPU バッファ参照) は描画されない。
/// </summary>
public static class SkiaRenderer
{
    /// <summary>保持ツリーを CPU でラスタライズして RGBA8 を返す (行ピッチ = width×4)。
    /// GPU の <c>RetainedCanvas.Render</c> + framebuffer 読み戻しに相当。
    /// transparent=true は premultiplied RGBA (GPU の transparent モードと同じ)。</summary>
    public static byte[] RenderRgba(RetainedCanvas canvas, Camera2D camera, int width, int height,
        bool transparent = false)
        => Draw(width, height, transparent, c => DrawNode(c, canvas.Root, camera,
            Affine2D.Identity, 1f, null));

    /// <summary>即時シーンを CPU でラスタライズして RGBA8 を返す。GPU の即時モード
    /// (<c>Rasterizer2D.Encode</c> + <c>Render</c>) に相当 — シェイプは自色で描く。</summary>
    public static byte[] RenderRgba(Scene2D scene, Camera2D camera, int width, int height,
        bool transparent = false)
        => Draw(width, height, transparent, c =>
        {
            foreach (Scene2D.Shape shape in scene.Shapes)
                DrawShape(c, shape, camera, Affine2D.Identity, shape.Color, 1f);
        });

    /// <summary>ピクセル読み取りヘルパ (テスト用): RGBA バッファの (x, y) を 0xAABBGGRR uint で返す
    /// (<see cref="Color2D.Rgba"/> と同じ並び — R が下位)。</summary>
    public static uint PixelAt(byte[] rgba, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return (uint)rgba[i] | ((uint)rgba[i + 1] << 8) | ((uint)rgba[i + 2] << 16) | ((uint)rgba[i + 3] << 24);
    }

    private static byte[] Draw(int width, int height, bool transparent, Action<SKCanvas> body)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bmp = new SKBitmap(info);
        using (var surface = SKSurface.Create(info, bmp.GetPixels(), info.RowBytes))
        {
            SKCanvas c = surface.Canvas;
            c.Clear(transparent ? SKColors.Transparent : SKColors.White);
            body(c);
            c.Flush();
        }
        var bytes = new byte[width * height * 4];
        Marshal.Copy(bmp.GetPixels(), bytes, 0, bytes.Length);
        if (!transparent)
            for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;   // 白背景モードは GPU 同様 alpha=255
        return bytes;
    }

    private static void DrawNode(SKCanvas c, UiNode n, in Camera2D cam,
        Affine2D parentWorld, float parentOpacity, SKRect? parentClip)
    {
        if (!n.Visible) return;   // サブツリーごと除外 (BuildOrder と同じ)
        Affine2D world = Affine2D.Mul(parentWorld, n.Transform);
        float opacity = parentOpacity * n.Opacity;

        // クリップ: 祖先 AABB の交差 (RetainedCanvas.ResolveClip と同じ軸並行前提)
        SKRect? clip = parentClip;
        if (n.Clip is RectClip rc)
        {
            SKRect r = ScreenAabb(rc, world, cam);
            clip = clip is SKRect pc ? SKRect.Intersect(pc, r) : r;
        }

        if (n.Content is Scene2D scene && scene.Shapes.Count > 0)
        {
            int save = c.Save();
            if (clip is SKRect cr) c.ClipRect(cr, SKClipOperation.Intersect, antialias: false);
            foreach (Scene2D.Shape shape in scene.Shapes)
            {
                // 1 ノード 1 色: パスは白描き + ノード Color が実色。AbsoluteColor/ContentColors は自色
                bool abs = n.ContentColors || shape.AbsoluteColor;
                DrawShape(c, shape, cam, world, abs ? shape.Color : n.Color, opacity);
            }
            c.RestoreToCount(save);
        }

        // 描画順 = pre-order + Z 昇順 (RetainedCanvas.SortedChildren と同じ)
        IEnumerable<UiNode> children = n.Children.Count <= 1 ? n.Children : n.Children.OrderBy(x => x.Z);
        foreach (UiNode child in children)
            DrawNode(c, child, cam, world, opacity, clip);
    }

    private static void DrawShape(SKCanvas c, Scene2D.Shape shape, in Camera2D cam, in Affine2D world,
        uint colorRgba, float opacity)
    {
        if (shape.Kind == PaintKind.Image) return;   // bindless GPU バッファ参照 — CPU では非対応

        using var path = new SKPath();
        path.FillType = shape.Rule == FillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding;
        bool fill = shape.Kind == PaintKind.Fill;
        foreach (Scene2D.Contour contour in shape.Contours)
        {
            List<Vector2> pts = contour.Points;
            if (pts.Count < 2) continue;
            SKPoint p0 = ToScreen(pts[0], world, cam);
            path.MoveTo(p0);
            for (int i = 1; i < pts.Count; i++) path.LineTo(ToScreen(pts[i], world, cam));
            if (fill || contour.Closed) path.Close();   // 塗りは常に閉じる (PathEncoder と同じ)
        }

        byte a = (byte)Math.Clamp((colorRgba >> 24) * opacity, 0, 255);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor((byte)colorRgba, (byte)(colorRgba >> 8), (byte)(colorRgba >> 16), a),
        };
        if (fill)
        {
            paint.Style = SKPaintStyle.Fill;
        }
        else
        {
            // ストローク幅は画面ピクセル (点列を先に変換しているので device 幅そのまま)。
            // GPU は距離ベース → 端/角は丸い
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = shape.StrokeWidth;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.StrokeJoin = SKStrokeJoin.Round;
        }
        c.DrawPath(path, paint);
    }

    private static SKPoint ToScreen(Vector2 p, in Affine2D world, in Camera2D cam)
    {
        Vector2 w = world.Apply(p);
        return new SKPoint(cam.A * w.X + cam.C * w.Y + cam.E, cam.B * w.X + cam.D * w.Y + cam.F);
    }

    private static SKRect ScreenAabb(RectClip rc, in Affine2D world, in Camera2D cam)
    {
        Span<SKPoint> pts =
        [
            ToScreen(new Vector2(rc.X, rc.Y), world, cam),
            ToScreen(new Vector2(rc.X + rc.W, rc.Y), world, cam),
            ToScreen(new Vector2(rc.X, rc.Y + rc.H), world, cam),
            ToScreen(new Vector2(rc.X + rc.W, rc.Y + rc.H), world, cam),
        ];
        float minx = pts[0].X, miny = pts[0].Y, maxx = pts[0].X, maxy = pts[0].Y;
        for (int i = 1; i < 4; i++)
        {
            minx = MathF.Min(minx, pts[i].X); miny = MathF.Min(miny, pts[i].Y);
            maxx = MathF.Max(maxx, pts[i].X); maxy = MathF.Max(maxy, pts[i].Y);
        }
        return new SKRect(minx, miny, maxx, maxy);
    }
}
