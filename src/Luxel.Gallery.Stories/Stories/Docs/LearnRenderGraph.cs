using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>RenderGraphを段階的に学ぶ独立コース。実行可能な正は samples/LuxelTriangle。</summary>
public static class LearnRenderGraph
{
    [Story("Learn/RenderGraph/Overview", Order = 0)]
    public static Widget Overview(StoryContext ctx) => DocNew(ctx, $$"""
        # RenderGraph入門

        {{RenderGraphCourseCatalog.Meta("Learn/RenderGraph/Overview", "Beginner+", "Grapics / Synchronization")}}

        RenderGraphは、複数passがどのresourceを読み書きするか宣言し、barrier、不要passのculling、transient resourceの寿命をまとめて扱う仕組みです。このコースは[Grapicsの同期](story:Learn/Grapics/Synchronization)で手書きBarrierとSubmitを理解した後に進めてください。

        ## このコースで作るもの

        実行可能な正は`samples/LuxelTriangle`です。完成済みのtexture付きsceneを題材に、direct描画から1 passのgraphへ移行し、最後にcompute post-processを追加します。

        | stage | resourceとpass | 学ぶこと |
        | --- | --- | --- |
        | `lighting` | renderer所有のcolor/depthへ直接描画 | command順とresource寿命の基準 |
        | `graph` | 同じ描画を1個のgraph passとして登録 | AddPass、Read/Write、Execute、移行時の同値性 |
        | `post` | transient scene color/depth → copy buffer → compute → external framebuffer | pass間依存、自動barrier、culling、aliasing |

        最初から複雑なgraphへ書き換えず、まずdirect版と同じ画像を1 passで作ります。これによりcamera、pipeline、windingのbugとgraph宣言のbugを分離できます。

        ## 実行する

        ```powershell
        dotnet build samples/LuxelTriangle/LuxelTriangle.csproj
        dotnet test tests/Luxel.Tests/Luxel.Tests.csproj --filter RenderGraphTests
        dotnet run --project samples/LuxelTriangle -- vk --stage graph --frames 3
        dotnet run --project samples/LuxelTriangle -- vk --stage post --frames 3
        dotnet run --project samples/LuxelTriangle -- vk --stage post --size 801x603
        # Windowsのみ
        dotnet run --project samples/LuxelTriangle -- dx --stage graph --frames 3
        dotnet run --project samples/LuxelTriangle -- dx --stage post --frames 3
        ```

        `graph`はdirect描画と同じ結果をRenderGraph経由で作ります。`post`はsceneをtransient targetへ描画し、compute shaderで寒色のshadowとvignetteを加えてexternal framebufferへ書きます。

        ## 実sampleのgraph実装

        次のsourceはsolutionで実際にbuildされます。各ページではこの全体像からresource、pass、compile、lifecycleを順番に取り出します。

        {{SampleSource("samples/LuxelTriangle/TriangleRenderer.cs", "render-graph-frame")}}

        RenderGraph本体は`src/Luxel.Graphics.RenderGraph/`、post-process shaderは`shaders/compute_tutorial_postprocess.slang`にあります。APIだけを一覧したい場合は[RenderGraph Guide](story:Reference/Guides/RenderGraph)を参照してください。
        """, toc: true);

