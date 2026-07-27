using Luxel.Controls;
using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

public static class BuildStories
{
    [Story("Build/Blocks/AppHost", Order = 0, SampleBundle = "rendering.app-host")]
    public static Widget AppHost(StoryContext ctx) => DocNew(ctx, $"""
        # Block: App Host

        window、device、surface、resize、frame loop、dispose を一度だけ用意する共通骨格です。他の rendering block はこの host の接続点へ追加します。

        {SampleBundle("rendering.app-host")}
        """, toc: true);

    [Story("Build/Blocks/ThreeD/Triangle", Order = 1, SampleBundle = "rendering.triangle")]
    public static Widget Triangle(StoryContext ctx) => DocNew(ctx, $"""
        # Block: Triangle

        C# ABI、vertex buffer、graphics pipeline、Slang shader を一つの検証済み bundle として扱います。

        {StoryRef(ctx, "Examples/3D/Triangle")}

        {SampleBundle("rendering.triangle")}
        """, toc: true);


    [Story("Build/Blocks/Input/Actions", Order = 2, SampleBundle = "input.actions")]
    public static Widget InputActions(StoryContext ctx) => DocNew(ctx, $"""
        # Block: Input actions

        Windowから独立したaction/context/stackのblockです。

        {SampleBundle("input.actions")}
        """, toc: true);

    [Story("Build/Blocks/Audio/Tone", Order = 3, SampleBundle = "audio.tone")]
    public static Widget AudioTone(StoryContext ctx) => DocNew(ctx, $"""
        # Block: Audio tone

        Procedural PCMとheadless backendでvoice lifecycleを確認します。

        {SampleBundle("audio.tone")}
        """, toc: true);

    [Story("Build/Blocks/Resources/Pipeline", Order = 4, SampleBundle = "resources.pipeline")]
    public static Widget ResourcePipeline(StoryContext ctx) => DocNew(ctx, $"""
        # Block: Resource pipeline

        Memory VFSとtyped conversion stepによる最小resource DAGです。

        {SampleBundle("resources.pipeline")}
        """, toc: true);

    [Story("Build/Recipes/TriangleApp", Order = 10, SampleBundle = "rendering.triangle")]
    public static Widget TriangleApp(StoryContext ctx) => DocNew(ctx, $"""
        # Recipe: Triangle App

        `rendering.app-host + rendering.triangle` の最小 recipe です。表示されるファイルは実際の `LuxelTriangle` project が build しているものです。

        {SampleBundle("rendering.triangle")}
        """, toc: true);

    [Story("Build/Recipes/TexturedScene", Order = 11, SampleBundle = "rendering.3d")]
    public static Widget TexturedScene(StoryContext ctx) => DocNew(ctx, $"""
        # Recipe: Textured 3D Scene

        texture、camera、depth、lighting、RenderGraph、compute post-process を stage 切替できる standalone project です。

        {SampleBundle("rendering.3d")}
        """, toc: true);

    [Story("Build/Recipes/TwoDCanvasApp", Order = 12, SampleBundle = "rendering.2d")]
    public static Widget TwoDCanvasApp(StoryContext ctx) => DocNew(ctx, $$"""
        # Recipe: 2D Canvas App

        [2D コース](story:Learn/Rendering/TwoD/Overview)で Scene2D、Camera2D、input、retained canvas を順に追加します。standalone bundle は App Host と同じ lifecycle 契約を使います。

        {{StoryRef(ctx, "Examples/2D/VectorPaths")}}

        {{SampleBundle("rendering.2d")}}
        """, toc: true);

    [Story("Build/Recipes/MiniGame2D", Order = 13)]
    public static Widget MiniGame2D(StoryContext ctx) => DocNew(ctx, $$"""
        # Recipe: Mini Game 2D

        2D canvas、input、retained UI、sprite/tilemap を組み合わせる統合例です。

        → [2D examples](story:Examples/2D/Tilemap)
        """, toc: true);

    [Story("Build/Recipes/Viewer3D", Order = 14, SampleBundle = "rendering.3d")]
    public static Widget Viewer3D(StoryContext ctx) => DocNew(ctx, $"""
        # Recipe: 3D Viewer

        App Host、camera、depth/lighting、RenderGraph、asset loader、UI overlay の接続順を学ぶための完成形です。

        {SampleBundle("rendering.3d")}
        """, toc: true);
}
