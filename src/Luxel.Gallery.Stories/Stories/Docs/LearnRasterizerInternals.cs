using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static class LearnRasterizerInternals
{
    private static Widget Page(StoryContext ctx, string path, string title, string body, string source, string? previous, string? next)
        => DocNew(ctx, $"""
        # {title}

        > [!WARNING]
        > ここで扱う型、buffer layout、tile size は現在の内部実装です。通常のアプリは `IRasterizer2D` と `Scene2D` を使ってください。

        {RenderingCourseCatalog.Meta(path, "Advanced", "Implementation reading + Gallery", "GPU compute", "2D")}

        {body}

        ## 実装の正

        {SampleSource(source)}
        """, toc: true);

    [Story("Learn/Grapics/2D/Internal/Overview", Order = 17)]
    public static Widget Overview(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Internal/Overview", "2D rasterizer pipeline",
        "1 frame は `Scene2D → PathEncoder → bounds → tile bin → fine raster → target` と流れます。C# は buffer と dispatch を組み、Slang compute shader が coverage と合成を計算します。",
        "src/Luxel.Graphics.TwoD/GpuDeviceRasterizer2D.cs", "Learn/Grapics/2D/IncrementalUpdates", "Learn/Grapics/2D/Internal/Flattening");

    [Story("Learn/Grapics/2D/Internal/Flattening", Order = 18)]
    public static Widget Flattening(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Internal/Flattening", "Curve flattening",
        "quadratic/cubic Bézier を tolerance まで de Casteljau 分割し、GPU が扱う line segment 列へ変換します。tolerance は品質と segment 数の交換条件です。",
        "src/Luxel.Graphics.TwoD/PathEncoder.cs", "Learn/Grapics/2D/Internal/Overview", "Learn/Grapics/2D/Internal/SceneEncoding");

    [Story("Learn/Grapics/2D/Internal/SceneEncoding", Order = 19)]
    public static Widget SceneEncoding(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Internal/SceneEncoding", "SoA scene encoding",
        "Segment、Path、Transform、Style、Clip、Order を別 buffer に encode します。order buffer が painter's order を保ち、同じ geometry を transform/style 更新から分離します。",
        "src/Luxel.Graphics.TwoD/PathEncoder.cs", "Learn/Grapics/2D/Internal/Flattening", "Learn/Grapics/2D/Internal/Abi");

    [Story("Learn/Grapics/2D/Internal/Abi", Order = 20)]
    public static Widget Abi(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Internal/Abi", "C# / Slang ABI",
        "`GpuSegment`、`GpuPath`、`GpuTransform`、`GpuStyle`、`GpuClip` と root arguments の byte layout を shader 側と一致させます。bindless index が各 SoA buffer を結びます。",
        "src/Luxel.Graphics.TwoD/Primitives.cs", "Learn/Grapics/2D/Internal/SceneEncoding", "Learn/Grapics/2D/Internal/Bounds");

    [Story("Learn/Grapics/2D/Internal/Bounds", Order = 21)]
    public static Widget Bounds(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Internal/Bounds", "Bounds pass",
        "path の screen-space AABB を計算し、stroke margin と clip を反映します。この結果が bin/fine pass の早期除外を可能にします。",
        "shaders/raster2d_bounds.slang", "Learn/Grapics/2D/Internal/Abi", "Learn/Grapics/2D/Internal/TileBinning");

    [Story("Learn/Grapics/2D/Internal/TileBinning", Order = 22)]
    public static Widget TileBinning(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Internal/TileBinning", "16×16 tile binning",
        "各 tile に交差する path order を集めます。現在の `TileCap` を超えた tile は correctness を保つため全 order 走査へ fallback します。値は公開契約ではありません。",
        "shaders/raster2d_bin.slang", "Learn/Grapics/2D/Internal/Bounds", "Learn/Grapics/2D/Internal/FineRaster");

    [Story("Learn/Grapics/2D/Internal/FineRaster", Order = 23)]
    public static Widget FineRaster(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Internal/FineRaster", "Fine raster と coverage",
        "pixel 内の sample 点で winding を計算し、NonZero/EvenOdd fill と segment distance による stroke coverage を求めます。現在は 4×4 supersampling です。",
        "shaders/raster2d_fine.slang", "Learn/Grapics/2D/Internal/TileBinning", "Learn/Grapics/2D/Internal/ImagesAndComposite");

    [Story("Learn/Grapics/2D/Internal/ImagesAndComposite", Order = 24)]
    public static Widget ImagesAndComposite(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Internal/ImagesAndComposite", "Image sampling と composite",
        "vector coverage と image sample を premultiplied alpha に揃え、order 順に source-over 合成します。透明色でも RGB が alpha 済みであることが重要です。",
        "shaders/raster2d_fine.slang", "Learn/Grapics/2D/Internal/FineRaster", "Learn/Grapics/2D/Internal/Dispatch");

    [Story("Learn/Grapics/2D/Internal/Dispatch", Order = 25)]
    public static Widget Dispatch(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Internal/Dispatch", "Dispatch と barrier",
        "bounds → compute barrier → bin → compute barrier → fine → all barrier の順で dispatch します。scratch buffer は必要量まで成長し、rasterizer は直列使用します。",
        "src/Luxel.Graphics.TwoD/GpuDeviceRasterizer2D.cs", "Learn/Grapics/2D/Internal/ImagesAndComposite", "Learn/Grapics/2D/Internal/RetainedUploads");

    [Story("Learn/Grapics/2D/Internal/RetainedUploads", Order = 26)]
    public static Widget RetainedUploads(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Internal/RetainedUploads", "Retained scene uploads",
        "stable slot と capacity 付き range により、transform/style の小さな変更を in-place upload できます。geometry の断片化が進んだ場合だけ compaction します。",
        "src/Luxel.Graphics.TwoD/GpuDeviceRasterizer2D.cs", "Learn/Grapics/2D/Internal/Dispatch", "Learn/Grapics/2D/Internal/Validation");

    [Story("Learn/Grapics/2D/Internal/Validation", Order = 27)]
    public static Widget Validation(StoryContext ctx) => Page(ctx, "Learn/Grapics/2D/Internal/Validation", "性能と correctness の検証",
        "fill rule、clip、stroke、overflow fallback、backend pixel parity を golden と unit test で確認します。shader を変えたら SPIR-V/DXIL cache と `inputs.sha256` も更新します。",
        "tests/Luxel.Tests/TwoDTests.cs", "Learn/Grapics/2D/Internal/RetainedUploads", null);
}
