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

        {RenderingCourseCatalog.Meta(path, "Advanced", "Implementation reading + Gallery", "GPU compute", "Learn/Rendering/TwoD")}

        {body}

        ## 実装の正

        {SampleSource(source)}
        """, toc: true);

    [Story("Learn/Rendering/RasterizerInternals/Overview", Order = 0)]
    public static Widget Overview(StoryContext ctx) => Page(ctx, "Learn/Rendering/RasterizerInternals/Overview", "2D rasterizer pipeline",
        "1 frame は `Scene2D → PathEncoder → bounds → tile bin → fine raster → target` と流れます。C# は buffer と dispatch を組み、Slang compute shader が coverage と合成を計算します。",
        "src/Luxel.Graphics.TwoD/GpuDeviceRasterizer2D.cs", "Learn/Rendering/TwoD/IncrementalUpdates", "Learn/Rendering/RasterizerInternals/Flattening");

    [Story("Learn/Rendering/RasterizerInternals/Flattening", Order = 1)]
    public static Widget Flattening(StoryContext ctx) => Page(ctx, "Learn/Rendering/RasterizerInternals/Flattening", "Curve flattening",
        "quadratic/cubic Bézier を tolerance まで de Casteljau 分割し、GPU が扱う line segment 列へ変換します。tolerance は品質と segment 数の交換条件です。",
        "src/Luxel.Graphics.TwoD/PathEncoder.cs", "Learn/Rendering/RasterizerInternals/Overview", "Learn/Rendering/RasterizerInternals/SceneEncoding");

    [Story("Learn/Rendering/RasterizerInternals/SceneEncoding", Order = 2)]
    public static Widget SceneEncoding(StoryContext ctx) => Page(ctx, "Learn/Rendering/RasterizerInternals/SceneEncoding", "SoA scene encoding",
        "Segment、Path、Transform、Style、Clip、Order を別 buffer に encode します。order buffer が painter's order を保ち、同じ geometry を transform/style 更新から分離します。",
        "src/Luxel.Graphics.TwoD/PathEncoder.cs", "Learn/Rendering/RasterizerInternals/Flattening", "Learn/Rendering/RasterizerInternals/Abi");

    [Story("Learn/Rendering/RasterizerInternals/Abi", Order = 3)]
    public static Widget Abi(StoryContext ctx) => Page(ctx, "Learn/Rendering/RasterizerInternals/Abi", "C# / Slang ABI",
        "`GpuSegment`、`GpuPath`、`GpuTransform`、`GpuStyle`、`GpuClip` と root arguments の byte layout を shader 側と一致させます。bindless index が各 SoA buffer を結びます。",
        "src/Luxel.Graphics.TwoD/Primitives.cs", "Learn/Rendering/RasterizerInternals/SceneEncoding", "Learn/Rendering/RasterizerInternals/Bounds");

    [Story("Learn/Rendering/RasterizerInternals/Bounds", Order = 4)]
    public static Widget Bounds(StoryContext ctx) => Page(ctx, "Learn/Rendering/RasterizerInternals/Bounds", "Bounds pass",
        "path の screen-space AABB を計算し、stroke margin と clip を反映します。この結果が bin/fine pass の早期除外を可能にします。",
        "shaders/raster2d_bounds.slang", "Learn/Rendering/RasterizerInternals/Abi", "Learn/Rendering/RasterizerInternals/TileBinning");

    [Story("Learn/Rendering/RasterizerInternals/TileBinning", Order = 5)]
    public static Widget TileBinning(StoryContext ctx) => Page(ctx, "Learn/Rendering/RasterizerInternals/TileBinning", "16×16 tile binning",
        "各 tile に交差する path order を集めます。現在の `TileCap` を超えた tile は correctness を保つため全 order 走査へ fallback します。値は公開契約ではありません。",
        "shaders/raster2d_bin.slang", "Learn/Rendering/RasterizerInternals/Bounds", "Learn/Rendering/RasterizerInternals/FineRaster");

    [Story("Learn/Rendering/RasterizerInternals/FineRaster", Order = 6)]
    public static Widget FineRaster(StoryContext ctx) => Page(ctx, "Learn/Rendering/RasterizerInternals/FineRaster", "Fine raster と coverage",
        "pixel 内の sample 点で winding を計算し、NonZero/EvenOdd fill と segment distance による stroke coverage を求めます。現在は 4×4 supersampling です。",
        "shaders/raster2d_fine.slang", "Learn/Rendering/RasterizerInternals/TileBinning", "Learn/Rendering/RasterizerInternals/ImagesAndComposite");

    [Story("Learn/Rendering/RasterizerInternals/ImagesAndComposite", Order = 7)]
    public static Widget ImagesAndComposite(StoryContext ctx) => Page(ctx, "Learn/Rendering/RasterizerInternals/ImagesAndComposite", "Image sampling と composite",
        "vector coverage と image sample を premultiplied alpha に揃え、order 順に source-over 合成します。透明色でも RGB が alpha 済みであることが重要です。",
        "shaders/raster2d_fine.slang", "Learn/Rendering/RasterizerInternals/FineRaster", "Learn/Rendering/RasterizerInternals/Dispatch");

    [Story("Learn/Rendering/RasterizerInternals/Dispatch", Order = 8)]
    public static Widget Dispatch(StoryContext ctx) => Page(ctx, "Learn/Rendering/RasterizerInternals/Dispatch", "Dispatch と barrier",
        "bounds → compute barrier → bin → compute barrier → fine → all barrier の順で dispatch します。scratch buffer は必要量まで成長し、rasterizer は直列使用します。",
        "src/Luxel.Graphics.TwoD/GpuDeviceRasterizer2D.cs", "Learn/Rendering/RasterizerInternals/ImagesAndComposite", "Learn/Rendering/RasterizerInternals/RetainedUploads");

    [Story("Learn/Rendering/RasterizerInternals/RetainedUploads", Order = 9)]
    public static Widget RetainedUploads(StoryContext ctx) => Page(ctx, "Learn/Rendering/RasterizerInternals/RetainedUploads", "Retained scene uploads",
        "stable slot と capacity 付き range により、transform/style の小さな変更を in-place upload できます。geometry の断片化が進んだ場合だけ compaction します。",
        "src/Luxel.Graphics.TwoD/GpuDeviceRasterizer2D.cs", "Learn/Rendering/RasterizerInternals/Dispatch", "Learn/Rendering/RasterizerInternals/Validation");

    [Story("Learn/Rendering/RasterizerInternals/Validation", Order = 10)]
    public static Widget Validation(StoryContext ctx) => Page(ctx, "Learn/Rendering/RasterizerInternals/Validation", "性能と correctness の検証",
        "fill rule、clip、stroke、overflow fallback、backend pixel parity を golden と unit test で確認します。shader を変えたら SPIR-V/DXIL cache と `inputs.sha256` も更新します。",
        "tests/Luxel.Tests/TwoDTests.cs", "Learn/Rendering/RasterizerInternals/RetainedUploads", null);
}
