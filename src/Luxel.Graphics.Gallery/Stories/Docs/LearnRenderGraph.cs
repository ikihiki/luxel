using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>Graphicsの2D章に続いて、RenderGraphを動くGalleryストーリーから段階的に学ぶ章。</summary>
public static class LearnRenderGraph
{
    [Story("Learn/Graphics/RenderGraph/Overview", Order = 30, Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # RenderGraph入門

        {{RenderingCourseCatalog.Meta("Learn/Graphics/RenderGraph/Overview", "Beginner+", "Standalone + DevTools", "Vulkan / DirectX 12", "Graphics / Synchronization")}}

        RenderGraphは、複数passがどのresourceを読み書きするか宣言し、barrier、不要passのculling、transient resourceの寿命をまとめて扱う仕組みです。この章は[Graphicsの同期](story:Learn/Graphics/Synchronization)で手書きBarrierとSubmitを理解した後に進めてください。

        ## 動くサンプル

        次のストーリーは、2D UIを入力として横方向・縦方向のblur passを実行し、左半分に元画像、右半分にblur結果を合成します。

        {{StoryRef(ctx, "Examples/RenderGraph/Blur")}}

        ```text
        External UI buffer
          → BlurH (Transient buffer)
          → BlurV (Transient buffer)
          → Composite
          → External output buffer
        ```

        この小さなgraphを各ページで順番に分解します。

        | ページ | 学ぶこと |
        | --- | --- |
        | Resources | External / Transientと論理handle |
        | Passes | AddPass、Read / Write、Executeと自動barrier |
        | Compilation | culling、lifetime解析、aliasing |
        | Lifecycle | submit、GPU完了、dispose、resize |
        | Debugging | validationとDevToolsによる依存の可視化 |

        RenderGraph本体は`src/Luxel.Graphics.RenderGraph/`にあります。APIだけを一覧したい場合はRenderGraph Guideを参照してください。
        """;

    [Story("Learn/Graphics/RenderGraph/Resources", Order = 31, Toc = true)]
    public static StoryResult Resources(StoryContext ctx) => $$"""
        # リソースとハンドル

        {{RenderingCourseCatalog.Meta("Learn/Graphics/RenderGraph/Resources", "Beginner+", "Standalone + DevTools", "Vulkan / DirectX 12", "RenderGraph Overview")}}

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
        """;

    [Story("Learn/Graphics/RenderGraph/Passes", Order = 32, Toc = true)]
    public static StoryResult Passes(StoryContext ctx) => $$"""
        # パスと依存関係

        {{RenderingCourseCatalog.Meta("Learn/Graphics/RenderGraph/Passes", "Intermediate", "Standalone + DevTools", "Vulkan / DirectX 12", "Resources")}}

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
        """;

    [Story("Learn/Graphics/RenderGraph/Compilation", Order = 33, Toc = true)]
    public static StoryResult Compilation(StoryContext ctx) => $$"""
        # Cullingとaliasing

        {{RenderingCourseCatalog.Meta("Learn/Graphics/RenderGraph/Compilation", "Intermediate", "Standalone + DevTools", "Vulkan / DirectX 12", "Passes")}}

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
        """;

    [Story("Learn/Graphics/RenderGraph/Lifecycle", Order = 34, Toc = true)]
    public static StoryResult Lifecycle(StoryContext ctx) => $$"""
        # フレーム寿命とresize

        {{RenderingCourseCatalog.Meta("Learn/Graphics/RenderGraph/Lifecycle", "Intermediate", "Standalone + DevTools", "Vulkan / DirectX 12", "Compilation / Synchronization")}}

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
        """;

    [Story("Learn/Graphics/RenderGraph/Debugging", Order = 35, Toc = true)]
    public static StoryResult Debugging(StoryContext ctx) => $$"""
        # ValidationとDevTools

        {{RenderingCourseCatalog.Meta("Learn/Graphics/RenderGraph/Debugging", "Intermediate", "Standalone + DevTools", "Vulkan / DirectX 12", "Lifecycle")}}

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

        次はIndexed Cubeや3D Cameraと組み合わせるか、[Bloom3D](story:Examples/RenderGraph/Bloom3D)で複数passの完成例を確認してください。
        """;
}
