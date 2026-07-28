using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>初心者向けレンダリング学習経路。実行可能な正は samples/LuxelTriangle。</summary>
public static partial class DocsRenderingLearn
{
    [Story("Learn/Rendering/Basics/Overview", Order = 0)]
    public static Widget Overview(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # Rendering 学習ガイド

        {RenderingCourseCatalog.Meta("Learn/Rendering/Basics/Overview", "Beginner", "Standalone + Gallery", "Vulkan / DirectX 12", "なし")}

        この章は、Gallery のデモを見るだけでなく、自分のウィンドウへ描画できるところまでを順番に進みます。最小アプリの実装はリポジトリの `samples/LuxelTriangle/` が単一の正です。

        ## 推奨アプリ構築ルート

        BasicsからThreeDの順序はcourse catalogから生成されます。2DとRasterizer Internalsは、このルートを終えた後に目的に応じて選ぶ独立トラックです。

        {RenderingCourseCatalog.ApplicationRouteMarkdown()}

        **検索キーワード:** triangle / texture / camera / render graph / glTF / blank screen / 真っ黒

        > [!IMPORTANT]
        > `GpuView` と `IGpuScene` はGallery内でデモを表示するためのハーネスです。通常アプリでは `WindowSystem`、`Window`、`GpuSurface` を使います。

        ## どのAPIまで学ぶか

        R1の三角形、R2のbuffer ABIとshader cache、R3のtexture付きquadからdepth/culling/方向光に続き、R4ではframe loopとRenderGraphへ進みます。`--stage graph`でdirect描画を1 passへ移し、`--stage post`でtransient resourceとcompute post-processを追加します。R5では2D、ECSを使わない静的glTF、デバッグ、publishまで進みます。複数frame-in-flightは本番設計として説明しますが、現在の公開queue APIとtutorialはper-frame fenceをまだ提供しません。
        """, toc: true);
    }


    [Story("Learn/Rendering/Basics/Environment", Order = 1)]
    public static Widget Environment(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # レンダリング環境を確認する

        {{RenderingCourseCatalog.Meta("Learn/Rendering/Basics/Environment", "Beginner", "Standalone", "Vulkan / DirectX 12", "Overview")}}

        ## 対応backend

        | OS | Window backend | GPU backend | 実行引数 |
        | --- | --- | --- | --- |
        | Windows | Win32 | Vulkan | `vk` |
        | Windows | Win32 | DirectX 12 | `dx` |
        | Linux | Silk.NET GLFW / X11 | Vulkan | `vk` |

        Linuxの実ウィンドウにはX11の `DISPLAY` が必要です。Wayland nativeは未対応です。headless環境では `eng/desktop/start.sh` または `xvfb-run` でX serverを用意します。

        ## ビルド

        ```powershell
        dotnet build samples/LuxelTriangle/LuxelTriangle.csproj
        dotnet run --project samples/LuxelTriangle -- vk
        # Windowsのみ
        dotnet run --project samples/LuxelTriangle -- dx
        ```

        成功すると暗い背景に赤・緑・青の三角形が表示されます。CIやsmoke testでは `--frames 3` を付けると自動終了します。

        ## Shader cache

        通常ビルドはGit管理済みの `shaders/compiled/` を使うため、Slang/DXCの事前導入は不要です。`.slang`を変更したときだけ次を実行します。

        ```powershell
        dotnet msbuild shaders/Luxel.ShaderCache.proj -t:CompileLuxelShaderCache
        ```

        生成物と `inputs.sha256` をshader sourceと一緒にコミットします。

        ## Backendとdeviceを作る実コード

        次は`LuxelTriangle`が実際にコンパイルするWindows/Linux両方のbackend選択です。Linuxではwindowが提供する`IVulkanWindowSurface`を`VulkanBackendOptions.WindowSurface`へ渡します。

        {{SampleSource("samples/LuxelTriangle/Program.cs", "device-and-surface-backend")}}

        surface生成と所有権を含む完全なevent loopは次のClear Colorページにあります。device、surface、window systemは`using`で所有者を明確にし、終了前にqueueをidleにします。

        ## 典型的な失敗

        - Linuxで `DISPLAY` がない → X11 serverを起動する
        - Linuxで `dx` を指定 → Vulkanの `vk` を使う
        - shader cache mismatch → 上記 `CompileLuxelShaderCache` を実行する
        - Vulkan deviceがない → Vulkan driverまたはlavapipeの導入を確認する
        """, toc: true);
    }


    [Story("Learn/Rendering/Basics/ClearColor", Order = 2, SampleBundle = "rendering.clear-color")]
    public static Widget ClearColor(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # オフラインClear Color

        {RenderingCourseCatalog.Meta("Learn/Rendering/Basics/ClearColor", "Beginner", "File-based offline app", "Vulkan / DirectX 12", "Environment")}

        `samples/ClearColor.cs` はプロジェクトファイルもwindow systemも必要としない、1ファイルのオフラインGPU sampleです。ファイル先頭の `#:project` が必要なLuxel projectを参照するため、チェックアウト後にそのまま実行できます。

        ```powershell
        dotnet run --file samples/ClearColor.cs -- vk
        ```

        shader、vertex buffer、graphics pipeline、surface、presentは使いません。処理は次の順です。

        ```text
        GpuDevice → offscreen render targetをclear
                  → host-mapped bufferへreadback
                  → clear-color.ppmへ保存
        ```

        `BeginRendering`のclear値だけでRGBA8 render targetを塗り、`ColorOutput → Copy` barrier後に`CopyTextureToBuffer`でCPU可視bufferへreadbackします。出力は汎用的に確認できるbinary PPMです。

        ## Row pitchと出力

        D3D12のtexture readback row pitchは256 byte単位なので、GPU側のRGBA8 strideは64 pixel単位へ揃えます。PPMへ保存するときは各rowのpaddingを除去してRGBへ変換します。`--size 801x603`で任意サイズ、`--output result.ppm`で保存先を指定できます。

        window、event loop、resize、surfaceは後続のinteractive sample側の責務であり、このsampleには含めません。

        {SampleBundle("rendering.clear-color")}
        """, toc: true);
    }


    [Story("Learn/Rendering/Basics/FirstTriangle", Order = 3, SampleBundle = "rendering.triangle")]
    public static Widget FirstTriangle(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # はじめての三角形

        {{RenderingCourseCatalog.Meta("Learn/Rendering/Basics/FirstTriangle", "Beginner", "Standalone + Gallery", "Vulkan / DirectX 12", "ClearColor")}}

        {{StoryRef(ctx, "Examples/3D/Triangle")}}

        上の表示はGalleryのoffscreenデモです。コピーして動かす完全なstandalone実装は次の4ファイルです。

        - `samples/LuxelTriangle/LuxelTriangle.csproj`
        - `samples/LuxelTriangle/Program.cs`
        - `samples/LuxelTriangle/TriangleRenderer.cs`
        - `shaders/tutorial_triangle.slang`

        ## 実sampleのABI

        {{SampleSource("samples/LuxelTriangle/TutorialAbi.cs", "triangle-abi")}}

        ## 描画の4段階

        1. `Malloc(..., HostMapped)`で3頂点を確保し、`Span<Vertex>`へ書く
        2. `GpuShaderCode.Load("tutorial_triangle")`からgraphics pipelineを作る
        3. commandへpipeline、root arguments、`Draw(3)`を記録する
        4. submit後、readback framebufferをsurfaceへpresentする

        ```csharp
        command.BeginRendering(target, null, 0.055f, 0.07f, 0.11f, 1)
            .SetGraphicsPipeline(pipeline)
            .SetRootArguments(new DrawArgs { VertexBufferIndex = vertices.BindlessIndex })
            .Draw(3)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(target, framebuffer);
        ```

        Slang側は `SV_VertexID` を使ってbindless bufferから頂点を読むため、vertex-input layout objectはありません。C#の `Vertex` とSlangの `Vertex` はどちらも `float4 position + float4 color = 32 byte` です。

        ```slang
        struct Vertex { float4 position; float4 color; }
        struct DrawArgs { uint vertexBufferIndex; }
        [[vk::push_constant]] DrawArgs g_args;
        [[vk::binding(0, 0)]] RWByteAddressBuffer g_buffers[];

        [shader("vertex")]
        VertexOut vsMain(uint vertexId : SV_VertexID)
        {
            Vertex v = g_buffers[g_args.vertexBufferIndex].Load<Vertex>(vertexId * 32);
            VertexOut o; o.position = v.position; o.color = v.color; return o;
        }

        [shader("pixel")]
        float4 psMain(VertexOut input) : SV_Target => input.color;
        ```

        つまり最小例のdata flowは`C# Vertex[] → HostMapped buffer → BindlessIndex → root args → SV_VertexID`です。このblockと上のcommand記録を組み合わせれば、サンプルファイルを開かなくても必要なbindingを追えます。

        > [!NOTE]
        > 入門サンプルは処理順を明確にするため毎フレーム `SubmitAndWait` します。複数frame-in-flightとfenceによる本番向け同期は後続のFrame Loopページで扱います。

        ## 典型的な失敗

        - clear colorだけ見える → pipeline、shader名、root arguments、`Draw(3)`を確認
        - 三角形が崩れる → C# / Slangのstruct sizeとoffsetを確認
        - resize後だけ壊れる → queue idle後にtarget/framebufferを再生成したか確認
        """, toc: true);
    }


    [Story("Learn/Rendering/Basics/BuffersAndBindings", Order = 4)]
    public static Widget BuffersAndBindings(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # バッファ、ABI、bindless binding

        {{RenderingCourseCatalog.Meta("Learn/Rendering/Basics/BuffersAndBindings", "Beginner", "Standalone + Gallery", "Vulkan / DirectX 12", "FirstTriangle")}}

        このページの実コードは `samples/LuxelTriangle/TutorialAbi.cs`、`TriangleRenderer.cs`、`shaders/tutorial_triangle.slang` です。確認コマンド:

        ```powershell
        dotnet build samples/LuxelTriangle/LuxelTriangle.csproj
        dotnet test tests/Luxel.Tests/Luxel.Tests.csproj --filter TutorialAbiTests
        dotnet run --project samples/LuxelTriangle -- vk --frames 3
        # Windowsのみ: dotnet run --project samples/LuxelTriangle -- dx --frames 3
        ```

        成功時はABI testが2件通り、実行すると暗い背景にRGB三角形が3 frame表示されます。

        ## Luxelでは「vertex buffer」もraw bindless buffer

        `GpuDevice.Malloc` が作る `GpuBuffer` は、作成時に `BindlessIndex` を得てraw storage buffer配列へ登録されます。vertex / index / storage / upload / readbackは別クラスではなく、**同じバッファをどう使うかという役割**です。

        | 役割 | 中身とアクセス | 推奨memory kind |
        | --- | --- | --- |
        | vertex | 頂点shaderが `SV_VertexID` からbyte offsetを計算してpull | 小さい更新データは`HostMapped`、大きい静的データは`DeviceLocal` |
        | index | index値をraw bufferからpullし、その値でvertexをpull。Luxel coreに`DrawIndexed`はない | 通常`DeviceLocal`、作成時にupload |
        | storage | compute / graphicsが任意offsetを読み書き | 通常`DeviceLocal` |
        | upload | CPUが書き、GPUが読む一時または頻繁更新データ | `HostMapped` |
        | readback | GPU copy後にCPUが読む | `HostCached` |

        `HostMapped` はCPU write向けで、write-combined / uncachedの場合があります。CPU readbackには使わず、`CopyBuffer`や`CopyTextureToBuffer`で `HostCached` へコピーします。`DeviceLocal` はCPUの `Span<T>` を持たずGPU処理向けです。古い資料の `HostCache` ではなく、API名は **`HostCached`** です。

        ## C# とSlangのABI

        tutorialの実構造体はテスト専用コピーではなく、renderer自身が使う `TutorialAbi.Vertex` と `TutorialAbi.DrawArgs` です。

        ```csharp
        [StructLayout(LayoutKind.Sequential)]
        public struct Vertex
        {
            public float Px, Py, Pz, Pw; // offset 0, float4 position
            public float R, G, B, A;     // offset 16, float4 color
        }                                // size 32

        public struct DrawArgs
        {
            public uint VertexBufferIndex; // offset 0, size 4
        }
        ```

        ```slang
        struct DrawArgs { uint vertexBufferIndex; };
        struct Vertex { float4 position; float4 color; };

        Vertex vertex = g_buffers[g_args.vertexBufferIndex]
            .Load<Vertex>(vertexId * 32);
        ```

        `float3`の直後へ別fieldを足す、C#側だけ`bool`を使う、field順を変える、といった変更はoffsetをずらします。曖昧な詰め方を避け、必要なら明示paddingを置き、`Marshal.SizeOf` / `Marshal.OffsetOf` testを同時に更新してください。buffer内の配列strideもstruct sizeと一致させます。

        行列は**置き場所とshaderの読み方に依存**します。Slang/HLSL既定のcolumn-major matrixをroot argsの型付きfieldとして受け、`mul(v, M)`する既存3Dコードは `Matrix4x4.Transpose` して渡します。一方、bufferから `Load4` を4回行ってrowを組み立てるper-instance matrixは転置しません。「Matrix4x4なら常にtranspose」ではありません。

        ## Root argumentsはraw bytes

        `SetRootArguments<T>` はunmanaged structをそのままbytes化し、Vulkanではpush constants、D3D12ではroot 32-bit constantsへ送ります。共通固定容量は **192 byte**、D3D12互換のためsizeは **4 byte単位**です。参照そのものではなく `BindlessIndex` や小さなscalar / matrixを入れます。大きな配列はbufferへ置いてindexだけを渡します。

        ```mermaid
        flowchart TB
        cpu["C#: DrawArgs raw bytes\nvertexBufferIndex = 17"] --> api["SetRootArguments\n4-byte units, max 192 bytes"]
        api --> vk["Vulkan\npush constants"]
        api --> dx["D3D12\nroot 32-bit constants"]
        vk --> slang["Slang: g_args.vertexBufferIndex"]
        dx --> slang
        heap["bindless raw buffer heap\nslot 17 = vertex data"] --> pull["g_buffers[17].Load<Vertex>()"]
        slang --> pull
        pull --> vertex["SV_VertexID -> byte offset -> Vertex"]
        ```

        ## 所有権と寿命

        `TriangleRenderer`がvertex bufferとpipelineを所有し、resize単位のtarget/framebufferも所有します。command bufferは1 frameだけです。dispose順は **queue idle → framebuffer / target → pipeline → vertex buffer → device**。bindless indexはbufferが生存中だけ有効なので、記録済みcommandが参照している間にbufferを破棄・再利用してはいけません。

        ## 典型的な失敗

        - `ArgumentException: 4-byte-compatible size` → root argsへ3 byteなどDWord非互換の型を渡した
        - `ArgumentOutOfRangeException: limited to 192 bytes` → 大きなデータをbufferへ移し、indexだけをroot argsへ置く
        - 三角形が崩れる → C# / Slangのsize、offset、stride、paddingを照合する
        - GPU validation / device lost → 寿命切れのbindless index、copy/barrier前後、範囲外offsetを確認する
        - CPU readbackが極端に遅い → `HostMapped`を直接読まず`HostCached`へGPU copyする
        """, toc: true);
    }


    [Story("Learn/Rendering/Basics/Shaders", Order = 5)]
    public static Widget Shaders(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # Slang shaderとGit cache

        {{RenderingCourseCatalog.Meta("Learn/Rendering/Basics/Shaders", "Beginner", "Standalone build / publish", "Vulkan / DirectX 12", "BuffersAndBindings")}}

        Luxelは `shaders/*.slang` を単一のsourceとして、Vulkan用SPIR-VとD3D12用DXILをGit管理します。通常のbuild / publishはcompilerを起動せずcacheを検証・コピーします。

        ## 1. ファイルを追加して分類する

        - `compute*.slang` または `raster2d_*.slang` はcompute。entry pointは `[shader("compute")] main`。
        - それ以外の `.slang` はgraphics。entry pointは `[shader("vertex")] vsMain` と `[shader("pixel")] psMain`。
        - SPIR-Vはどちらも1 sourceにつき `compiled/<name>.spv`。
        - compute DXILは `compiled/<name>.dxil`。
        - graphics DXILは `compiled/<name>.vs.dxil` と `compiled/<name>.ps.dxil`。

        したがって `shaders/my_effect.slang` をgraphicsとして追加すると、期待生成物は次です。

        ```text
        shaders/compiled/my_effect.spv
        shaders/compiled/my_effect.vs.dxil
        shaders/compiled/my_effect.ps.dxil
        ```

        compute shaderをgraphics名で追加するとDXIL compileは`vsMain` / `psMain`を探して失敗します。computeとして分類したい場合はfilenameを `compute...` または `raster2d_...` にしてください。

        ## 2. Cacheを再生成する

        repository rootで実行します。固定版Slang/DXCは必要なときだけ`tools/`へ自動取得されます。

        ```powershell
        dotnet msbuild shaders/Luxel.ShaderCache.proj -t:CompileLuxelShaderCache
        git status --short shaders
        ```

        source、全backend生成物、`shaders/compiled/inputs.sha256`を一緒にcommitします。manifestは全 `.slang` のSHA-256だけでなくschema、Slang/DXC version、SPIR-V/DXIL profileも固定します。`tools/`はlocal cacheでcommitしません。

        ## 3. 通常buildとpublish

        ```powershell
        dotnet build Luxel.slnx --no-restore
        dotnet publish samples/LuxelTriangle/LuxelTriangle.csproj -c Release -o artifacts/triangle-publish
        ```

        `Luxel.Shaders.targets`はbuild前にcache completenessと`inputs.sha256`一致を検証し、出力の`shaders/`へcopyします。publishにも同じcompiled filesが含まれます。`GpuShaderCode.Load("tutorial_triangle")`はbackendに応じて `.spv`、`.vs.dxil` / `.ps.dxil`を読みます。

        ## Backend差と固定binding

        Slang sourceは共通です。`[[vk::push_constant]]`のroot argsはVulkanのpush constants、同じbytesはD3D12の`b0` root constantsになります。`g_buffers[]`はVulkanのset 0 / binding 0とD3D12のunbounded UAV tableへ対応し、どちらも同じ`BindlessIndex`で引きます。shader側structのpaddingとroot argsの192 byte / 4 byte条件は両backend共通として扱ってください。

        ## 所有権と寿命

        sourceと`compiled/`はrepositoryが所有し、build output / publish outputは生成先が所有します。runtimeでは`GpuShaderCode.Load`のbytesから作った`GpuPipeline`をrendererが所有し、使用中commandが完了してからdisposeします。`.slang`だけ変更して古いpipeline/cacheを使い続けないでください。

        ## Errorから直す

        - `Slang シェーダキャッシュがありません` → `CompileLuxelShaderCache`を実行する
        - `キャッシュが不足しています: ...` → filename分類に対応するSPIR-V/DXILを再生成する
        - `Slang ソースまたはコンパイル設定が Git キャッシュと一致しません` → source変更後のcacheと`inputs.sha256`を再生成・commitする
        - `entry point 'vsMain'/'psMain' not found` → graphics entry名、またはcompute filename分類を直す
        - `entry point 'main' not found` → compute shaderに`main`を定義する
        - Slang type/layout error → C#側を先に合わせず、両言語のfield順・size・paddingを同じ変更で直す
        - publish後だけshader missing → projectが`shaders/Luxel.Shaders.targets`をimportしているか、publish出力の`shaders/`を確認する

        shader sourceを変更した場合だけcache regenerationが必要です。このページやC# ABI testだけの変更ではcompiled shaderを更新しません。
        """, toc: true);
    }


    [Story("Learn/Rendering/Basics/FrameLoopAndSynchronization", Order = 9)]
    public static Widget FrameLoopAndSynchronization(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # Frame loopと同期

        {{RenderingCourseCatalog.Meta("Learn/Rendering/Basics/FrameLoopAndSynchronization", "Beginner+", "Standalone", "Vulkan / DirectX 12", "DepthCullingLighting")}}

        実コードは `samples/LuxelTriangle/Program.cs` と `samples/LuxelTriangle/TriangleRenderer.cs` です。このページでは、1 frameを「CPUがcommandを記録する期間」だけでなく、GPU完了とpresentまで含む寿命として捉えます。

        ## 実行と期待結果

        ```powershell
        dotnet build samples/LuxelTriangle/LuxelTriangle.csproj
        dotnet run --project samples/LuxelTriangle -- vk --stage lighting --frames 3
        dotnet run --project samples/LuxelTriangle -- vk --stage lighting --size 801x603
        # Windowsのみ
        dotnet run --project samples/LuxelTriangle -- dx --stage lighting --frames 3
        ```

        `--frames 3`は3回のrender、submit完了待ち、presentを行って終了コード0で終了します。通常起動では回転するtexture付きcubeが表示され、resizeしてもaspectとdepthが保たれます。最小化中は描画を休止し、復元後に新しいsizeで再開します。

        ## 1 frameの流れ

        ```text
        Pump events
          → resize/minimize判定
          → per-frame値を更新
          → commandを記録してFinish
          → queueへSubmit
          → GPU完了を確認
          → CPU可視framebufferをPresent
          → 次のframe
        ```

        tutorialの現在の実装は `device.MainQueue.SubmitAndWait(command)` を使います。これは内部的にsubmitしてからqueue全体のidleを待つため、理解しやすく、commandや一時resourceを待機直後に安全に破棄できます。一方、CPUはGPU完了まで毎frame停止し、次frameのcommand記録を重ねられません。動作確認には適していますが、本番向けthroughputの設計ではありません。

        公開されているqueue操作は `Submit`、`SubmitAndWait`、`WaitIdle` です。**現在の公開APIにはper-frame fence、完了値、frame tokenがありません。** したがって、このsampleが複数frameを同時実行している、または`Submit`だけでresource再利用を安全に判定できる、とは説明できません。

        ## Fenceと2〜3 frames-in-flightの本番設計

        一般的なrendererは2または3個のframe slotをringとして持ちます。各slotには、そのframeのcommand allocator/buffer、upload領域、readback/present用buffer、動的descriptorや一時resource、そしてGPU完了を示すfence値をまとめます。

        ```text
        slot = frameNumber % slotCount
        slotの前回fence完了を待つ
        slot専用resourceをreset / 更新
        commandを記録してsubmit
        このsubmitのfence値をslotへ保存
        present
        ```

        重要なのは「毎frame待つ」のではなく、**再利用しようとするslotの前回GPU処理だけを待つ**ことです。CPUがframe N+1を準備している間にGPUがframe Nを処理でき、2〜3 frame分の揺らぎを吸収できます。slot数を無制限に増やすとlatencyとmemory使用量が増えるため、通常はswap/present側のbufferingと合わせます。

        > [!IMPORTANT]
        > 上のringは将来の公開fence/frame-token統合を示すarchitectureです。現在の`LuxelTriangle`へそのまま貼れるAPI例ではありません。現行sampleは意図的に`SubmitAndWait`で1 frameずつ完了させます。

        ## 所有権と寿命

        | 対象 | 安全に再利用・破棄できる時点 |
        | --- | --- |
        | command buffer / root argument bytes | そのsubmitのGPU完了後 |
        | upload、dynamic buffer、transient resource | それを読む最後のGPU処理完了後 |
        | color/depth target、readback framebuffer | 最後に参照したframe完了後 |
        | pipeline、texture、sampler、mesh buffer | それを参照する全submit完了後 |
        | window surface | queue idle後、またはsurfaceを使う全frame完了後 |

        `using`のscopeを抜けたことはGPU完了を意味しません。`Submit`は投入だけなので、その直後にcommandが参照するresourceをdisposeしてはいけません。現行sampleでは`SubmitAndWait`または明示的な`WaitIdle`がこの境界です。本番slot方式ではslotのfence完了が境界になります。

        ## Present、VSync、frame pacing

        `GpuSurface.Present(framebuffer, stridePixels, width, height)`はGPUのrender commandではなく、完成したCPU可視framebufferをwindowへ提示する段階です。present前にcopy/computeが完了している必要があるため、sampleは`SubmitAndWait`後に呼びます。D3D12のRGBA8 readback row pitchに合わせ、`stridePixels`は64 pixel単位へ揃えますが、visibleな`width` / `height`は実寸を渡します。

        VSyncはdisplay refreshへ提示を合わせてtearingを抑える一方、present待ちがframe pacingへ影響します。VSyncを切る場合も無制限loopにせず、target frame time、timer、present結果を使ってCPUの生成速度を制御します。simulationのdelta timeは「GPU待機時間そのもの」ではなく、上限を設けた実測値または固定stepを使うと、window移動や一時停止後の大ジャンプを避けられます。

        VulkanとD3D12では内部のswapchain、present mode、fence実装が異なりますが、アプリ側の原則は同じです。**使用中resourceを再利用しない、present対象の処理完了を保証する、CPUを無制限に先行させない**、の3点をbackend共通のframe policyにします。

        ## Resize、最小化、終了

        1. resize callbackでは新しいwidth/heightを保存し、`resizePending`だけを立てる。
        2. event loopの安全な位置で`MainQueue.WaitIdle()`し、古いsurface依存resourceを使う処理を完了させる。
        3. widthまたはheightが0ならtargetを作らず、短くsleepしてevent処理だけ続ける。
        4. 復元または正のsizeへのresizeでsurface、color/depth、framebufferを同じvisible sizeから再生成する。
        5. 終了時は新規submitを止め、queue idleを待ち、GPU resource、surface、device、window systemの順に寿命を閉じる。

        将来frame slotを導入した場合、resizeでは全slotのfenceを待ってからsurface依存resourceを作り直します。古いsizeのslotと新しいsizeのslotを混ぜません。shutdownでも同じく、未完了slotを待たずにdeviceを破棄してはいけません。

        ## 典型的な失敗

        - 毎frameのCPU使用率が高い → minimize中の0×0 loopやVSync off時の無制限loopを確認
        - `Submit`へ変えたら時々壊れる → GPU完了前にcommand/resource/framebufferを再利用またはdisposeしている
        - 2〜3 slotにしたのに毎frame止まる → current slotではなくqueue全体を`WaitIdle`している
        - latencyが増え続ける → CPU先行数をslot数で制限していない
        - resize直後に例外・破損 → old frame完了前にsurface依存resourceを破棄した、またはcolor/depth/framebufferのsizeが不一致
        - 復元後に真っ黒 → 0×0時にresourceを作った、`resizePending`を消し過ぎた、presentを再開していない
        - 終了時だけdevice lost / validation error → queue idle前にresourceやdeviceをdisposeしている
        """, toc: true);
    }

}
