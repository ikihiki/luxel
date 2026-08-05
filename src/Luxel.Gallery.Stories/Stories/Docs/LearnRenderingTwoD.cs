using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static class LearnRenderingTwoD
{
    private static Widget Page(StoryContext ctx, string path, string title, string summary, string body, string? source = null, string? previous = null, string? next = null)
        => DocNew(ctx, $"""
        # {title}

        {RenderingCourseCatalog.Meta(path, "Intermediate", "Gallery / Standalone / Headless", "Vulkan / DirectX 12 / Skia CPU", summary)}

        {body}

        {(source is null ? new DocMarkdown("") : SampleSource(source))}
        """, toc: true);

    [Story("Learn/Grapics/2D/Paths", Order = 11)]
    public static Widget Paths(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Paths", "Path、fill、stroke", "2D Overview",
        "`Scene2D` は `MoveTo`、`LineTo`、`QuadTo`、`CubicTo`、`Close` を蓄積します。fill は open contour を閉じ、stroke は明示的に閉じた contour だけを閉じます。\n\n→ [動く VectorPaths](story:Examples/2D/VectorPaths)",
        "src/Luxel.Graphics.TwoD/Scene2D.cs", "Learn/Grapics/2D/Overview", "Learn/Grapics/2D/Compositing");

    [Story("Learn/Grapics/2D/Compositing", Order = 12)]
    public static Widget Compositing(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Compositing", "Fill rule と合成", "Paths",
        "NonZero と EvenOdd は winding の解釈が異なります。色は premultiplied RGBA で保持し、Scene の order 順に source-over 合成します。",
        "src/Luxel.Graphics.TwoD/Primitives.cs", "Learn/Grapics/2D/Paths", "Learn/Grapics/2D/Images");

    [Story("Learn/Grapics/2D/Images", Order = 13)]
    public static Widget Images(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Images", "Image、sprite、atlas", "Compositing",
        "`ImageRect`、`ImageSubRect`、`DrawSprite` は vector path と同じ painter's order に参加します。画像の寿命と atlas の UV を block の外へ隠さないでください。",
        "src/Luxel.Graphics.TwoD/Scene2D.cs", "Learn/Grapics/2D/Compositing", "Learn/Grapics/2D/Camera");

    [Story("Learn/Grapics/2D/Camera", Order = 14)]
    public static Widget Camera(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Camera", "Camera2D と入力", "Images",
        "world 座標を camera で screen 座標へ写し、pointer は逆変換して hit test します。描画と入力で同じ camera を共有することが重要です。",
        "src/Luxel.Graphics.TwoD/Primitives.cs", "Learn/Grapics/2D/Images", "Learn/Grapics/2D/Backends");

    [Story("Learn/Grapics/2D/Backends", Order = 15)]
    public static Widget Backends(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Backends", "GPU と Skia backend", "Camera2D",
        "アプリは `IRasterizer2D` に依存し、backend 固有機能は `Rasterizer2DCapabilities` で確認します。GPU は compute rasterizer、headless test は Skia CPU を選べます。",
        "src/Luxel.Graphics.TwoD/IRasterizer2D.cs", "Learn/Grapics/2D/Camera", "Learn/Grapics/2D/RetainedCanvas");

    [Story("Learn/Grapics/2D/RetainedCanvas", Order = 16)]
    public static Widget RetainedCanvasPage(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/RetainedCanvas", "RetainedCanvas", "Backends",
        "保持型 canvas は node tree と display data を分離します。transform、style、clip、order、geometry の変更を追跡し、変わった範囲だけを backend へ送ります。",
        "src/Luxel.Graphics.TwoD/Retained/RetainedCanvas.cs", "Learn/Grapics/2D/Backends", "Learn/Grapics/2D/IncrementalUpdates");

    [Story("Learn/Grapics/2D/IncrementalUpdates", Order = 17)]
    public static Widget IncrementalUpdates(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/IncrementalUpdates", "増分更新", "RetainedCanvas",
        "transform だけの移動では segment を再生成しません。`LastTransformWrites`、`LastStyleWrites`、`LastSegmentBytesWritten` で更新量を観察できます。",
        "src/Luxel.Graphics.TwoD/GpuDeviceRasterizer2D.cs", "Learn/Grapics/2D/RetainedCanvas", "Learn/Grapics/2D/Internal/Overview");
}
