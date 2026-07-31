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

    [Story("Build/Blocks/Framework/FixedTimestep", Order = 2, SampleBundle = "framework.fixed-timestep")]
    public static Widget FrameworkTiming(StoryContext ctx) => DocNew(ctx, $"""
        # Block: Framework fixed timestep

        GPUやwindowを使わず、可変frame dtからbounded fixed updatesとinterpolation alphaを得るblockです。

        {SampleBundle("framework.fixed-timestep")}
        """, toc: true);

    [Story("Build/Blocks/UI/HeadlessTree", Order = 3, SampleBundle = "ui.headless-tree")]
    public static Widget UiHeadlessTree(StoryContext ctx) => DocNew(ctx, $"""
        # Block: Headless reactive UI tree

        `Signal`、`CompositeControl`、generated `Kit` factories、LayoutだけでBuild invalidationを検証するclean-consumer blockです。

        {SampleBundle("ui.headless-tree")}
        """, toc: true);

    [Story("Build/Blocks/Input/Actions", Order = 4, SampleBundle = "input.actions")]
    public static Widget InputActions(StoryContext ctx) => DocNew(ctx, $"""
        # Block: Input actions

        Windowから独立したaction/context/stackのblockです。

        {SampleBundle("input.actions")}
        """, toc: true);

    [Story("Build/Blocks/Audio/Tone", Order = 5, SampleBundle = "audio.tone")]
    public static Widget AudioTone(StoryContext ctx) => DocNew(ctx, $"""
        # Block: Audio tone

        Procedural PCMとheadless backendでvoice lifecycleを確認します。

        {SampleBundle("audio.tone")}
        """, toc: true);

    [Story("Build/Blocks/Resources/Pipeline", Order = 6, SampleBundle = "resources.pipeline")]
    public static Widget ResourcePipeline(StoryContext ctx) => DocNew(ctx, $"""
        # Block: Resource pipeline

        Memory VFSとtyped conversion stepによる最小resource DAGです。

        {SampleBundle("resources.pipeline")}
        """, toc: true);

    [Story("Build/Recipes/Cavern2D", Order = 7, SampleBundle = "game.cavern")]
    public static Widget Cavern2D(StoryContext ctx) => DocNew(ctx, $"""
        # Recipe: Cavern 2D game

        Framework、UI、2D、input、audio、resources、particles、settingsを統合したrepository capstoneです。

        {StoryRef(ctx, "Game/Cavern")}
        {SampleBundle("game.cavern")}
        """, toc: true);

    [Story("Build/Recipes/Range3D", Order = 8, SampleBundle = "game.range")]
    public static Widget Range3D(StoryContext ctx) => DocNew(ctx, $"""
        # Recipe: Range 3D game

        ECS、physics、glTF、GPU asset extraction、particlesを統合したrepository capstoneです。

        {StoryRef(ctx, "Apps/Game/Range")}
        {SampleBundle("game.range")}
        """, toc: true);

    [Story("Build/Blocks/Scripting/HotReload", Order = 9, SampleBundle = "scripting.gallery")]
    public static Widget ScriptHotReload(StoryContext ctx) => DocNew(ctx, $"""
        # Block: Script hot reload

        Galleryで検証される`ScriptHost`、diagnostics、successful-swap、cancellationの接続例です。

        {StoryRef(ctx, "Examples/Scripting/HotReload")}
        {SampleBundle("scripting.gallery")}
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

    [Story("Build/Recipes/HeadlessScene2D", Order = 12, SampleBundle = "rendering.2d")]
    public static Widget HeadlessScene2D(StoryContext ctx) => DocNew(ctx, $$"""
        # Recipe: Headless Scene2D Render

        静的な `Scene2D` を `Camera2D.Pixels` で一度だけSkia CPU rasterizerへ描画し、決定的なpixel hashを検証するstandalone recipeです。input、retained canvas、interactive cameraはこのbundleには含まれません。

        {{StoryRef(ctx, "Examples/2D/VectorPaths")}}

        {{SampleBundle("rendering.2d")}}
        """, toc: true);
}