    [Story("Learn/RenderGraph/Resources", Order = 1)]
    public static Widget Resources(StoryContext ctx) => DocNew(ctx, $$"""
        # リソースとハンドル

        {{RenderGraphCourseCatalog.Meta("Learn/RenderGraph/Resources", "Beginner+", "RenderGraph Overview")}}

        graphへ登録するresourceは**External**と**Transient**に分かれます。違いは作成場所と所有者です。

        | 種類 | 登録API | 所有者 | 典型例 |
        | --- | --- | --- | --- |
        | External buffer | `ImportBuffer` | renderer / application | present用framebuffer、永続mesh buffer |
        | External texture | `ImportTexture` | renderer / application | swapchain相当のtarget、永続texture |
        | Transient buffer | `CreateBuffer` | RenderGraph | frame途中だけ使うcopy・compute buffer |
        | Transient texture | `CreateTexture` | RenderGraph | scene color、depth、post-process中間target |

        ```csharp
        using Luxel.Graphics.RenderGraph;

        using var graph = new RenderGraph(device);
        TextureHandle sceneColor = graph.CreateTexture(
            new TextureDesc(width, height, GpuFormat.Rgba8Unorm), "SceneColor");
        TextureHandle sceneDepth = graph.CreateTexture(
            new TextureDesc(width, height, GpuFormat.D32Float, TextureKind.Depth), "SceneDepth");
        BufferHandle copiedColor = graph.CreateBuffer(
            new BufferDesc(framebufferBytes, GpuMemoryKind.HostMapped), "CopiedColor");
        BufferHandle output = graph.ImportBuffer(framebuffer, "PresentFramebuffer");
        ```

        ## 論理handleを使う理由

        `BufferHandle`と`TextureHandle`はgraph内の論理IDです。setup中は物理`GpuBuffer` / `GpuTexture`を直接渡さず、execute callback内で解決します。

        ```csharp
        graph.AddPass("PostProcess", PassQueue.Compute)
            .Read(copiedColor, ResourceUsage.StorageBufferRead)
            .Write(output, ResourceUsage.StorageBufferWrite)
            .Execute(pass => RecordPost(
                pass.Cmd,
                pass.Buffer(copiedColor),
                pass.Buffer(output)));
        ```

        この間接化により、compileはresourceの寿命を調べ、同時に使われないtransientへ同じ物理slotを割り当てられます。

        ## handleのscope

        default値のhandleは`Id == 0`で無効です。compile前に物理resourceへ解決することもできません。またhandleにはgraph識別子がないため、別graphのhandleを混ぜると同じIDの別resourceを誤参照する可能性があります。

        - handleを作ったgraphの外へ保存しない
        - frameをまたいでhandleを再利用しない
        - external resourceの寿命はgraphではなくimportした側が管理する
        - transientのdescriptorはcompile後に変更せず、次frameで作り直す
        """, toc: true);

    [Story("Learn/RenderGraph/Passes", Order = 2)]
    public static Widget Passes(StoryContext ctx) => DocNew(ctx, $$"""
        # パスと依存関係

        {{RenderGraphCourseCatalog.Meta("Learn/RenderGraph/Passes", "Intermediate", "Resources")}}

        passは`AddPass`で作り、`Read` / `Write`でaccessを宣言し、最後の`Execute`でgraphへ登録します。宣言はdocumentationではなく、barrierとlifetime解析の入力です。

        ```csharp
        graph.AddPass("Scene", PassQueue.Graphics)
            .Write(sceneColor, TextureUsage.ColorAttachment)
            .Write(sceneDepth, TextureUsage.DepthAttachment)
            .Execute(pass => RecordScene(
                pass.Cmd,
                pass.Texture(sceneColor),
                pass.Texture(sceneDepth)));

        graph.AddPass("CopyScene", PassQueue.Graphics)
            .Read(sceneColor, TextureUsage.CopySource)
            .Write(copiedColor, ResourceUsage.CopyDest)
            .Execute(pass => pass.Cmd.CopyTextureToBuffer(
                pass.Texture(sceneColor),
                pass.Buffer(copiedColor),
                stridePixels));
        ```

        ## Read / Writeがbarrierを作る

        write→read、write→write、read→writeのhazardでは、前後のusageから`GpuStage`を求めてpass境界へbarrierを挿入します。read→readだけなら書き込みがないためbarrierは不要です。

        | usage | stage | access |
        | --- | --- | --- |
        | `TextureUsage.ColorAttachment` | `ColorOutput` | Write |
        | `TextureUsage.DepthAttachment` | `DepthStencil` | Write |
        | `TextureUsage.SampledPixel` | `PixelShader` | Read |
        | `TextureUsage.CopySource` / `CopyDest` | `Copy` | Read / Write |
        | `ResourceUsage.StorageBufferRead` / `Write` | `ComputeShader` | Read / Write |
        | `ResourceUsage.UniformBuffer` | `AllGraphics` | Read |
        | `ResourceUsage.IndirectArgs` | `DrawIndirect` | Read |

        callbackで使うresourceを宣言しなければ、graphは依存を認識できません。同じpass内でrender後にcopyするなど複数stageを使う場合、そのpass内部の順序とbarrierはcallback側の責任です。

        ## 現在の実行順とqueue

        現在は依存からtopological sortせず、**passの登録順で実行**します。producerをconsumerより先に登録してください。`PassQueue.Graphics`と`PassQueue.Compute`は分類と診断に使われますが、どちらも`device.MainQueue`で実行されます。`AsyncCompute`を並列実行の契約として扱わないでください。
        """, toc: true);

