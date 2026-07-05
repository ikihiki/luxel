using System.Numerics;

namespace Luxel.TwoD;

/// <summary>図形の種別。</summary>
public enum PaintKind { Fill, Stroke, Image }

/// <summary>角丸矩形の丸める角の選択 (入力グループの接合面を直角にする等)。</summary>
[Flags]
public enum RectCorners
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomRight = 4,
    BottomLeft = 8,
    Left = TopLeft | BottomLeft,
    Right = TopRight | BottomRight,
    All = TopLeft | TopRight | BottomRight | BottomLeft,
}

/// <summary>
/// ベクターシーン。塗り (色 + 塗り規則) またはストローク (色 + 幅) ごとに 1 つ以上の輪郭を持つ。
/// 曲線は追加時に線分へ平坦化される。座標はワールド空間。
/// </summary>
public sealed class Scene2D
{
    internal sealed class Contour
    {
        public readonly List<Vector2> Points = new();
        public bool Closed;
    }

    internal sealed class Shape
    {
        public PaintKind Kind;
        public uint Color;
        public FillRule Rule;
        public float StrokeWidth;
        public uint SrcIndex, SrcStride, SrcW, SrcH;   // Image 用 (bindless バッファソース)
        /// <summary>true = 保持型キャンバスでノード色に畳まれず自分の色で描かれる (カラー絵文字のレイヤ等)。</summary>
        public bool AbsoluteColor;
        public readonly List<Contour> Contours = new();
    }

    internal readonly List<Shape> Shapes = new();

    private Shape? _shape;
    private Contour? _contour;
    private Vector2 _pen;

    /// <summary>曲線平坦化の許容誤差 (ワールド単位)。</summary>
    public float FlattenTolerance { get; set; } = 0.2f;

    /// <summary>エンコード後の (線分数, パス数) を数える — <see cref="UiNode.ReserveContent"/> の
    /// 見積もり用 (仮想化リスト等でワーストケースの Content を一度測る)。</summary>
    public (int Segments, int Paths) CountEncoded()
    {
        (GpuSegment[] segs, GpuPath[] paths, _) = PathEncoder.Encode(this);
        return (segs.Length, paths.Length);
    }

    /// <summary>塗り図形を開始する。<paramref name="absoluteColor"/> = true は保持型キャンバスで
    /// ノード色に畳まれず**この色のまま**描かれる (カラー絵文字のレイヤ等、テーマ recolor 対象外の色)。</summary>
    public Scene2D BeginFill(uint colorRgba, FillRule rule = FillRule.NonZero, bool absoluteColor = false)
    {
        FlushContour();
        _shape = new Shape { Kind = PaintKind.Fill, Color = colorRgba, Rule = rule, AbsoluteColor = absoluteColor };
        return this;
    }

    /// <summary>ストローク図形を開始する (幅はワールド単位ではなく画面ピクセル)。</summary>
    public Scene2D BeginStroke(uint colorRgba, float width)
    {
        FlushContour();
        _shape = new Shape { Kind = PaintKind.Stroke, Color = colorRgba, StrokeWidth = width };
        return this;
    }

    public Scene2D MoveTo(float x, float y)
    {
        FlushContour();
        _pen = new Vector2(x, y);
        _contour = new Contour();
        _contour.Points.Add(_pen);
        return this;
    }

    public Scene2D LineTo(float x, float y)
    {
        _pen = new Vector2(x, y);
        _contour!.Points.Add(_pen);
        return this;
    }

    public Scene2D QuadTo(float cx, float cy, float x, float y)
    {
        FlattenQuad(_pen, new Vector2(cx, cy), new Vector2(x, y), FlattenTolerance, _contour!.Points);
        _pen = new Vector2(x, y);
        return this;
    }

    public Scene2D CubicTo(float c0x, float c0y, float c1x, float c1y, float x, float y)
    {
        FlattenCubic(_pen, new Vector2(c0x, c0y), new Vector2(c1x, c1y), new Vector2(x, y),
            FlattenTolerance, _contour!.Points);
        _pen = new Vector2(x, y);
        return this;
    }

    /// <summary>現在の輪郭を閉じる。</summary>
    public Scene2D Close()
    {
        if (_contour != null) _contour.Closed = true;
        FlushContour();
        return this;
    }

