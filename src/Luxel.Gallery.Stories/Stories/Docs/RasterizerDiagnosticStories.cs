using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>Production ABI/source-backed visual maps for the 2D compute rasterizer stages.</summary>
public static class RasterizerDiagnosticStories
{
    private static Widget Diagnostic(StoryContext ctx, string title, string stage, string body, string source)
        => DocNew(ctx, $"""
        # {title}

        > **Diagnostic map:** {stage}. The constants shown here are current implementation values, not public API.

        {body}

        ## Production source

        {SampleSource(source)}

        → [Rasterizer Internals course](story:Learn/Grapics/RasterizerInternals/Overview)
        """, toc: true);

    [Story("Examples/2D/Rasterizer/InputPaths", Order = 0)]
    public static Widget InputPaths(StoryContext ctx) => Diagnostic(ctx, "Input paths", "Scene2D → flattened segments",
        "The live path example below exercises line, quadratic, cubic, open, and closed contours. `FlattenTolerance` controls the CPU-side de Casteljau subdivision before upload.\n\n"
        + StoryRef(ctx, "Examples/2D/VectorPaths"), "src/Luxel.Graphics.TwoD/PathEncoder.cs");

    [Story("Examples/2D/Rasterizer/EncodedScene", Order = 1)]
    public static Widget EncodedScene(StoryContext ctx) => Diagnostic(ctx, "Encoded scene", "Segment / Path / Transform / Style / Clip / Order SoA",
        "```mermaid\nflowchart LR\nScene2D --> PathEncoder\nPathEncoder --> Segments\nPathEncoder --> Paths\nPathEncoder --> Styles\nPathEncoder --> Transforms\nPathEncoder --> Clips\nPathEncoder --> Order\n```\n\nEach path indexes the other arrays; the order buffer preserves painter order independently from stable retained slots.",
        "src/Luxel.Graphics.TwoD/Primitives.cs");

    [Story("Examples/2D/Rasterizer/Bounds", Order = 2)]
    public static Widget Bounds(StoryContext ctx) => Diagnostic(ctx, "Bounds pass", "path AABB + stroke margin + clip",
        "```mermaid\nflowchart LR\nPath --> Transform --> ScreenAABB\nStyle --> StrokeMargin --> ScreenAABB\nClip --> Intersection --> ScreenAABB\nScreenAABB --> BoundsBuffer\n```\n\nThe bounds buffer is consumed by both bin and fine passes for early rejection.",
        "shaders/raster2d_bounds.slang");

    [Story("Examples/2D/Rasterizer/TileBins", Order = 3)]
    public static Widget TileBins(StoryContext ctx) => Diagnostic(ctx, "Tile bins", "16×16 pixel tiles",
        "| Tile state | Meaning |\n|---|---|\n| empty | no path bounds intersect the tile |\n| populated | painter-order indices fit in the tile list |\n| overflow | fine pass scans the complete order buffer for correctness |\n\nThe current tile capacity is an implementation tuning value and may change.",
        "shaders/raster2d_bin.slang");

    [Story("Examples/2D/Rasterizer/Coverage", Order = 4)]
    public static Widget Coverage(StoryContext ctx) => Diagnostic(ctx, "Coverage and fill rules", "4×4 samples per pixel",
        "| Rule | Covered when |\n|---|---|\n| NonZero | winding number is not zero |\n| EvenOdd | crossing parity is odd |\n\nFine raster evaluates sample points, then converts the number of covered samples into pixel coverage.",
        "shaders/raster2d_fine.slang");

    [Story("Examples/2D/Rasterizer/Stroke", Order = 5)]
    public static Widget Stroke(StoryContext ctx) => Diagnostic(ctx, "Stroke distance", "screen-space segment distance",
        "Stroke coverage compares each sample's distance to the flattened segment against half the configured screen-space width. Closed contours add the closing segment; open strokes do not.",
        "shaders/raster2d_fine.slang");

    [Story("Examples/2D/Rasterizer/Composite", Order = 6)]
    public static Widget Composite(StoryContext ctx) => Diagnostic(ctx, "Painter-order composite", "premultiplied source-over",
        "```text\nout.rgb = src.rgb + dst.rgb * (1 - src.a)\nout.a   = src.a   + dst.a   * (1 - src.a)\n```\n\nVector coverage and sampled images enter the same ordered premultiplied-alpha composite.",
        "shaders/raster2d_fine.slang");

    [Story("Examples/2D/Rasterizer/Dispatch", Order = 7)]
    public static Widget Dispatch(StoryContext ctx) => Diagnostic(ctx, "Dispatch chain", "bounds → barrier → bin → barrier → fine",
        "```mermaid\nflowchart LR\nBounds --> Barrier1[compute barrier]\nBarrier1 --> Bin\nBin --> Barrier2[compute barrier]\nBarrier2 --> Fine\nFine --> Barrier3[all-stage barrier]\n```\n\nScratch buffers grow to the required capacity and are reused by the serial rasterizer session.",
        "src/Luxel.Graphics.TwoD/GpuDeviceRasterizer2D.cs");

    [Story("Examples/2D/Rasterizer/RetainedUpdates", Order = 8)]
    public static Widget RetainedUpdates(StoryContext ctx) => Diagnostic(ctx, "Retained updates", "dirty ranges and stable slots",
        "Transform-only and style-only edits update their corresponding SoA ranges without regenerating segment bytes. The retained canvas exposes last-write counters so tests can distinguish incremental upload from a full rebuild.",
        "src/Luxel.Graphics.TwoD/Retained/RetainedCanvas.cs");
}