    [Story("Learn/RenderGraph/Compilation", Order = 3)]
    public static Widget Compilation(StoryContext ctx) => DocNew(ctx, $$"""
        # Cullingとaliasing

        {{RenderGraphCourseCatalog.Meta("Learn/RenderGraph/Compilation", "Intermediate", "Passes")}}

        `Execute(command)`を初めて呼ぶとgraphをcompileします。compileはpassを検証し、resourceの寿命を解析し、不要passをcullして、transientの物理resourceを割り当てます。

        ## External outputから必要なpassを逆算する

        external resourceはgraph外へ結果を渡す終端です。compileはexternalへのwriteから依存を後ろ向きに辿り、到達しないpassをcullします。

        ```text
        SceneColor → CopyScene → PostProcess → External framebuffer
        DebugOnly  ─────────────────────────X  (externalへ届かないのでculled)
        ```

        「登録したpassは必ず実行される」とは限りません。残したい処理は、その出力が最終external resourceまでRead/Write依存でつながるようにします。副作用だけのpassを表す専用APIは現在ありません。

        ## Transient lifetimeとaliasing

        compileは各論理resourceの最初のwriteと最後のreadを調べます。寿命が重ならずdescriptorが同じtransientは、同じ物理slotを共有できます。

        - bufferはsizeと`GpuMemoryKind`が一致する
        - textureはwidth、height、format、color/depth kindが一致する
        - external resourceはaliasingしない
        - 寿命が重なる論理resourceは共有しない

        aliasingは同時利用を許可する機能ではなく、compileが非重複を証明した場合のmemory再利用です。同じ物理resourceへ別の論理resourceが割り当てられる境界もhazard追跡され、必要なbarrierが入ります。

        ## 終端barrier

        external resourceへ最後にwriteした場合、graphは後段のcopyやpresentから見えるよう保守的な終端barrierを記録します。ただし、これはCPUから見たGPU完了待ちではありません。submitとresource破棄の関係は次ページで扱います。
        """, toc: true);