    /// <summary>図形を確定する。</summary>
    public Scene2D End()
    {
        FlushContour();
        if (_shape != null && _shape.Contours.Count > 0) Shapes.Add(_shape);
        _shape = null;
        return this;
    }

    /// <summary>EndFill は End の別名 (互換)。</summary>
    public Scene2D EndFill() => End();

    // ---- 便利メソッド ----

    public Scene2D FillRect(uint color, float x, float y, float w, float h)
        => BeginFill(color).MoveTo(x, y).LineTo(x + w, y).LineTo(x + w, y + h).LineTo(x, y + h).Close().End();

    /// <summary>別バッファに描かれた RGBA8 画像を矩形にサンプリングして合成する (iframe 相当の埋め込み)。
    /// ソースは bindless バッファ (<see cref="GpuBuffer.BindlessIndex"/>) — framebuffer と同じ形式で、
    /// 透過合成するには premultiplied RGBA (RetainedCanvas.Render の transparent=true) で描いておく。
    /// <paramref name="srcStride"/> は行ピッチ (px, パディング込み)。サンプリングは nearest。</summary>
    public Scene2D ImageRect(uint srcIndex, uint srcStride, uint srcW, uint srcH,
                             float x, float y, float w, float h)
    {
        FlushContour();
        _shape = new Shape
        {
            Kind = PaintKind.Image,
            Color = Color2D.White,
            SrcIndex = srcIndex,
            SrcStride = srcStride,
            SrcW = srcW,
            SrcH = srcH,
        };
        MoveTo(x, y); LineTo(x + w, y); LineTo(x + w, y + h); LineTo(x, y + h); Close();
        return End();
    }

    public Scene2D FillRoundedRect(uint color, float x, float y, float w, float h, float r)
        => FillRoundedRect(color, x, y, w, h, r, RectCorners.All);

    /// <summary>角ごとに丸めを選べる角丸矩形 (入力グループ等、接合面だけ直角にする用)。
    /// <see cref="RectCorners.All"/> は従来の <c>FillRoundedRect</c> と同一ジオメトリ。</summary>
    public Scene2D FillRoundedRect(uint color, float x, float y, float w, float h, float r, RectCorners corners)
    {
        r = MathF.Min(r, MathF.Min(w, h) * 0.5f);
        BeginFill(color);
        if ((corners & RectCorners.TopLeft) != 0) MoveTo(x + r, y); else MoveTo(x, y);
        if ((corners & RectCorners.TopRight) != 0) LineTo(x + w - r, y).QuadTo(x + w, y, x + w, y + r); else LineTo(x + w, y);
        if ((corners & RectCorners.BottomRight) != 0) LineTo(x + w, y + h - r).QuadTo(x + w, y + h, x + w - r, y + h); else LineTo(x + w, y + h);
        if ((corners & RectCorners.BottomLeft) != 0) LineTo(x + r, y + h).QuadTo(x, y + h, x, y + h - r); else LineTo(x, y + h);
        if ((corners & RectCorners.TopLeft) != 0) LineTo(x, y + r).QuadTo(x, y, x + r, y); else LineTo(x, y);
        return Close().End();
    }

    /// <summary>角丸矩形のアウトライン (幅は画面ピクセル)。フォーカスリング等、面を覆わない輪郭用 —
    /// 塗りと違い描画順に関係なく下の内容が見える。</summary>
    public Scene2D StrokeRoundedRect(uint color, float width, float x, float y, float w, float h, float r)
    {
        r = MathF.Min(r, MathF.Min(w, h) * 0.5f);
        BeginStroke(color, width);
        MoveTo(x + r, y);
        LineTo(x + w - r, y).QuadTo(x + w, y, x + w, y + r);
        LineTo(x + w, y + h - r).QuadTo(x + w, y + h, x + w - r, y + h);
        LineTo(x + r, y + h).QuadTo(x, y + h, x, y + h - r);
        LineTo(x, y + r).QuadTo(x, y, x + r, y);
        return Close().End();
    }

    public Scene2D FillCircle(uint color, float cx, float cy, float radius, int segments = 64)
    {
        BeginFill(color);
        AddCircleContour(cx, cy, radius, segments);
        return End();
    }

