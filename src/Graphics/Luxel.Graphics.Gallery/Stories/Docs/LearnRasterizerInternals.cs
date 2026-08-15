using static Luxel.Gallery.Story;
namespace Luxel.Gallery.Stories;

[StoryMeta("Learn/Graphics/2D/Internal")]
public static class LearnRasterizerInternals
{
    private static StoryResult Page(string path, string title, string stage, string input, string output,
        string invariant, string correctness)
        => $$"""
        # {{title}}

        {{Toc()}}

        > [!WARNING]
        > ここで扱うlayout、tile size、shader stageは内部実装です。通常の2D描画コードは`IRasterizer2D`と`Scene2D`を使います。

        {{RenderingCourseCatalog.Meta(path, "Advanced", "Gallery / Standalone", "GPU compute", "2D描画")}}

        ## Pipeline上の位置

        `Scene2D / RetainedCanvas → flattened geometry → SoA encoding → bounds → tile bins → fine coverage + composite → RGBA target`

        **このstage:** {{stage}}

        ## 入力と出力

        - **入力:** {{input}}
        - **出力:** {{output}}

        ## Core invariant

        {{invariant}}

        ## Correctnessと失敗時の確認

        {{correctness}}
        """;

    [Story]
    public static StoryResult Overview() => Page("Learn/Graphics/2D/Internal/Overview", "2D rasterizer pipeline", "全stageの接続",
        "Scene2DまたはRetainedCanvas", "RGBA render target",
        "C#側のbuffer rangeとdispatch順序が、shader側の読み取りlayoutと一致する。",
        "描画が欠ける場合は入力path、encoded range、bounds、tile membership、coverage、compositeの順に切り分けます。");

    [Story]
    public static StoryResult Flattening() => Page("Learn/Graphics/2D/Internal/Flattening", "Curve flattening", "curve → line segment",
        "quadratic/cubic Bézierとworld-unit tolerance", "ordered line segment列",
        "各subdivisionは許容誤差以内で元curveを近似し、contour orderを保存する。",
        "toleranceが小さすぎるとsegment数が増え、大きすぎると輪郭が崩れます。open/closed contourと2点未満の破棄も確認します。");

    [Story]
    public static StoryResult SceneEncoding() => Page("Learn/Graphics/2D/Internal/SceneEncoding", "SoA scene encoding", "segments → SoA buffers",
        "flattened segments、shape/style/transform/clip/order", "GPU buffer rangeとroot arguments",
        "各shapeのrangeがsegment列を正確に指し、order bufferがpainter orderを保存する。",
        "rangeのoff-by-one、空shape、transform/style slotのずれをdiagnosticで確認します。");

    [Story]
    public static StoryResult Abi() => Page("Learn/Graphics/2D/Internal/Abi", "C# / Slang ABI", "host layout ↔ shader layout",
        "GpuSegment/GpuPath/GpuTransform/GpuStyle/GpuClip", "shaderから同じ意味で読めるbytes",
        "field order、size、alignment、bindless indexがC#とSlangで一致する。",
        "ABI test、shader cacheのinputs hash、root argument sizeを確認します。layout変更は片側だけで完結しません。");

    [Story]
    public static StoryResult Bounds() => Page("Learn/Graphics/2D/Internal/Bounds", "Bounds pass", "encoded paths → screen AABB",
        "path segment range、camera transform、stroke width、clip", "pathごとのscreen-space bounds",
        "boundsは全covered pixelを含み、stroke marginとclipを保守的に反映する。",
        "欠けはunder-estimate、過剰workはover-estimateを疑います。NaN、zero-size target、clip交差も確認します。");

    [Story]
    public static StoryResult TileBinning() => Page("Learn/Graphics/2D/Internal/TileBinning", "16×16 tile binning", "bounds → tile membership",
        "screen boundsとpainter order", "tileごとのordered path list",
        "tile内のpath orderを保存し、capacity overflowでも全order fallbackでcorrectnessを失わない。",
        "overflow count、tile offset/capacity、boundsとの交差を確認します。TileCapは公開契約ではありません。");

    [Story]
    public static StoryResult FineRaster() => Page("Learn/Graphics/2D/Internal/FineRaster", "Fine rasterとcoverage", "tile paths → sample coverage",
        "tile list、segments、fill rule、stroke width", "pixelごとのpremultiplied source color",
        "NonZero/EvenOddのwindingとstroke distanceが同じsample grid上で安定してcoverageを返す。",
        "現在の4×4 sample、境界pixel、open stroke、clip、fallback tileを確認します。");

    [Story]
    public static StoryResult ImagesAndComposite() => Page("Learn/Graphics/2D/Internal/ImagesAndComposite", "Image samplingとcomposite", "coverage/image → painter result",
        "vector coverage、image sample、order、destination color", "premultiplied RGBA pixel",
        "すべてのsourceをpremultiplied表現へ揃え、order順のsource-overを崩さない。",
        "transparent RGB、atlas座標/stride、bindless index、shape orderを確認します。");

    [Story]
    public static StoryResult Dispatch() => Page("Learn/Graphics/2D/Internal/Dispatch", "Dispatchとbarrier", "bounds → bin → fine",
        "scene buffers、scratch capacity、target", "完了したRGBA target",
        "各passのproducer writeがbarrier後のconsumer readから可視で、scratch rangeがdispatch量を満たす。",
        "bounds/bin/fineの順序、compute barrier、target transition、rasterizerの直列使用を確認します。browserでは同期waitを使いません。");

    [Story]
    public static StoryResult RetainedUploads() => Page("Learn/Graphics/2D/Internal/RetainedUploads", "Retained scene uploads", "dirty ranges → partial writes",
        "stable node slots、dirty transform/style/segment ranges", "in-place uploadまたはcompacted scene",
        "transform/style-only mutationはgeometry rangeを変えず、structural mutationだけが必要範囲を再構築する。",
        "write counters、fragmentation、capacity growth、full rebuild flagを確認します。");

    [Story]
    public static StoryResult Validation() => Page("Learn/Graphics/2D/Internal/Validation", "性能とcorrectnessの検証", "pipeline output → evidence",
        "unit tests、golden、backend parity、overflow fixture", "再現可能なpass/fail結果",
        "fill、clip、stroke、overflow fallback、ABI、backend差を独立したfixtureで固定する。",
        "shader変更時はSPIR-V/DXIL/WGSL artifactとinputs hashを更新し、unit test、golden、backend parityで検証します。");
}