    [Story("Learn/RenderGraph/Lifecycle", Order = 4)]
    public static Widget Lifecycle(StoryContext ctx) => DocNew(ctx, $$"""
        # フレーム寿命とresize

        {{RenderGraphCourseCatalog.Meta("Learn/RenderGraph/Lifecycle", "Intermediate", "Compilation / Synchronization")}}

        tutorialは毎frame新しいRenderGraphを作り、現在のsizeとexternal framebufferを登録します。graphはcompile後に変更できないため、frame固有のpassとresourceを素直に表現できます。

        ```text
        RenderGraphを構築
          → resourceとpassを登録
          → command記録中にgraph.Execute(command)
          → command.Finish()
          → MainQueue.SubmitAndWait(command)
          → graph.Dispose()
          → external framebufferをPresent
        ```

        ```csharp
        using var graph = BuildFrameGraph(width, height, framebuffer);
        using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();

        graph.Execute(command);
        command.Finish();
        device.MainQueue.SubmitAndWait(command);
        surface.Present(framebuffer);
        ```

        ## disposeはGPU完了後

        graphのdisposeはgraph所有のtransient resourceを解放します。GPUがcommandを実行中の可能性があるため、`SubmitAndWait`より前にdisposeしてはいけません。複数frameを同時進行させる場合は、graphまたはtransient allocationをframe slotが所有し、そのslotのGPU完了後に再利用します。

        External resourceはgraphが破棄しません。rendererがgraphより長く保持し、最後のsubmit完了後に解放します。

        ## resizeと0×0

        resizeではqueue idle後にexternal framebufferを作り直し、次frameのgraphを新しいvisible width、height、strideから構築します。compile済みgraphのdescriptorだけを書き換えることはできません。最小化中の0×0ではgraphや0-size textureを作らず描画を休止します。

        ## Backend差

        VulkanとDirectX 12でlogical passと依存宣言は共通です。backend差はbarrier command、SPIR-V/DXIL、D3D12 readback row pitchなど下層へ閉じ込めます。RenderGraphを使ってもsubmit、GPU完了、presentが自動になるわけではありません。
        """, toc: true);

    [Story("Learn/RenderGraph/Debugging", Order = 5)]
    public static Widget Debugging(StoryContext ctx) => DocNew(ctx, $$"""
        # ValidationとDevTools

        {{RenderGraphCourseCatalog.Meta("Learn/RenderGraph/Debugging", "Intermediate", "Lifecycle")}}

        RenderGraphの不具合は、API validation、passのculling、resource宣言漏れ、GPU lifetimeの順に切り分けます。

        ## よくあるvalidation error

        | message / 症状 | 確認すること |
        | --- | --- |
        | `無効なハンドル` | default handle、ID 0、範囲外handleを渡していないか |
        | `CopyDest は書き込みです。 Write() を使ってください。` | write usageを`Read`へ渡していないか |
        | `SampledPixel は読み込みです。 Read() を使ってください。` | read usageを`Write`へ渡していないか |
        | `Compile/Execute 後にグラフを変更することはできません。` | `Execute`後にresourceやpassを追加していないか |
        | `リソース ... は未割当 (Compile 未実行)` | callback外やcompile前に物理resourceを解決していないか |
        | passが実行されない | external outputまで依存が届かずcullされていないか、最後に`Execute(...)`したか |
        | 画像が古い / 壊れる | callbackで使うresourceのRead/Write宣言、usage、stageが正しいか |
        | frame後に不定期に壊れる | GPU完了前にgraphやexternal resourceを破棄していないか |

        ## DevToolsで依存を見る

        `EngineDiagnostics.RenderGraph`が有効な場合、`RenderGraph.Execute`はcompile後の`DiagRenderGraph`を発行します。`RenderGraphNodes.Build(...)`はpassをnode、resourceのwriter→reader依存をedgeへ変換し、DevToolsの読み取り専用NodeGraphへ渡します。

        DevToolsでは次を確認できます。

        - pass名と`Graphics` / `Compute`分類
        - 各passのRead / Write resource
        - `(culled)`になったpass
        - transientのphysical slotとalias情報
        - compile後に実行されたpass数

        画面が空なら、diagnostic channelが有効か、graphが`Execute`されたか、listenerが`EngineDiagnostics.RenderGraph`を購読しているかを確認します。この表示は可視化であり、実行順変更やasync computeを行うschedulerではありません。

        {{StoryRef(ctx, "Examples/RenderGraph/Blur")}}

        次は[Indexed Cube](story:Build/Recipes/IndexedCube)や[3D Camera](story:Build/Recipes/Camera3D)と組み合わせるか、[Bloom3D](story:Examples/RenderGraph/Bloom3D)で複数passの完成例を確認してください。
        """, toc: true);
}
