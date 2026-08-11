using System.Numerics;

namespace Luxel.Graphics.TwoD;

/// <summary>Scene2D を GPU 用の Segment[]/Path[] 配列にエンコードする。</summary>
internal static class PathEncoder
{
    public static (GpuSegment[] segments, GpuPath[] paths, GpuStyle[] styles) Encode(Scene2D scene)
    {
        var segments = new List<GpuSegment>();
        var paths = new GpuPath[scene.Shapes.Count];
        var styles = new GpuStyle[scene.Shapes.Count];

        for (int pi = 0; pi < scene.Shapes.Count; pi++)
        {
            Scene2D.Shape shape = scene.Shapes[pi];
            uint segStart = (uint)segments.Count;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

            // 塗り/画像は常に閉じる (巻き数のため)。ストロークは Closed の輪郭のみ閉じる。
            bool alwaysClose = shape.Kind != PaintKind.Stroke;
            foreach (Scene2D.Contour contour in shape.Contours)
            {
                List<Vector2> pts = contour.Points;
                int n = pts.Count;
                int edgeCount = (alwaysClose || contour.Closed) ? n : n - 1;
                for (int i = 0; i < edgeCount; i++)
                {
                    Vector2 a = pts[i];
                    Vector2 b = pts[(i + 1) % n];
                    if (a == b) continue;
                    segments.Add(new GpuSegment { X0 = a.X, Y0 = a.Y, X1 = b.X, Y1 = b.Y, PathId = (uint)pi });
                }
                foreach (Vector2 q in pts)
                {
                    minX = MathF.Min(minX, q.X); minY = MathF.Min(minY, q.Y);
                    maxX = MathF.Max(maxX, q.X); maxY = MathF.Max(maxY, q.Y);
                }
            }

            paths[pi] = new GpuPath
            {
                SegStart = segStart,
                SegCount = (uint)segments.Count - segStart,
                TransformSlot = 0,                 // 即時モードは恒等変換 (slot 0)
                StyleSlot = (uint)pi,
                ClipSlot = GpuPath.NoClip,
                FillRule = (uint)shape.Rule,
                BMinX = minX,
                BMinY = minY,
                BMaxX = maxX,
                BMaxY = maxY,
                Kind = shape.Kind switch { PaintKind.Stroke => 1u, PaintKind.Image => 2u, _ => 0u },
                StrokeHalfWidth = shape.StrokeWidth * 0.5f,
                SrcIndex = shape.SrcIndex,
                SrcStride = shape.SrcStride,
                SrcW = shape.SrcW,
                SrcH = shape.SrcH,
                SrcX = shape.SrcX,
                SrcY = shape.SrcY,
            };
            styles[pi] = new GpuStyle { ColorRgba = shape.Color, Opacity = 1f };
        }

        return (segments.ToArray(), paths, styles);
    }
}
