using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>初心者向けRenderGraph教材。実行可能な正は samples/LuxelTriangle。</summary>
public static partial class DocsRenderingLearn
{
    [Story("Learn/Grapics/RenderGraph", Order = 9)]
    public static Widget RenderGraph(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # RenderGraph

        {{RenderingCourseCatalog.Meta("Learn/Grapics/RenderGraph", "Beginner+", "Standalone + DevTools", "Vulkan / DirectX 12", "Synchronization")}}

        RenderGraphは、複数passがどのresourceを読み書きするか宣言し、実行順の検証、barrier、不要passのculling、transient resourceの寿命をまとめて扱います。前ページの同期では手書きBarrierとSubmit系methodを扱いました。RenderGraphはそのうちpass間のGPU依存をRead/Write宣言から構成します。

        実行sampleは `samples/LuxelTriangle/Program.cs`、`samples/LuxelTriangle/TriangleRenderer.cs`、`samples/LuxelTriangle/TutorialAbi.cs`、scene shaderの`shaders/tutorial_3d.slang`、post-process shaderの`shaders/compute_tutorial_postprocess.slang`です。sampleにはtextureやcameraを含む完成済みsceneを使いますが、このページではgraphのresourceとpass宣言だけに注目します。indexed meshとcameraの作り方は[Indexed Cube](story:Build/Recipes/IndexedCube)と[3D Camera](story:Build/Recipes/Camera3D)へ分けています。RenderGraph本体は`src/Luxel.RenderGraph/`にあります。

        ## 実行と期待結果

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

        `graph`はdirect描画と同じ結果を1 passのRenderGraph経由で作り、移行前後の画像を比較する段階です。`post`はsceneをtransient color/depthへ描画し、colorをtransient bufferへcopyし、compute shaderで寒色のshadowと柔らかなvignetteを加えてexternal framebufferへ書きます。どちらも3 frame smokeが終了コード0で完了し、通常起動ではresize後も同じ構成を新しいsizeで再構築します。

        shader sourceを変更した場合はcacheも更新します。

        ```powershell
        dotnet msbuild shaders/Luxel.ShaderCache.proj -t:CompileLuxelShaderCache
        ```

        ## 実sampleのgraph実装

        以下は`TriangleRenderer.cs`の実regionです。Learnページ側に別実装を複製せず、solutionでbuildされるsourceを表示します。

        {{SampleSource("samples/LuxelTriangle/TriangleRenderer.cs", "render-graph-frame")}}

        ## direct → graph → post

        | stage | resourceとpass | 学ぶこと |
        | --- | --- | --- |
        | `lighting` | renderer所有のcolor/depthへ直接描画し、手書きbarrier後にframebufferへcopy | command順とresource寿命の基準 |
        | `graph` | 同じ処理を1個のgraph passとして登録 | pass名、Read/Write宣言、Compile/Execute、移行時の同値性 |
        | `post` | transient scene color/depth → copy用transient buffer → compute → external framebuffer | pass間依存、自動barrier、culling、transient寿命 |

        最初から複雑なgraphへ書き換えず、まず1 passでdirect版と同じ画像を作ると、camera、pipeline、windingのbugとgraph宣言のbugを分けられます。その後、scene / copy / postへ分割します。

        ## ExternalとTransient

        **External resource**はgraphの外で作られ、`ImportBuffer`または`ImportTexture`で取り込みます。sampleの最終framebufferのように、presentや次frameでも必要なresourceです。所有者はrendererであり、RenderGraphをdisposeしてもexternal resourceは解放されません。

        **Transient resource**は`CreateBuffer`または`CreateTexture`で論理的に宣言します。物理resourceはcompile時に割り当てられ、graphが所有し、graphのdisposeで解放されます。`post`のscene color、scene depth、copy用bufferはそのframeの途中だけ必要なのでtransientに適しています。

        ```csharp
        using var graph = new RenderGraph(_device);
        TextureHandle sceneColor = graph.CreateTexture(
            new TextureDesc(width, height, GpuFormat.Rgba8Unorm), "SceneColor");
        TextureHandle sceneDepth = graph.CreateTexture(
            new TextureDesc(width, height, GpuFormat.D32Float, TextureKind.Depth), "SceneDepth");
        BufferHandle copiedColor = graph.CreateBuffer(
            new BufferDesc(framebufferBytes, GpuMemoryKind.HostMapped), "CopiedColor");
        BufferHandle output = graph.ImportBuffer(_framebuffer, "PresentFramebuffer");
        ```

        handleはgraph内の論理IDで、`GpuTexture`や`GpuBuffer`そのものではありません。execute callback内で`ctx.Texture(handle)` / `ctx.Buffer(handle)`に解決します。defaultのinvalid handleやcompile前の解決は失敗します。handle自体にはgraph識別子がないため、別graphのhandleを混ぜると同じIDの別resourceを誤参照し得ます。handleを作ったgraphのscopeから外へ持ち出さないでください。

        ## Read / Writeが依存とbarrierを作る

        ```csharp
        graph.AddPass("Scene", PassQueue.Graphics)
            .Write(sceneColor, TextureUsage.ColorAttachment)
            .Write(sceneDepth, TextureUsage.DepthAttachment)
            .Execute(ctx => RecordScene(ctx.Cmd, ctx.Texture(sceneColor), ctx.Texture(sceneDepth)));

        graph.AddPass("CopyScene", PassQueue.Graphics)
            .Read(sceneColor, TextureUsage.CopySource)
            .Write(copiedColor, ResourceUsage.CopyDest)
            .Execute(ctx => ctx.Cmd.CopyTextureToBuffer(
                ctx.Texture(sceneColor), ctx.Buffer(copiedColor), stridePixels));

        graph.AddPass("PostProcess", PassQueue.Compute)
            .Read(copiedColor, ResourceUsage.StorageBufferRead)
            .Write(output, ResourceUsage.StorageBufferWrite)
            .Execute(ctx => RecordPost(ctx.Cmd, ctx.Buffer(copiedColor), ctx.Buffer(output)));
        ```

        宣言はdocumentationではなく実行計画の入力です。write→read、write→write、read→writeのhazardに対して、RenderGraphはpass境界へstage barrierを挿入します。同一pass内でrenderしてcopyするなど複数stageを使う場合、そのpass内部の順序と必要なbarrierはcallback側の責任です。passへ宣言していないresourceをcallbackで触るとgraphは依存を認識できません。

        現在の実行順は**依存からtopological sortするのではなく、passの登録順そのまま**です。producerをconsumerより先に登録してください。`PassQueue.Graphics`と`PassQueue.Compute`は分類と診断に使われますが、**現在はどちらも`device.MainQueue`で実行**されます。公開されたasync compute実行やqueue間同期があるとは考えないでください。`AsyncCompute`をこのtutorialの並列実行手段として使いません。

        ## Culling、aliasing、終端resource

        compileはexternal resourceを最終成果物とみなし、それへ到達しないpassを後ろからcullします。たとえばdebug用transientへ書くだけで、その結果をexternal outputへつながないpassは実行されません。残したいpassは、その出力が最終framebufferまでRead/Write依存で到達するよう宣言します。「登録したから必ず実行される」わけではありません。

        lifetimeが重ならない同形transientは同じ物理slotを共有できます。bufferはsizeとmemory kind、textureはwidth、height、format、color/depth kindが一致することがaliasing候補です。alias境界も同じ物理resourceとしてhazard追跡され、必要なbarrierが入ります。aliasingは論理resourceの同時利用を許す機能ではなく、compileが非重複寿命を証明できた場合のmemory再利用です。

        external resourceはaliasing対象ではなく、graphも破棄しません。最終external writeの後には後段のcopy/presentから見えるよう保守的な終端barrierが入ります。ただしCPUから読む時点のGPU完了は別問題なので、sampleは`SubmitAndWait`後にpresentします。

        ## 1 frameごとの構築、resize、dispose

        tutorialは毎frame、新しいRenderGraphへ現在のwidth/heightとframebufferを登録し、commandへexecuteしてsubmitします。graphはcompile後に変更できないため、毎frame再構築するとframe固有のresourceとpassを素直に表現できます。

        ```text
        graphを構築
          → passを登録
          → command記録中にgraph.Execute(command)
          → command.Finish()
          → MainQueue.SubmitAndWait(command)
          → graph.Dispose()
          → external framebufferをPresent
        ```

        **graphを`SubmitAndWait`より前にdisposeしてはいけません。** disposeはgraph所有のtransient resourceを解放するため、GPUがまだ参照している可能性があります。現行sampleでは完了待ちの後にdisposeします。複数frameを同時進行させる場合はgraphまたはそのtransient allocationをframe slotが所有し、そのslotに対応するGPU処理の完了後に破棄・再利用します。

        resize時はqueue idle後にexternal framebufferを作り直し、次frameのgraphを新しいvisible width/heightとstrideから構築します。compile済みgraphのdescriptorだけを書き換えることはできません。最小化の0×0ではgraphや0-size textureを作らず描画を休止します。終了時も最後のsubmit完了後にgraphをdisposeし、external resource、pipeline、deviceの順に閉じます。

        ## Backend差

        VulkanとD3D12でgraphの論理passと依存宣言は共通です。backend差はbarrier command、SPIR-V/DXIL、D3D12 readback row pitchなど下層へ閉じ込めます。post shaderのroot argumentsとbuffer strideは両backendで同じABIにし、visible widthとaligned strideを混同しません。graphを使ってもGPU完了やpresentが自動になるわけではありません。

        ## Validation errorの読み方

        - `無効なハンドル` → default handle、ID 0、または範囲外handleをRead/Write/resolveしている。別graphのhandle混入は同じIDの誤参照になる場合もある
        - `CopyDest は書き込みです。 Write() を使ってください。` → write usageを`Read`へ渡した。`Write(handle, ResourceUsage.CopyDest)`へ直す
        - `SampledPixel は読み込みです。 Read() を使ってください。` → read usageをtextureの`Write`へ渡した。`Read(handle, TextureUsage.SampledPixel)`へ直す
        - `Compile/Execute 後にグラフを変更することはできません。` → `Execute`後にresource/passを追加した。次frame用graphを新しく作る
        - `リソース ... は未割当 (Compile 未実行)` → execute callback外など、compile前に物理resourceへ解決しようとした
        - passが実行されない → external outputまで依存が届かずcullされた、またはbuilderの最後に`Execute(...)`しておらずpassが登録されていない
        - 画像が古い / 破損する → callbackで使ったresourceのRead/Write宣言漏れ、usage/stage違い、またはgraphをGPU完了前にdisposeした
        - graphとpostの順が逆になる → 現在は登録順実行なのでproducer/consumerの追加順を確認

        ## DevToolsで依存を見る

        `RenderGraph.Execute`は`EngineDiagnostics.RenderGraph`が有効なとき、compile後の`DiagRenderGraph`を発行します。`src/Luxel.Controls/RenderGraphNodes.cs`の`RenderGraphNodes.Build(...)`が、**passをnode、resourceのwriter→reader依存をedge**へ変換し、DevToolsの読み取り専用NodeGraphへ渡します。

        DevToolsではpass名、`Graphics` / `Compute`分類、Read/Write resource、`(culled)`表示、transientのphysical slot / alias情報を確認します。画面が空なら診断channelが有効か、graphが実際に`Execute`されたか、DevTools listenerが`EngineDiagnostics.RenderGraph`を購読しているかを順に確認してください。この経路は可視化であり、実行順変更やasync computeを行うschedulerではありません。
        """, toc: true);
    }
}
