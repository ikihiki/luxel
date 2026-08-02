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

    [Story("Learn/Grapics/TwoD/Paths", Order = 1)]
    public static Widget Paths(StoryContext ctx) => Page(ctx, "Learn/Grapics/TwoD/Paths", "Path、fill、stroke", "TwoD Overview",
        "`Scene2D` は `MoveTo`、`LineTo`、`QuadTo`、`CubicTo`、`Close` を蓄積します。fill は open contour を閉じ、stroke は明示的に閉じた contour だけを閉じます。\n\n→ [動く VectorPaths](story:Examples/2D/VectorPaths)",
        "src/Luxel.Graphics.TwoD/Scene2D.cs", "Learn/Grapics/TwoD/Overview", "Learn/Grapics/TwoD/Compositing");

    [Story("Learn/Grapics/TwoD/Compositing", Order = 2)]
    public static Widget Compositing(StoryContext ctx) => Page(ctx, "Learn/Grapics/TwoD/Compositing", "Fill rule と合成", "Paths",
        "NonZero と EvenOdd は winding の解釈が異なります。色は premultiplied RGBA で保持し、Scene の order 順に source-over 合成します。",
        "src/Luxel.Graphics.TwoD/Primitives.cs", "Learn/Grapics/TwoD/Paths", "Learn/Grapics/TwoD/Images");

    [Story("Learn/Grapics/TwoD/Images", Order = 3)]
    public static Widget Images(StoryContext ctx) => Page(ctx, "Learn/Grapics/TwoD/Images", "Image、sprite、atlas", "Compositing",
        "`ImageRect`、`ImageSubRect`、`DrawSprite` は vector path と同じ painter's order に参加します。画像の寿命と atlas の UV を block の外へ隠さないでください。",
        "src/Luxel.Graphics.TwoD/Scene2D.cs", "Learn/Grapics/TwoD/Compositing", "Learn/Grapics/TwoD/Camera");

    [Story("Learn/Grapics/TwoD/Camera", Order = 4)]
    public static Widget Camera(StoryContext ctx) => Page(ctx, "Learn/Grapics/TwoD/Camera", "Camera2D と入力", "Images",
        "world 座標を camera で screen 座標へ写し、pointer は逆変換して hit test します。描画と入力で同じ camera を共有することが重要です。",
        "src/Luxel.Graphics.TwoD/Primitives.cs", "Learn/Grapics/TwoD/Images", "Learn/Grapics/TwoD/Backends");

    [Story("Learn/Grapics/TwoD/Backends", Order = 5)]
    public static Widget Backends(StoryContext ctx) => Page(ctx, "Learn/Grapics/TwoD/Backends", "GPU と Skia backend", "Camera2D",
        "アプリは `IRasterizer2D` に依存し、backend 固有機能は `Rasterizer2DCapabilities` で確認します。GPU は compute rasterizer、headless test は Skia CPU を選べます。",
        "src/Luxel.Graphics.TwoD/IRasterizer2D.cs", "Learn/Grapics/TwoD/Camera", "Learn/Grapics/TwoD/RetainedCanvas");

    [Story("Learn/Grapics/TwoD/RetainedCanvas", Order = 6)]
    public static Widget RetainedCanvasPage(StoryContext ctx) => Page(ctx, "Learn/Grapics/TwoD/RetainedCanvas", "RetainedCanvas", "Backends",
        "保持型 canvas は node tree と display data を分離します。transform、style、clip、order、geometry の変更を追跡し、変わった範囲だけを backend へ送ります。",
        "src/Luxel.Graphics.TwoD/Retained/RetainedCanvas.cs", "Learn/Grapics/TwoD/Backends", "Learn/Grapics/TwoD/IncrementalUpdates");

    [Story("Learn/Grapics/TwoD/IncrementalUpdates", Order = 7)]
    public static Widget IncrementalUpdates(StoryContext ctx) => Page(ctx, "Learn/Grapics/TwoD/IncrementalUpdates", "増分更新", "RetainedCanvas",
        "transform だけの移動では segment を再生成しません。`LastTransformWrites`、`LastStyleWrites`、`LastSegmentBytesWritten` で更新量を観察できます。",
        "src/Luxel.Graphics.TwoD/GpuDeviceRasterizer2D.cs", "Learn/Grapics/TwoD/RetainedCanvas", "Learn/Grapics/RasterizerInternals/Overview");
}
