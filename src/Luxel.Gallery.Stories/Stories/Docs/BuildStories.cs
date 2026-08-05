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

        Windowから独立したaction/context/stackのcopyable blockです。実windowとの接続はhostが`WindowInputSource`を生成し、同じ`InputBus`へpollします。

        {StoryRef(ctx, "Examples/Input/WindowActions")}

        {SampleBundle("input.actions")}

        Platformごとのbackendと制約は [Platform input and deterministic tests](story:Learn/Input/PlatformsAndTesting) を参照してください。
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

    [Story("Build/Recipes/IndexedCube", Order = 11, SampleBundle = "rendering.3d")]
    public static Widget IndexedCube(StoryContext ctx) => DocNew(ctx, $"""
        # Recipe: Index bufferでcubeを描く

        texture付きquadを24頂点・36 indexのcubeへ拡張するrecipeです。実装の正は`samples/LuxelTriangle/TriangleRenderer.cs`、共有ABIは`samples/LuxelTriangle/TutorialAbi.cs`、vertex pullingは`shaders/tutorial_3d.slang`です。

        ## 実行する

        ```powershell
        dotnet build samples/LuxelTriangle/LuxelTriangle.csproj
        dotnet test tests/Luxel.Tests/Luxel.Tests.csproj --filter TutorialAbiTests
        dotnet run --project samples/LuxelTriangle -- vk --stage transform --frames 3
        # Windowsのみ
        dotnet run --project samples/LuxelTriangle -- dx --stage transform --frames 3
        ```

        ## Indexed vertex pulling

        Luxelのcore graphics APIには固定functionの`DrawIndexed`はありません。`Draw(36)`が生成する`SV_VertexID`をindex-stream番号として使い、raw index bufferからvertex indexを読み、そのindexでvertex bufferをpullします。

        ```slang
        uint vertexIndex = g_buffers[indexBufferIndex].Load(vertexId * 4);
        Vertex vertex = g_buffers[vertexBufferIndex]
            .Load<Vertex>(vertexIndex * vertexStride);
        ```

        tutorialは32-bit indexを使います。cubeは8 positionだけを共有せず、面ごとのnormalとUV seamを表すため24頂点を持ちます。同じ位置でも面が異なればnormalとUVが異なるため、別vertexとして格納します。

        ```text
        24 vertices
          → 6 faces × 4 face-local vertices
        36 indices
          → 6 faces × 2 triangles × 3 indices
        ```

        C#とSlangでindex width、vertex stride、position / normal / UV offsetを一致させます。index値がvertex数以上にならないこと、各triangleのwindingが揃っていることも作成時に検証します。

        ## Resourceの所有

        vertex bufferとindex bufferはrendererが所有し、記録済みcommandが完了するまで破棄しません。meshを毎frameuploadせず、初期化時に作成してdraw間で再利用します。

        次はこのindexed cubeへ[3D Camera](story:Build/Recipes/Camera3D)を適用します。

        {SampleBundle("rendering.3d")}
        """, toc: true);

    [Story("Build/Recipes/Camera3D", Order = 12, SampleBundle = "rendering.3d")]
    public static Widget Camera3D(StoryContext ctx) => DocNew(ctx, $"""
        # Recipe: 3D Camera

        このrecipeは[Index bufferでcubeを描く](story:Build/Recipes/IndexedCube)のmeshを描画対象にします。

        indexed cubeへmodel、view、projectionを適用し、window resizeへ追従するperspective cameraを組み込むrecipeです。実装の正は`samples/LuxelTriangle/TriangleRenderer.cs`と`samples/LuxelTriangle/TutorialAbi.cs`です。

        ## 座標規約

        | 項目 | recipeの規約 |
        | --- | --- |
        | handedness | right-handed |
        | world axes | +X=右、+Y=上、cameraの前方=-Z |
        | clip/depth | shader出力後のdepthは0..1 |
        | matrix | CPUは`System.Numerics.Matrix4x4`、shaderはmatrix×column-vector |

        規約をbackendごとに変えず、Vulkan / DirectX 12から同じ最終clip spaceに見えるようbackend変換を1か所へ閉じ込めます。

        ## Model、View、Projection

        CPU上の意味は`model * view * projection`です。現在のABIはmatrixをtransposeしてroot argumentsへ格納し、Slangではmatrix×column-vectorとして適用します。

        ```csharp
        Matrix4x4 model = Matrix4x4.CreateRotationY(angle);
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            new Vector3(0, 1.5f, 3.5f), Vector3.Zero, Vector3.UnitY);
        Matrix4x4 projection = CreatePerspective(width, height);

        args.Model = Matrix4x4.Transpose(model);
        args.ViewProjection = Matrix4x4.Transpose(view * projection);
        ```

        ```slang
        float4 worldPosition = mul(g_args.model, float4(vertex.position, 1));
        float4 clipPosition = mul(g_args.viewProjection, worldPosition);
        ```

        matrixの掛け算順とmemory layoutは別問題です。objectがorbitする場合はmodelの順序、backend間で上下が逆ならprojectionとviewportのY補正が重複していないかを確認します。

        ## Resizeとaspect

        aspectはreadback用のaligned strideではなく、resize callbackで確定したvisible client width / heightから計算します。最小化中の0×0ではprojection計算とrenderを停止し、正のsizeへ戻った次frameでtargetとprojectionをまとめて更新します。

        ```csharp
        float aspect = visibleWidth / (float)visibleHeight;
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
            fieldOfView, aspect, nearPlane, farPlane);
        ```

        nearは0より大きく、farより十分小さくします。正方形、横長、縦長へresizeし、cubeの辺の比率と中心位置が維持されることを確認します。

        {SampleBundle("rendering.3d")}
        """, toc: true);

    [Story("Build/Recipes/HeadlessScene2D", Order = 13, SampleBundle = "rendering.2d")]
    public static Widget HeadlessScene2D(StoryContext ctx) => DocNew(ctx, $$"""
        # Recipe: Headless Scene2D Render

        静的な `Scene2D` を `Camera2D.Pixels` で一度だけSkia CPU rasterizerへ描画し、決定的なpixel hashを検証するstandalone recipeです。input、retained canvas、interactive cameraはこのbundleには含まれません。

        {{StoryRef(ctx, "Examples/2D/VectorPaths")}}

        {{SampleBundle("rendering.2d")}}
        """, toc: true);
}