    public Scene2D StrokeLine(uint color, float width, float x0, float y0, float x1, float y1)
        => BeginStroke(color, width).MoveTo(x0, y0).LineTo(x1, y1).End();

    public Scene2D StrokePolyline(uint color, float width, params Vector2[] points)
    {
        BeginStroke(color, width);
        for (int i = 0; i < points.Length; i++)
            if (i == 0) MoveTo(points[i].X, points[i].Y); else LineTo(points[i].X, points[i].Y);
        return End();
    }

    private void AddCircleContour(float cx, float cy, float r, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)segments * MathF.Tau;
            float x = cx + MathF.Cos(t) * r, y = cy + MathF.Sin(t) * r;
            if (i == 0) MoveTo(x, y); else LineTo(x, y);
        }
        Close();
    }

    private void FlushContour()
    {
        if (_contour != null && _contour.Points.Count >= 2 && _shape != null)
            _shape.Contours.Add(_contour);
        _contour = null;
    }

    // ---- 平坦化済み輪郭の取り出し / 追加 (グリフ線分キャッシュ用 — AP-M4) ----

    /// <summary>直前に <see cref="End"/> した図形の輪郭点列を取り出す (グリフキャッシュの構築用)。</summary>
    public Vector2[][] ExportContours()
    {
        if (Shapes.Count == 0) return [];
        Shape s = Shapes[^1];
        var res = new Vector2[s.Contours.Count][];
        for (int i = 0; i < res.Length; i++) res[i] = s.Contours[i].Points.ToArray();
        return res;
    }

    /// <summary>平坦化済みの閉輪郭列を現在の図形へ (ox, oy) 平行移動でコピーする —
    /// ベジェ平坦化をスキップする高速パス (キャッシュ済みグリフの描画用)。</summary>
    public Scene2D AppendClosedContours(Vector2[][] contours, float ox, float oy)
    {
        FlushContour();
        foreach (Vector2[] pts in contours)
        {
            var c = new Contour { Closed = true };
            c.Points.Capacity = pts.Length;
            for (int i = 0; i < pts.Length; i++) c.Points.Add(new Vector2(pts[i].X + ox, pts[i].Y + oy));
            _shape!.Contours.Add(c);
        }
        return this;
    }

    // ---- ベジェ平坦化 (再帰的 de Casteljau) ----

    private static void FlattenQuad(Vector2 p0, Vector2 c, Vector2 p1, float tol, List<Vector2> outPts, int depth = 0)
    {
        Vector2 d = p1 - p0;
        float area = MathF.Abs((c.X - p1.X) * d.Y - (c.Y - p1.Y) * d.X);
        if (depth >= 16 || area * area <= tol * tol * (d.X * d.X + d.Y * d.Y))
        {
            outPts.Add(p1);
            return;
        }
        Vector2 p01 = (p0 + c) * 0.5f, p12 = (c + p1) * 0.5f, mid = (p01 + p12) * 0.5f;
        FlattenQuad(p0, p01, mid, tol, outPts, depth + 1);
        FlattenQuad(mid, p12, p1, tol, outPts, depth + 1);
    }

    private static void FlattenCubic(Vector2 p0, Vector2 c0, Vector2 c1, Vector2 p1, float tol, List<Vector2> outPts, int depth = 0)
    {
        Vector2 d = p1 - p0;
        float d0 = MathF.Abs((c0.X - p1.X) * d.Y - (c0.Y - p1.Y) * d.X);
        float d1 = MathF.Abs((c1.X - p1.X) * d.Y - (c1.Y - p1.Y) * d.X);
        if (depth >= 16 || (d0 + d1) * (d0 + d1) <= tol * tol * (d.X * d.X + d.Y * d.Y))
        {
            outPts.Add(p1);
            return;
        }
        Vector2 p01 = (p0 + c0) * 0.5f, p12 = (c0 + c1) * 0.5f, p23 = (c1 + p1) * 0.5f;
        Vector2 p012 = (p01 + p12) * 0.5f, p123 = (p12 + p23) * 0.5f, mid = (p012 + p123) * 0.5f;
        FlattenCubic(p0, p01, p012, mid, tol, outPts, depth + 1);
        FlattenCubic(mid, p123, p23, p1, tol, outPts, depth + 1);
    }
}
