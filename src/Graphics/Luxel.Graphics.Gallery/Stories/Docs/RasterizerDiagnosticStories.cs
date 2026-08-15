using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Story;
using static Luxel.Gallery.DocKit.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>Production ABI/source-backed visual maps for the 2D compute rasterizer stages.</summary>
[StoryMeta("Examples/2D/Rasterizer")]
public static class RasterizerDiagnosticStories
{
    private static StoryResult Diagnostic(StoryContext ctx, string title, string stage, string body)
        => $"""
        # {title}

        > **Diagnostic map:** {stage}. The constants shown here are current implementation values, not public API.

        {body}

        → [Internal course](story:Learn/Graphics/2D/Internal/Overview)
        """;

    [Story]
    public static StoryResult InputPaths(StoryContext ctx) => Diagnostic(ctx, "Input paths", "Scene2D → flattened segments",
        "The live path example below exercises line, quadratic, cubic, open, and closed contours. `FlattenTolerance` controls the CPU-side de Casteljau subdivision before upload.\n\n"
        + StoryRef(ctx, "Examples/2D/VectorPaths"));

    [Story]
    public static StoryResult EncodedScene(StoryContext ctx) => Diagnostic(ctx, "Encoded scene", "Segment / Path / Transform / Style / Clip / Order SoA",
        "```mermaid\nflowchart LR\nScene2D --> PathEncoder\nPathEncoder --> Segments\nPathEncoder --> Paths\nPathEncoder --> Styles\nPathEncoder --> Transforms\nPathEncoder --> Clips\nPathEncoder --> Order\n```\n\nEach path indexes the other arrays; the order buffer preserves painter order independently from stable retained slots.");

    [Story]
    public static StoryResult Bounds(StoryContext ctx) => Diagnostic(ctx, "Bounds pass", "path AABB + stroke margin + clip",
        "```mermaid\nflowchart LR\nPath --> Transform --> ScreenAABB\nStyle --> StrokeMargin --> ScreenAABB\nClip --> Intersection --> ScreenAABB\nScreenAABB --> BoundsBuffer\n```\n\nThe bounds buffer is consumed by both bin and fine passes for early rejection.");

    [Story]
    public static StoryResult TileBins(StoryContext ctx) => Diagnostic(ctx, "Tile bins", "16×16 pixel tiles",
        "| Tile state | Meaning |\n|---|---|\n| empty | no path bounds intersect the tile |\n| populated | painter-order indices fit in the tile list |\n| overflow | fine pass scans the complete order buffer for correctness |\n\nThe current tile capacity is an implementation tuning value and may change.");

    [Story]
    public static StoryResult Coverage(StoryContext ctx) => Diagnostic(ctx, "Coverage and fill rules", "4×4 samples per pixel",
        "| Rule | Covered when |\n|---|---|\n| NonZero | winding number is not zero |\n| EvenOdd | crossing parity is odd |\n\nFine raster evaluates sample points, then converts the number of covered samples into pixel coverage.");

    [Story]
    public static StoryResult Stroke(StoryContext ctx) => Diagnostic(ctx, "Stroke distance", "screen-space segment distance",
        "Stroke coverage compares each sample's distance to the flattened segment against half the configured screen-space width. Closed contours add the closing segment; open strokes do not.");

    [Story]
    public static StoryResult Composite(StoryContext ctx) => Diagnostic(ctx, "Painter-order composite", "premultiplied source-over",
        "```text\nout.rgb = src.rgb + dst.rgb * (1 - src.a)\nout.a   = src.a   + dst.a   * (1 - src.a)\n```\n\nVector coverage and sampled images enter the same ordered premultiplied-alpha composite.");

    [Story]
    public static StoryResult Dispatch(StoryContext ctx) => Diagnostic(ctx, "Dispatch chain", "bounds → barrier → bin → barrier → fine",
        "```mermaid\nflowchart LR\nBounds --> Barrier1[compute barrier]\nBarrier1 --> Bin\nBin --> Barrier2[compute barrier]\nBarrier2 --> Fine\nFine --> Barrier3[all-stage barrier]\n```\n\nScratch buffers grow to the required capacity and are reused by the serial rasterizer session.");

    [Story]
    public static StoryResult RetainedUpdates(StoryContext ctx) => Diagnostic(ctx, "Retained updates", "dirty ranges and stable slots",
        "Transform-only and style-only edits update their corresponding SoA ranges without regenerating segment bytes. The retained canvas exposes last-write counters so tests can distinguish incremental upload from a full rebuild.");
}
