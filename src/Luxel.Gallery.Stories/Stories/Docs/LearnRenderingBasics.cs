using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>初心者向けレンダリング学習経路。実行可能な正は samples/LuxelTriangle。</summary>
public static partial class DocsRenderingLearn
{
    [Story("Learn/Grapics/Overview", Order = 0)]
    public static Widget Overview(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # Grapics 学習ガイド

        {RenderingCourseCatalog.Meta("Learn/Grapics/Overview", "Beginner", "Standalone + Gallery", "Vulkan / DirectX 12", "なし")}

        この章は、Gallery のデモを見るだけでなく、自分のウィンドウへ描画できるところまでを順番に進みます。最小アプリの実装はリポジトリの `samples/LuxelTriangle/` が単一の正です。

        ## 推奨アプリ構築ルート

        Grapics直下のページはリンク順に並びます。基礎ルートの後に2Dを置き、その下のInternalで2D rasterizerの内部実装を扱います。

        {RenderingCourseCatalog.ApplicationRouteMarkdown()}

        **検索キーワード:** triangle / texture / shader / pipeline / barrier / submit / render graph

        > [!IMPORTANT]
        > `GpuView` とそのrender callbackはGallery内でデモを表示するためのハーネスです。通常アプリでは `WindowSystem`、`Window`、`GpuSurface` を使います。

        ## どのAPIまで学ぶか

        三角形、buffer ABI、texture付きquad、shader、pipeline stateを順に学び、BarrierとSubmit系methodによる同期を整理してからRenderGraphへ進みます。`--stage graph`でdirect描画を1 passへ移し、`--stage post`でtransient resourceとcompute post-processを追加します。indexed meshとcameraの実装はBuildのRecipeへ分けています。
        """, toc: true);
    }


    [Story("Learn/Grapics/Environment", Order = 1)]
    public static Widget Environment(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # グラフィック環境

        {{RenderingCourseCatalog.Meta("Learn/Grapics/Environment", "Beginner", "Standalone + Browser", "Vulkan / Direct3D 12 / WebGPU", "Overview")}}

        > [!NOTE]
        > `Luxel.Platform`と各platform実装はwindowの作成、event pump、clipboard、IME、低レベル入力をサポートします。通常のFrameworkアプリでは`Luxel.UI.App`の構成にdeviceとsurfaceの自動管理を任せられます。以下はgraphics APIを直接組み立てる低水準向けの例です。

        低水準APIではwindow libraryとgraphics backendを利用者が接続します。例ではwindow system固有の型を持ち込まず、必要なnative handleやcallbackは`handle`などの変数へ事前に用意済みとします。

        ## Backend

        | Backend | 実行環境 | Surfaceの入力 | `LuxelTriangle`の実行引数 |
        | --- | --- | --- | --- |
        | Vulkan | Windows | Win32 `HWND` | `vk` / `vulkan` |
        | Vulkan | Linux / X11 | instance extensionと`VkSurfaceKHR`作成callback | `vk` / `vulkan` |
        | Direct3D 12 | Windows | Win32 `HWND` | `dx` / `d3d12` |
        | WebGPU (native) | Windows | `HINSTANCE`と`HWND` | `webgpu` / `wgpu` |
        | WebGPU (native) | Linux / X11 | Xlib displayとwindow | `webgpu` / `wgpu` |
        | WebGPU (browser) | Browser WASM | canvas selector | — |

        Linuxの実ウィンドウにはX11の`DISPLAY`が必要です。Wayland nativeは未対応です。surfaceは`GpuDevice`ではなく、作成に使用した具体的なbackendから作ります。

        ## Vulkan

        ### Windows

        Win32では`VulkanPresentationMode.Win32`でdeviceを初期化し、事前に取得した`HWND`を`CreateWin32Surface`へ渡します。

        ```csharp
        nint handle = /* HWND */;
        uint width = 1280, height = 720;

        VulkanBackend backend = VulkanBackend.Create(new VulkanBackendOptions
        {
            Presentation = VulkanPresentationMode.Win32,
        });
        using var device = new GpuDevice(backend);
        using GpuSurface surface = backend.CreateWin32Surface(
            handle, width, height);
        ```

        ### Linux / X11

        Vulkan instanceの作成前に、window libraryが要求するinstance extensionと、`VkInstance`から`VkSurfaceKHR`を作るcallbackが必要です。`handle`はX11 windowなど、callbackがsurface作成に必要とする値です。

        ```csharp
        ulong handle = /* X11 window */;
        IReadOnlyList<string> requiredInstanceExtensions = /* prepared */;

        var presentationSource = new VulkanPresentationSource(
            requiredInstanceExtensions,
            instanceHandle => CreateVulkanSurface(instanceHandle, handle));

        VulkanBackend backend = VulkanBackend.Create(new VulkanBackendOptions
        {
            Presentation = VulkanPresentationMode.Window,
            PresentationSource = presentationSource,
        });
        using var device = new GpuDevice(backend);
        using GpuSurface surface = backend.CreateSurface(width, height);
        ```

        `CreateVulkanSurface`は選択したwindow libraryを使ってsurfaceを作り、`VkSurfaceKHR`を`ulong`で返す利用者側の関数です。Linux Vulkanではwindow handleだけでは初期化できません。

        ## Direct3D 12

        Direct3D 12はWindows専用です。`Create()`の`enableDebug`は既定で`true`です。

        ```csharp
        nint handle = /* HWND */;

        D3D12Backend backend = D3D12Backend.Create();
        using var device = new GpuDevice(backend);
        using GpuSurface surface = backend.CreateSurface(
            handle, width, height);
        ```

        ## WebGPU (native)

        deviceの作成コードはWindowsとLinuxで共通ですが、surface作成APIはnative window systemごとに異なります。

        ```csharp
        WebGpuBackend backend = WebGpuBackend.Create();
        using var device = new GpuDevice(backend);
        ```

        ### Windows

        ```csharp
        nint instanceHandle = /* HINSTANCE */;
        nint handle = /* HWND */;

        using GpuSurface surface = backend.CreateWin32Surface(
            instanceHandle, handle, width, height);
        ```

        ### Linux / X11

        ```csharp
        nint displayHandle = /* Xlib Display* */;
        ulong handle = /* Xlib Window */;

        using GpuSurface surface = backend.CreateXlibSurface(
            displayHandle, handle, width, height);
        ```

        ## WebGPU (browser)

        browser版のbackend作成は非同期です。native handleの代わりに描画先canvasのselectorを渡します。

        ```csharp
        string handle = /* canvas selector */;

        BrowserWebGpuBackend backend =
            await BrowserWebGpuBackend.CreateAsync();
        using var device = new GpuDevice(backend);
        using GpuSurface surface = backend.CreateCanvasSurface(
            handle, width, height);
        ```
        """, toc: true);
    }


    [Story("Learn/Grapics/ClearColor", Order = 2)]
    public static Widget ClearColor(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # ClearColor

        {{RenderingCourseCatalog.Meta("Learn/Grapics/ClearColor", "Beginner", "Standalone + Gallery", "Vulkan / Direct3D 12 / WebGPU", "Environment")}}

        {{StoryRef(ctx, CanonicalClearColorRecipe.Story)}}

        上のサンプルはGalleryではnative offscreen renderingとして動作し、静的GalleryとPagesではbrowser-WASM WebGPU runtimeとして同じClearColor recipeを実行します。

        ClearColorでは、render targetを指定した色でclearし、その結果をframebufferへコピーしてsurfaceへ表示します。Luxelにはcommand listとcommand bufferを分けたAPIはありません。`StartCommandRecording()`が一過性の`GpuCommandBuffer`を作成し、同時にコマンド記録を開始します。

        ## 描画先とframebufferを作成する

        GPU上の描画先として`GpuTexture`を作り、surfaceへ渡すRGBA8データの格納先として`GpuBuffer`を確保します。

        ```csharp
        uint width = 1280, height = 720;
        uint stridePixels = (width + 63) / 64 * 64;

        using GpuTexture target = device.CreateRenderTarget(
            width, height, GpuFormat.Rgba8Unorm);
        using GpuBuffer framebuffer = device.Malloc(
            checked((ulong)stridePixels * height * 4),
            GpuMemoryKind.HostMapped);
        ```

        D3D12のtexture copyではrow pitchが256 byte単位になるため、RGBA8の`stridePixels`を64 pixel単位へ揃えます。VulkanとWebGPUでも同じstrideを使用できます。

        ## コマンドバッファを作成する

        main queueから使い捨ての`GpuCommandBuffer`を作り、記録を開始します。`using`によりsubmit後にbackend固有のcommand resourceを破棄します。

        ```csharp
        using GpuCommandBuffer command =
            device.MainQueue.StartCommandRecording();
        ```

        ## コマンドを作成する

        `BeginRendering`はrender passを開始し、指定したRGBA値でtargetをclearします。描画終了後は、color outputの書き込みをcopyから読める状態へbarrierで遷移し、textureをframebufferへコピーします。

        ```csharp
        command.BeginRendering(target, null, 0.055f, 0.07f, 0.11f, 1)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(target, framebuffer, stridePixels);
        command.Finish();
        ```

        `Finish()`以降はcommandへ追加記録せず、queueへのsubmitに使用します。

        ## Submitする

        `SubmitAndWait`は記録済みcommandをmain queueへ投入し、GPU処理が完了するまで待つ入門用helperです。完了を待つため、直後にframebufferをPresentできます。

        ```csharp
        device.MainQueue.SubmitAndWait(command);
        ```

        非同期backendでは`SubmitAsync`も利用できます。本番の複数frame-in-flightでは、後続のFrame Loopページで扱う同期方式に置き換えます。

        ## SurfaceへPresentする

        `GpuSurface.Present`へ、コピー先framebuffer、1行のpixel数、実際の表示領域を渡します。surfaceは同じbackend instanceが作成したbufferだけを受け付けます。

        ```csharp
        surface.Present(framebuffer, stridePixels, width, height);
        ```

        `stridePixels`にはalignmentを含む行幅を渡しますが、`width`と`height`には実際に表示する領域を渡します。

        ## Framebufferのバッファリング

        このsampleの`framebuffer`はrender targetをCPUへ読み戻し、`GpuSurface.Present`へ渡す`GpuBuffer`です。現在のtutorialは`SubmitAndWait`後にpresentするため、1個のframebufferを安全に再利用できます。CPUとGPUを非同期に進める場合は、使用中のbufferを上書きしないよう2〜3個をringにします。

        ```text
        slot 0: GPU copy中 / present待ち
        slot 1: CPUが次frame用に予約
        slot 2: 前回利用したqueue処理の完了後に再利用可能
        ```

        各slotにはframebufferと、それを最後に使ったqueue処理の完了状態を対応付けます。slotを再利用する直前に対応する処理の完了を待ち、copy先として記録し、GPU完了後にpresentします。これはGPU resource再利用のためのbufferingであり、window backend内部のswapchain bufferingとは別の層です。

        D3D12のRGBA8 readbackでは各rowを256 byteへ揃えるため、slotごとに同じ`stridePixels`と必要byte数を確保します。`stridePixels`は64 pixel単位のaligned rowですが、`Present`へ渡す`width` / `height`はvisible sizeです。resizeでは使用中slotの完了を待ち、全framebufferを新しいsizeでまとめて作り直します。

        tutorialはsingle framebuffer + `SubmitAndWait`を正とします。Barrierとqueue完了の使い分けは[同期](story:Learn/Grapics/Synchronization)で説明します。backend内部の完了機構は[GPU同期の内部実装](story:Internals/Gpu/Synchronization)で扱います。

        ## Resize

        resize callbackでは即座にGPU resourceを破棄せず、次のevent-loop iterationでqueueをidleにしてからsurface、render target、framebufferを作り直します。最小化中の0×0では描画を休止します。
        """, toc: true);
    }


    [Story("Learn/Grapics/FirstTriangle", Order = 3, SampleBundle = "rendering.triangle")]
    public static Widget FirstTriangle(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # 三角形表示

        {{RenderingCourseCatalog.Meta("Learn/Grapics/FirstTriangle", "Beginner", "Standalone + Gallery", "Vulkan / DirectX 12", "ClearColor")}}

        {{StoryRef(ctx, "Examples/3D/Triangle")}}

        このページでは`GpuDevice`、`GpuBuffer`、`GpuShaderCode`、`GpuPipeline`、`GpuCommandBuffer`を使って三角形を描画します。完全なstandalone実装は`samples/LuxelTriangle/TriangleRenderer.cs`と`shaders/tutorial_triangle.slang`で確認できます。

        ## 1. 頂点バッファの作成

        1頂点は位置の`float4`と色の`float4`で8個の`float`を使います。3頂点分の96 byteを`HostMapped`メモリへ確保します。`using`のscopeを抜けるとバッファは破棄されます。

        ```csharp
        const uint vertexCount = 3;
        const uint floatsPerVertex = 8;
        const ulong vertexBufferSize =
            vertexCount * floatsPerVertex * sizeof(float);

        using GpuBuffer vertexBuffer = device.Malloc(
            vertexBufferSize, GpuMemoryKind.HostMapped);
        ```

        `GpuDevice.Malloc`が返す`GpuBuffer`には、シェーダーからraw bufferとして参照するための`BindlessIndex`も割り当てられます。

        ## 2. 頂点データの作成と転送

        各頂点を`position(x, y, z, w)`、`color(r, g, b, a)`の順に並べます。`HostMapped`バッファなので、`Span<float>`を取得してCPUから直接転送できます。

        ```csharp
        float[] vertexData =
        [
             0.00f, -0.72f, 0, 1,  1.00f, 0.18f, 0.18f, 1,
             0.72f,  0.62f, 0, 1,  0.18f, 1.00f, 0.28f, 1,
            -0.72f,  0.62f, 0, 1,  0.20f, 0.42f, 1.00f, 1,
        ];

        vertexData.CopyTo(vertexBuffer.Span<float>(vertexData.Length));
        ```

        C#側とシェーダー側で、1頂点のstrideが`8 * sizeof(float) = 32 byte`になるようにデータ配置を一致させます。

        ## 3. シェーダーの作成

        頂点シェーダーは`SV_VertexID`から頂点番号を受け取り、root argumentsで渡された`BindlessIndex`を使って頂点バッファを読みます。専用のvertex-input layoutは不要です。

        ```slang
        [[vk::binding(0, 0)]]
        RWByteAddressBuffer g_buffers[];

        struct DrawArgs { uint vertexBufferIndex; };
        [[vk::push_constant]] DrawArgs g_args;

        struct Vertex { float4 position; float4 color; };
        struct VertexOut
        {
            float4 position : SV_Position;
            float4 color : COLOR0;
        };

        [shader("vertex")]
        VertexOut vsMain(uint vertexId : SV_VertexID)
        {
            Vertex vertex = g_buffers[g_args.vertexBufferIndex]
                .Load<Vertex>(vertexId * 32);
            VertexOut output;
            output.position = vertex.position;
            output.color = vertex.color;
            return output;
        }

        [shader("pixel")]
        float4 psMain(VertexOut input) : SV_Target
        {
            return input.color;
        }
        ```

        buildで生成されたSPIR-VまたはDXILを`GpuShaderCode.Load`で読み込みます。`GpuShaderCode`は実行中のbackendに対応するshader blobを保持します。

        ```csharp
        GpuShaderCode shader = GpuShaderCode.Load("tutorial_triangle");
        ```

        ## 4. パイプラインの作成

        render targetのformatに合わせたraster設定を作り、頂点シェーダーとピクセルシェーダーをgraphics pipelineへまとめます。既定のentry pointは`vsMain`と`psMain`です。

        ```csharp
        GpuRasterDesc raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        using GpuPipeline pipeline =
            device.CreateGraphicsPipeline(shader, raster);
        ```

        ## 5. コマンドの設定

        main queueからcommand bufferを作り、render pass、pipeline、頂点バッファの`BindlessIndex`、頂点数の順に設定します。描画後はrender targetをframebufferへコピーします。

        ```csharp
        using GpuCommandBuffer command =
            device.MainQueue.StartCommandRecording();

        command.BeginRendering(target, null, 0.055f, 0.07f, 0.11f, 1)
            .SetGraphicsPipeline(pipeline)
            .SetRootArguments(vertexBuffer.BindlessIndex)
            .Draw(vertexCount)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(target, framebuffer, stridePixels);

        command.Finish();
        device.MainQueue.SubmitAndWait(command);
        surface.Present(framebuffer, stridePixels, width, height);
        ```

        処理全体のdata flowは`float[] → HostMapped GpuBuffer → BindlessIndex → root arguments → SV_VertexID`です。

        > [!NOTE]
        > 入門例では処理順を明確にするため`SubmitAndWait`を使います。BarrierとSubmit系methodの使い分けは後続の同期ページで扱います。

        ## 典型的な失敗

        - clear colorだけ見える → pipeline、shader名、root arguments、`Draw(3)`を確認
        - 三角形が崩れる → C# / Slangのstrideとfield順を確認
        - resize後だけ壊れる → queue idle後にtarget/framebufferを再生成したか確認
        """, toc: true);
    }


    [Story("Learn/Grapics/Buffers", Order = 4)]
    public static Widget Buffers(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # Buffers

        {{RenderingCourseCatalog.Meta("Learn/Grapics/Buffers", "Beginner", "Standalone + Gallery", "Vulkan / DirectX 12", "FirstTriangle")}}

        {{StoryRef(ctx, "Examples/3D/BuffersAndBindings")}}

        上のsampleは4頂点の四角形を、**頂点座標・インデックス・色の3つのbuffer**に分けて描画します。6個のindexを`Draw(6)`の`SV_VertexID`で順に読み、indexが指す頂点座標と色を別bufferから取得します。

        ## 四角形sampleのbufferを作成する

        頂点bufferには4個の`float2`、index bufferには2三角形分の6個の`uint`、色bufferには4個の`float4`を格納します。各bufferを`HostMapped`で確保し、対応する`Span<T>`へ転送します。

        ```csharp
        float[] vertices =
        [
            -0.72f, -0.72f,
             0.72f, -0.72f,
             0.72f,  0.72f,
            -0.72f,  0.72f,
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        float[] colors =
        [
            1.00f, 0.18f, 0.18f, 1,
            0.18f, 1.00f, 0.28f, 1,
            0.20f, 0.42f, 1.00f, 1,
            1.00f, 0.82f, 0.18f, 1,
        ];

        using GpuBuffer vertexBuffer = device.Malloc(
            checked((ulong)vertices.Length * sizeof(float)),
            GpuMemoryKind.HostMapped);
        using GpuBuffer indexBuffer = device.Malloc(
            checked((ulong)indices.Length * sizeof(uint)),
            GpuMemoryKind.HostMapped);
        using GpuBuffer colorBuffer = device.Malloc(
            checked((ulong)colors.Length * sizeof(float)),
            GpuMemoryKind.HostMapped);

        vertices.CopyTo(vertexBuffer.Span<float>(vertices.Length));
        indices.CopyTo(indexBuffer.Span<uint>(indices.Length));
        colors.CopyTo(colorBuffer.Span<float>(colors.Length));
        ```

        ## Luxelでは「vertex buffer」もraw bindless buffer

        `GpuDevice.Malloc` が作る `GpuBuffer` は、作成時に `BindlessIndex` を得てraw storage buffer配列へ登録されます。vertex / index / storage / upload / readbackは別クラスではなく、**同じバッファをどう使うかという役割**です。

        | 役割 | 中身とアクセス | 推奨memory kind |
        | --- | --- | --- |
        | vertex | 頂点shaderが `SV_VertexID` からbyte offsetを計算してpull | 小さい更新データは`HostMapped`、大きい静的データは`DeviceLocal` |
        | index | index値をraw bufferからpullし、その値でvertexをpull。Luxel coreに`DrawIndexed`はない | 通常`DeviceLocal`、作成時にupload |
        | storage | compute / graphicsが任意offsetを読み書き | 通常`DeviceLocal` |
        | upload | CPUが書き、GPUが読む一時または頻繁更新データ | `HostMapped` |
        | readback | GPU copy後にCPUが読む | `HostCached` |

        `HostMapped` はCPU write向けで、write-combined / uncachedの場合があります。CPU readbackには使わず、`CopyBuffer`や`CopyTextureToBuffer`で `HostCached` へコピーします。`DeviceLocal` はCPUの `Span<T>` を持たずGPU処理向けです。

        ## C# とSlangのABI

        root argumentsには3つのbufferの`BindlessIndex`だけを入れます。C#とSlangでfield順を一致させると、12 byteの同じraw bytesとして解釈されます。

        ```csharp
        public struct DrawArgs
        {
            public uint VertexBufferIndex;
            public uint IndexBufferIndex;
            public uint ColorBufferIndex;
        }

        var args = new DrawArgs
        {
            VertexBufferIndex = vertexBuffer.BindlessIndex,
            IndexBufferIndex = indexBuffer.BindlessIndex,
            ColorBufferIndex = colorBuffer.BindlessIndex,
        };
        ```

        ```slang
        struct DrawArgs
        {
            uint vertexBufferIndex;
            uint indexBufferIndex;
            uint colorBufferIndex;
        };
        [[vk::push_constant]] DrawArgs g_args;
        [[vk::binding(0, 0)]] RWByteAddressBuffer g_buffers[];

        struct VertexOut
        {
            float4 position : SV_Position;
            float4 color : COLOR0;
        };

        [shader("vertex")]
        VertexOut vsMain(uint vertexId : SV_VertexID)
        {
            uint index = g_buffers[g_args.indexBufferIndex]
                .Load<uint>(vertexId * 4);
            float2 position = asfloat(
                g_buffers[g_args.vertexBufferIndex].Load2(index * 8));
            float4 color = asfloat(
                g_buffers[g_args.colorBufferIndex].Load4(index * 16));

            VertexOut output;
            output.position = float4(position, 0, 1);
            output.color = color;
            return output;
        }
        ```

        `vertexId`は0〜5のindex-stream上の位置です。index bufferから得た0〜3の値を使い、頂点bufferは1要素8 byte、色bufferは1要素16 byteとしてbyte offsetを計算します。異なる役割のデータを別bufferへ分けても、同じindexで対応する頂点と色を取得できます。

        ## Root argumentsはraw bytes

        `SetRootArguments<T>` はunmanaged structをそのままbytes化し、Vulkanではpush constants、D3D12ではroot 32-bit constantsへ送ります。共通固定容量は **192 byte**、D3D12互換のためsizeは **4 byte単位**です。参照そのものではなく `BindlessIndex` や小さなscalar / matrixを入れます。大きな配列はbufferへ置いてindexだけを渡します。

        ```mermaid
        flowchart TB
        cpu["C#: DrawArgs raw bytes\nvertex / index / color buffer indices"] --> api["SetRootArguments\n4-byte units, max 192 bytes"]
        api --> vk["Vulkan\npush constants"]
        api --> dx["D3D12\nroot 32-bit constants"]
        vk --> slang["Slang: g_args"]
        dx --> slang
        index["index buffer\nSV_VertexID -> vertex index"] --> vertex["vertex buffer\nindex -> float2 position"]
        index --> color["color buffer\nindex -> float4 color"]
        slang --> index
        ```

        ## コマンドを記録する

        `DrawIndexed`の代わりにindex数の6を`Draw`へ渡します。shader内で`SV_VertexID`をindex bufferのoffsetとして使います。

        ```csharp
        command.BeginRendering(target, null, 0.055f, 0.07f, 0.11f, 1)
            .SetGraphicsPipeline(pipeline)
            .SetRootArguments(args)
            .Draw(6)
            .EndRendering();
        ```

        ## 所有権と寿命

        sampleはvertex、index、color bufferを描画中保持し、破棄時はqueueの完了を待ってから3つのbufferを破棄します。command bufferは1 frameだけです。bindless indexはbufferが生存中だけ有効なので、記録済みcommandが参照している間にbufferを破棄・再利用してはいけません。

        ## 典型的な失敗

        - `ArgumentException: 4-byte-compatible size` → root argsへ3 byteなどDWord非互換の型を渡した
        - `ArgumentOutOfRangeException: limited to 192 bytes` → 大きなデータをbufferへ移し、indexだけをroot argsへ置く
        - 三角形が崩れる → C# / Slangのsize、offset、stride、paddingを照合する
        - GPU validation / device lost → 寿命切れのbindless index、copy/barrier前後、範囲外offsetを確認する
        - CPU readbackが極端に遅い → `HostMapped`を直接読まず`HostCached`へGPU copyする
        """, toc: true);
    }


    [Story("Learn/Grapics/Textures", Order = 5)]
    public static Widget TexturesBasics(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # Textures

        {{RenderingCourseCatalog.Meta("Learn/Grapics/Textures", "Beginner", "Standalone + Gallery", "Vulkan / DirectX 12", "Buffers")}}

        {{StoryRef(ctx, "Examples/3D/Textures")}}

        Textureは2次元のpixel配列をGPUへ置き、shaderからUV座標で参照するresourceです。`GpuTexture`が画像本体、`GpuSampler`が補間方法と範囲外UVの扱いを持ちます。

        ## Textureを作成する

        `GpuDevice.CreateTexture`へwidth、height、RGBA8のpixel dataを渡して、サンプリング可能な`GpuTexture`を直接作成します。dataは左上から右へ並ぶtightなrowを、上から下へ続けます。

        ```csharp
        const uint textureWidth = 8;
        const uint textureHeight = 8;
        byte[] pixels = CreateCheckerboard(textureWidth, textureHeight);

        using GpuTexture texture = device.CreateTexture(
            textureWidth,
            textureHeight,
            pixels,
            GpuFormat.Rgba8Unorm);
        ```

        上のsampleではチェック柄を作る処理を`CreateCheckerboard`へ分けています。8×8のRGBA8なので、pixel dataは`8 * 8 * 4 = 256 byte`です。dataの長さは`width * height * 4`と正確に一致させます。uploadは同期的なので、`CreateTexture`から戻った後は元の`pixels`を再利用できます。生成された`GpuTexture`はrendererが所有し、それを参照するGPU commandが完了してから破棄します。

        ## Samplerを作成する

        samplerはpixel間の補間と、0〜1の外側に出たUVの扱いを指定します。`GpuDevice.CreateSampler`で直接作成します。

        ```csharp
        using GpuSampler sampler = device.CreateSampler(
            GpuSamplerFilter.Point,
            GpuSamplerAddress.Repeat);
        ```

        `Point`は最も近い1 pixelを選び、`Linear`は周囲を補間します。`Clamp`は端のpixelを延長し、`Repeat`はUVを繰り返します。samplerもrendererが所有し、参照中のGPU commandが完了するまで生存させます。

        ## Shaderへ渡す

        bufferと同様に、textureとsamplerの`BindlessIndex`をroot argumentsへ入れます。

        ```csharp
        public struct DrawArgs
        {
            public uint TextureIndex;
            public uint SamplerIndex;
        }

        var args = new DrawArgs
        {
            TextureIndex = texture.BindlessIndex,
            SamplerIndex = sampler.BindlessIndex,
        };
        ```

        Slang側では同じindexを使ってtextureとsamplerを選択し、pixel shaderでUVをsampleします。

        ```slang
        [[vk::binding(1, 0)]] Texture2D g_textures[];
        [[vk::binding(2, 0)]] SamplerState g_samplers[];

        float4 color = g_textures[g_args.textureIndex]
            .Sample(g_samplers[g_args.samplerIndex], input.uv);
        ```

        ## コマンドへ設定する

        textureとsamplerはroot argumentsを通じて参照されるため、command側ではpipelineとargsを設定して描画します。

        ```csharp
        command.BeginRendering(target, null, 0, 0, 0, 1)
            .SetGraphicsPipeline(pipeline)
            .SetRootArguments(args)
            .Draw(6)
            .EndRendering();
        ```

        ## Texture付きquadで確認する

        実行可能な正は`samples/LuxelTriangle/TriangleRenderer.cs`、ABIは`samples/LuxelTriangle/TutorialAbi.cs`、shaderは`shaders/tutorial_3d.slang`です。4頂点・6 indexのquadへ4×4の橙/紫checker textureを貼り、upload、UV、sampler、bindless indexをまとめて確認できます。

        ```powershell
        dotnet build samples/LuxelTriangle/LuxelTriangle.csproj
        dotnet test tests/Luxel.Tests/Luxel.Tests.csproj --filter TutorialAbiTests
        dotnet run --project samples/LuxelTriangle -- vk --stage texture
        dotnet run --project samples/LuxelTriangle -- vk --stage texture --frames 3
        # Windowsのみ
        dotnet run --project samples/LuxelTriangle -- dx --stage texture --frames 3
        ```

        成功すると暗い背景の中央にchecker付きquadが表示されます。UV向きを検証するときは四隅を非対称な色にすると、上下・左右の反転を発見しやすくなります。

        ## Pixel、色空間、alpha

        `CreateTexture`へ渡すdataは、**左上から右へ進むtightなRGBA8 row**を上から下へ並べます。公開APIのformatには現在sRGB variantがないため、sRGB authored imageを`Rgba8Unorm`として読む場合はshaderで明示的にlinearへdecodeします。hardware decodeが追加された将来にshader decodeと重ねず、decodeを0回か1回に固定してください。

        `GpuBlendMode.None`ではalphaは出力値に残るだけで背景とは混ざりません。透過には`GpuBlendMode.AlphaBlend`を使い、RGBをalphaで事前乗算しないstraight alphaへ統一します。opaque textureではalphaを1にすると、色空間とblendの問題を分けて確認できます。

        ## UV原点とindexed quad

        tutorialの規約はCPU imageの(0,0)とUV(0,0)を左上、`u`は右、`v`は下です。CPU upload、asset loader、shaderの複数箇所でVを反転しないでください。

        quadは6頂点を複製せず、4頂点をindex列`0,1,2, 0,2,3`で再利用します。shaderが`SV_VertexID`からraw index bufferを読み、そのindexでposition、color、UVを含むvertexをpullします。C#とSlangでindex width、vertex stride、UV offsetを一致させます。

        ```text
        CPU RGBA bytes → CreateTexture → bindless texture index ┐
        CreateSampler  → bindless sampler index ────────────────┼→ tutorial_3d.slang → sampled color
        Vertex UV + index pulling ───────────────────────────────┘
        ```

        ## Upload rowとbackend差

        呼び出し側のupload dataは常に`width * 4` byteのtight rowで、backend用paddingを含めません。Vulkan backendはstaging copyへ渡し、D3D12 backendは内部で各rowを256 byte境界のfootprintへ詰め直します。readback framebufferの`StridePixels`を64 pixelへ揃える規則と、texture uploadの入力規則を混同しないでください。

        現在の`CreateTexture` uploadは同期的です。methodが戻った後は入力配列を再利用できます。一方、生成したtexture、sampler、その`BindlessIndex`は記録済みcommandが完了するまで生存させます。

        ## Backend差を切り分ける

        Slang source、RGBA channel順、UV規約、bindless indexはbackend共通です。VulkanはSPIR-Vとdescriptor array、D3D12はDXILとdescriptor heapを使います。linear filteringやfloat丸めによる数LSBの差はあり得ますが、上下反転、R/B交換、1 rowずれは許容差ではなくbugです。

        典型的な問題は、RGBA/BGRAの取り違え、UVの上下反転、textureとsamplerのindex入れ替え、upload rowのpadding混入、描画完了前のresource破棄です。
        """, toc: true);
    }


    [Story("Learn/Grapics/Shaders", Order = 6)]
    public static Widget Shaders(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # Shaders

        {{RenderingCourseCatalog.Meta("Learn/Grapics/Shaders", "Beginner", "Gallery + Standalone", "Vulkan / DirectX 12 / WebGPU", "Textures")}}

        shaderはGPU上で各頂点、pixel、またはdispatchされたthreadを処理するprogramです。この章では、Slangでshaderを書き、Luxelからresourceと値を渡し、実行時またはbuild前にcompileする流れを説明します。

        ## Slangとは

        [Slang公式サイト](https://shader-slang.org/)で公開されている、HLSLに近い構文のshader languageとcompilerです。1つの`.slang` sourceから、Luxelのbackendに応じてVulkan向けSPIR-V、DirectX 12向けDXIL、WebGPU向けWGSLを作れます。

        Slang側の型とC#側の型は別々にcompileされます。両者でfieldの順序、型、paddingを一致させ、GPUへ渡すbytesのlayoutを同じにする必要があります。

        ## シェーダーの種類と作り方

        Luxelで最初に使うのはgraphics shaderとcompute shaderです。

        - **vertex shader**: 頂点ごとに実行され、clip spaceの位置とpixel shaderへ渡す値を作ります。
        - **pixel shader**: rasterizeされたpixelごとに実行され、render targetへ書く色を返します。
        - **compute shader**: 描画pipelineとは独立したthread groupとして実行され、bufferやtextureを読み書きします。

        graphics shaderはvertexとpixelの2つのentry pointを同じsourceへ定義します。

        ```slang
        struct VSOut
        {
            float4 position : SV_Position;
            float4 color : COLOR0;
        };

        [shader("vertex")]
        VSOut vsMain(uint vertexId : SV_VertexID)
        {
            VSOut output;
            output.position = float4(0, 0, 0, 1);
            output.color = float4(1, 0.5, 0.1, 1);
            return output;
        }

        [shader("pixel")]
        float4 psMain(VSOut input) : SV_Target
        {
            return input.color;
        }
        ```

        compute shaderは`main`をentry pointにし、`numthreads`で1 groupのthread数を指定します。

        ```slang
        [shader("compute")]
        [numthreads(8, 8, 1)]
        void main(uint3 threadId : SV_DispatchThreadID)
        {
            // threadIdを使ってbufferまたはtextureを更新する
        }
        ```

        graphicsには`CreateGraphicsPipeline`、computeには`CreateComputePipeline`を使います。Luxelの既定entry point名はgraphicsが`vsMain` / `psMain`、computeが`main`です。

        ## メイン関数の入出力

        entry pointのparameterと戻り値にはsemanticを付け、GPU pipelineのどの値に対応するかを示します。

        | semantic | stage | 意味 |
        | --- | --- | --- |
        | `SV_VertexID` | vertex input | `Draw`が生成した頂点番号 |
        | `SV_Position` | vertex output | clip spaceの頂点位置。vertex shaderで必須 |
        | `COLOR0`, `TEXCOORD0` | vertex output / pixel input | stage間で補間される任意の値 |
        | `SV_Target` | pixel output | render targetへ書く色 |
        | `SV_DispatchThreadID` | compute input | dispatch全体で一意なthread座標 |

        `VSOut`のようなstructをvertex shaderの戻り値とpixel shaderのparameterで共有すると、同じsemanticを持つfieldが接続されます。位置は`float4`の`SV_Position`として返し、色やUVは`COLOR0`や`TEXCOORD0`で渡します。pixel shaderの`SV_Target`は通常`float4`のRGBA色です。

        ## bindingとroot argument

        buffer、texture、samplerはbindingへ配置し、shaderから参照します。Luxelの基本bindingはbufferが0、textureが1、samplerが2です。

        ```slang
        [[vk::binding(0, 0)]] RWByteAddressBuffer g_buffers[];
        [[vk::binding(1, 0)]] Texture2D g_textures[];
        [[vk::binding(2, 0)]] SamplerState g_samplers[];

        struct DrawArgs
        {
            uint vertexBufferIndex;
            uint textureIndex;
            uint samplerIndex;
        };
        [[vk::push_constant]] DrawArgs g_args;
        ```

        bindingの配列はbindless resource tableです。C#側で各resourceの`BindlessIndex`をroot argumentへ入れ、commandへ設定します。

        ```csharp
        var args = new DrawArgs
        {
            VertexBufferIndex = vertexBuffer.BindlessIndex,
            TextureIndex = texture.BindlessIndex,
            SamplerIndex = sampler.BindlessIndex,
        };

        command.SetRootArguments(args);
        ```

        `[[vk::push_constant]]`はVulkanだけに限定するための記述ではありません。Luxelが同じroot argument bytesを各backendの対応する仕組みへ渡します。Slangの`DrawArgs`とC#の`DrawArgs`はfield順とsizeを一致させ、参照中のresourceはGPU commandが完了するまで破棄しません。

        ## オンラインコンパイル

        Galleryのsampleでは、実行時にSlang sourceをcompileできます。`ResourceSystem`へ`SlangSource`を渡し、現在のbackend向け`GpuShaderCode`を非同期に作ります。

        ```csharp
        string slang = LoadShaderSource();

        ResourceHandle<GpuShaderCode> shader =
            resources.Create<SlangSource, GpuShaderCode>(
                "sample.slang",
                new SlangSource("sample.slang", slang),
                "graphics");

        ResourceHandle<GpuPipeline> pipeline = resources.CreateGraphicsPipeline(
            "sample.pipeline", shader,
            GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        ```

        selectorはgraphics shaderなら`"graphics"`、compute shaderなら`"compute"`です。compile中はresourceがLoadingになり、成功後にpipelineを使用できます。sourceの変更をすぐ試せるため、Gallery、editor、開発中のhot reloadに向いています。一方、起動時にcompilerとcompile時間が必要なので、配布物では次のoffline cacheを使えます。

        ## オフラインコンパイルとキャッシュ

        standalone applicationでは`.slang`を`shaders/`へ置き、build前に全backend向けartifactを生成してGit管理します。通常のbuild / publishはcompilerを起動せず、cacheの完全性とsource hashを検証して出力へcopyします。

        - `compute*.slang`または`raster2d_*.slang`: compute。entry pointは`main`。
        - それ以外の`.slang`: graphics。entry pointは`vsMain`と`psMain`。
        - 共通: `compiled/<name>.spv`と`compiled/<name>.wgsl`。
        - compute DXIL: `compiled/<name>.dxil`。
        - graphics DXIL: `compiled/<name>.vs.dxil`と`compiled/<name>.ps.dxil`。

        sourceを追加または変更したらrepository rootでcacheを再生成します。

        ```powershell
        dotnet msbuild shaders/Luxel.ShaderCache.proj -t:CompileLuxelShaderCache
        git status --short shaders
        ```

        `shaders/*.slang`、`shaders/compiled/*`、`shaders/compiled/inputs.sha256`を同じcommitへ含めます。`inputs.sha256`にはsource hashだけでなくcache schema、Slang/DXC version、target profileも記録されます。取得されたcompilerの`tools/`はlocal cacheなのでcommitしません。

        runtimeではbase nameを指定するだけで、現在のbackendに対応するartifactを読み込めます。

        ```csharp
        GpuShaderCode shader = GpuShaderCode.Load("my_effect");
        using GpuPipeline pipeline = device.CreateGraphicsPipeline(shader, raster);
        ```

        ## Publishする際の注意

        executable projectから`shaders/Luxel.Shaders.targets`をimportすると、compiled shader cacheがbuild/publish先の`shaders/`へcopyされます。publish時にSlang compilerを実行するのではなく、Git管理された検証済みartifactを同梱します。

        ```xml
        <Import Project="../../shaders/Luxel.Shaders.targets" />
        ```

        runtimeではcurrent working directoryではなく、executable基準の`AppContext.BaseDirectory/shaders`からshaderを解決します。IDEやrepository rootから起動したときだけ動く相対pathを使用しないでください。

        ```csharp
        string shaderDirectory = Path.Combine(
            AppContext.BaseDirectory, "shaders");
        ```

        `dotnet publish`後は、必要な`.spv`、`.vs.dxil`、`.ps.dxil`、compute用`.dxil`がpublish directoryへ存在することを確認します。publish directory自体へ`cd`するだけではcwd依存を発見できないため、空の別directoryをcurrent directoryにして、publishしたexecutableを絶対pathで起動するsmoke testを行います。

        ```powershell
        $publish = Join-Path $env:TEMP "luxel-publish"
        $cwd = Join-Path $env:TEMP "luxel-empty-cwd"
        dotnet publish samples/LuxelTriangle/LuxelTriangle.csproj `
          -c Release -o $publish

        Push-Location $cwd
        try {
          & (Join-Path $publish "LuxelTriangle.exe") vk --frames 1
          if ($LASTEXITCODE -ne 0) { throw "publish smoke failed" }
        } finally { Pop-Location }
        ```

        Linuxでは実行ファイル名に`.exe`を付けません。Windowsでは必要に応じてVulkanとDirectX 12の両方を起動し、cacheに各backend用artifactが含まれることを確認します。

        cacheが不足している、またはsourceと`inputs.sha256`が一致しない場合、通常buildは意図的に失敗します。`CompileLuxelShaderCache`を再実行し、生成物を更新してください。pipelineとshaderがGPU commandから参照されている間は破棄せず、sourceだけを変更した古いcacheを配布しないようにします。
        """, toc: true);
    }


    [Story("Learn/Grapics/PipelineState", Order = 7)]
    public static Widget PipelineState(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # Pipelineのその他の設定

        {{RenderingCourseCatalog.Meta("Learn/Grapics/PipelineState", "Beginner", "Gallery + Standalone", "Vulkan / DirectX 12 / WebGPU", "Shaders")}}

        shaderが「各頂点・pixelをどう計算するか」を決めるのに対し、graphics pipeline stateは「三角形をどう組み立て、どの面とpixelを残し、既存のrender targetへどう書き込むか」を決めます。Luxelでは固定pipeline stateを`GpuRasterDesc`へまとめ、`CreateGraphicsPipeline`時にshaderと組み合わせます。

        ```csharp
        GpuRasterDesc raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        raster.Topology = GpuPrimitiveTopology.TriangleList;
        raster.CullMode = GpuCullMode.Back;
        raster.FrontFace = GpuFrontFace.CounterClockwise;
        raster.DepthTest = true;
        raster.DepthWrite = true;
        raster.DepthFormat = GpuFormat.D32Float;
        raster.Blend = GpuBlendMode.None;

        using GpuPipeline pipeline =
            device.CreateGraphicsPipeline(shader, raster);
        ```

        `GpuRasterDesc.Default`はtriangle list、depth無効、blend無効、culling無効です。設定を変更したら別のpipelineを作ります。対してrender target、clear値、viewport / scissorはcommand記録時の状態です。

        | 分類 | Luxelでの指定場所 | 主な設定 |
        | --- | --- | --- |
        | Rasterizer State | `GpuRasterDesc` | topology、cull mode、front face |
        | Depth-Stencil State | `GpuRasterDesc` + `BeginRendering` | depth test/write/format、depth target、clear depth |
        | Blend State | `GpuRasterDesc` | overwriteまたはstraight alpha blend |
        | Viewport / Scissor | `BeginRendering`が自動設定 | 現在はrender target全体 |

        ## Rasterizer State

        rasterizerはvertex shaderが返したclip-space頂点からprimitiveを組み立て、三角形が覆うpixelを求めます。`Topology`は頂点列の解釈、`CullMode`と`FrontFace`は描画前に除外する面を決めます。

        ### Primitive Topology

        - `TriangleList`: 3頂点ごとに独立した三角形。`0,1,2`の次は`3,4,5`です。
        - `TriangleStrip`: 最初の3頂点で三角形を作り、以降は1頂点追加するたびに前の2頂点と新しい三角形を作ります。

        初学者向けsampleは、頂点数と三角形の対応が明確な`TriangleList`を使います。topologyはpipeline stateなので、同じshaderでもlistとstripを切り替える場合は対応するpipelineを用意します。

        ### Cull ModeとFront Face

        三角形を画面上から見た頂点の回り順でfront/backへ分類します。`FrontFace = CounterClockwise`なら反時計回りを表面とし、`CullMode = Back`なら裏面をrasterizeしません。

        ```csharp
        raster.CullMode = GpuCullMode.Back;
        raster.FrontFace = GpuFrontFace.CounterClockwise;
        ```

        `GpuCullMode.None` / `Front` / `Back`を選べます。何も表示されないときは一度`None`へ戻してください。projectionのY方向、negative scale、index順の違いでwindingが反転することがあります。LuxelのVulkan backendはviewportのYを内部で反転し、Direct3D 12と同じ画面座標の見え方へ揃えます。

        現在の公開`GpuRasterDesc`はfill描画のみで、wireframe、depth bias、line widthは公開していません。

        ## Depth-Stencil State

        depth testは各fragmentのdepthとdepth targetに保存済みの値を比較し、手前のfragmentだけをcolor targetへ通します。Luxelの通常depthは0..1で、比較は`LessOrEqual`、clear値は1です。

        ```csharp
        raster.DepthTest = true;
        raster.DepthWrite = true;
        raster.DepthFormat = GpuFormat.D32Float;

        using GpuTexture depth = device.CreateDepthTarget(width, height);

        command.BeginRendering(
            colorTarget, depth,
            r: 0.05f, g: 0.07f, b: 0.1f, a: 1,
            clearDepth: 1f);
        ```

        `DepthTest`だけでなく、pipelineの`DepthFormat`と実際に渡すdepth targetのformatを一致させます。resize時はcolor targetと同じvisible sizeでdepth targetも作り直します。

        - `DepthTest = false`, `DepthWrite = false`: 2D、背景、描画順で上書きする単純なpass。
        - `DepthTest = true`, `DepthWrite = true`: 通常のopaque 3D geometry。
        - `DepthTest = true`, `DepthWrite = false`: 透明物など、既存depthでは隠すがdepth targetは更新したくないpass。

        名前はDepth-Stencil Stateですが、現在のLuxel公開APIはdepthのみを公開しています。stencil test、stencil operation、read/write maskはまだ`GpuRasterDesc`から設定できません。

        {{StoryRef(ctx, "Examples/3D/Depth")}}

        ## Blend State

        blendはpixel shaderの出力`src`と、render targetに既にある色`dst`を合成します。

        - `GpuBlendMode.None`: `src`で`dst`を上書きします。opaque描画の既定値です。
        - `GpuBlendMode.AlphaBlend`: straight alphaとしてRGBを`src.a`と`1 - src.a`で合成します。

        ```text
        out.rgb = src.rgb * src.a + dst.rgb * (1 - src.a)
        out.a   = src.a             + dst.a   * (1 - src.a)
        ```

        ```csharp
        raster.Blend = GpuBlendMode.AlphaBlend;
        ```

        `AlphaBlend`へ渡すRGBはpremultiplied alphaではありません。透明物は通常、opaque passの後に奥から手前へsortし、depth testを有効、depth writeを無効にして描きます。blendを有効にするだけでは、複数の透明面の順序問題は解決しません。加算、乗算、個別blend factor、color write maskは現在の公開enumにはありません。

        {{StoryRef(ctx, "Examples/3D/Blend")}}

        ## Viewport / Scissor

        viewportはNDCをrender target上の座標とdepth範囲へ変換します。scissorはその後、fragmentを書き込める矩形を整数pixelで制限します。

        ```text
        clip position
          → perspective divide
          → NDC
          → viewport transform
          → rasterization
          → scissor test
          → depth / blend
          → color target
        ```

        一般的なgraphics APIではviewportとscissorはcommand bufferへ動的に設定し、split screen、letterbox、thumbnail、部分更新などに使います。**現在のLuxel公開APIには`SetViewport` / `SetScissor`はありません。** `BeginRendering(colorTarget, ...)`が両方をrender target全体へ自動設定します。

        ```csharp
        command.BeginRendering(colorTarget, depthTarget)
            .SetGraphicsPipeline(pipeline)
            .SetRootArguments(args)
            .Draw(vertexCount)
            .EndRendering();
        ```

        したがって現在は、pipelineだけを変えて部分領域へ描画することはできません。必要なら小さいintermediate render targetへ描いて後で合成するか、将来のdynamic viewport / scissor APIを追加します。resize後の次frameでは、新しいtarget sizeに合わせて`BeginRendering`が全領域を再設定します。

        ## Pipelineを分ける判断

        shaderが同じでも、次のようなpassは別pipelineにします。

        | pass | Depth | Blend | Cull |
        | --- | --- | --- | --- |
        | opaque 3D | test on / write on | None | Back |
        | transparent 3D | test on / write off | AlphaBlend | BackまたはNone |
        | 2D overlay | test off / write off | AlphaBlend | None |
        | full-screen post process | test off / write off | None | None |

        pipelineはdrawごとに作らず、初期化時またはresource systemで作成して再利用します。render target format、depth format、topology、culling、blendが異なる組み合わせをkeyにcacheすると、同じ状態のpipelineを共有できます。

        ## よくある症状

        - **何も出ない**: cullingを`None`へ戻し、winding、`ColorFormat`、shader entry pointを確認する。
        - **奥の面が手前に出る**: depth target、`DepthTest`、`DepthWrite`、clear depth 1、0..1 projectionを確認する。
        - **透明部分が黒い・縁が暗い**: straight alphaとpremultiplied alphaを混ぜていないか確認する。
        - **透明面の前後が逆**: transparent drawを奥から手前へsortし、depth writeを無効にする。
        - **resize後に欠ける**: color/depth targetを同じsizeで再作成し、次の`BeginRendering`で全viewport/scissorを設定する。
        """, toc: true);
    }


    [Story("Learn/Grapics/Synchronization", Order = 8)]
    public static Widget Synchronization(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # 同期

        {{RenderingCourseCatalog.Meta("Learn/Grapics/Synchronization", "Beginner+", "Standalone", "Vulkan / DirectX 12 / WebGPU", "Pipeline State")}}

        GPU commandは記録した順に並べるだけでは、前段の書き込みが後段の読み出しから正しく見えるとは限りません。また、`Submit`はGPU処理の完了を待ちません。このページでは、command内の実行・memory依存を表す`Barrier`と、commandをqueueへ投入してCPU側の完了境界を作るSubmit系methodを分けて説明します。

        ```text
        command内: producer → Barrier → consumer
        CPU / queue: Finish → Submit → GPU execution → completion wait
        ```

        ## Barrierは何を同期するか

        `GpuCommandBuffer.Barrier(source, destination, hazard)`は、前段stageのmemory accessを後段stageから見えるようにするstage barrierです。同じcommand buffer内で、computeの書き込みをpixel shaderから読む、color outputをcopyする、GPUが生成したindirect argumentsをdrawで読む、といった依存に使います。

        ```csharp
        command.Dispatch(groupCountX)
            .Barrier(GpuStage.ComputeShader, GpuStage.PixelShader)
            .BeginRendering(colorTarget)
            .SetGraphicsPipeline(graphicsPipeline)
            .Draw(vertexCount)
            .EndRendering();
        ```

        `source`は値を生成したstage、`destination`はその値を次に使うstageです。`GpuStage`は`[Flags]` enumなので、複数stageをbitwise ORでまとめられます。

        ## GpuStage一覧

        | 値 | 対象になる処理 | Barrierでの典型的な指定 |
        | --- | --- | --- |
        | `GpuStage.None` | stageを指定しない | stage依存がないことを明示する値。通常のproducer / consumer指定には使わない |
        | `GpuStage.DrawIndirect` | indirect draw / dispatch引数の読み出し | GPUが生成した引数を読むdestination。必要に応じて`GpuHazard.IndirectArguments`も指定する |
        | `GpuStage.VertexShader` | vertex shaderとvertex pullingのload | computeやcopyで用意したvertex dataを読むdestination |
        | `GpuStage.PixelShader` | pixel / fragment shader | computeやcopyで用意したtexture・bufferを読むdestination |
        | `GpuStage.ComputeShader` | compute shader | dispatchによる書き込みのsource、または前段の結果を読むdestination |
        | `GpuStage.ColorOutput` | color attachmentへの書き込み | render targetを書いたsource |
        | `GpuStage.DepthStencil` | depth / stencil testと書き込み | depth / stencil resourceへaccessするstage |
        | `GpuStage.Copy` | copy / transfer | copy元を準備した後のdestination、またはcopy結果を使う前のsource |
        | `GpuStage.AllGraphics` | すべてのgraphics stage | graphics側の複数stageをまとめて指定したい場合 |
        | `GpuStage.All` | すべてのcommand | 最も粗い指定。依存stageを切り分ける診断時に限定して使う |

        通常はproducerとconsumerをできるだけ正確に指定します。`AllGraphics`や`All`へ広げるほど意図は粗くなり、backendが不要な待機まで挿入する可能性があります。

        ## よく使うBarrier

        ### Render targetからcopyする

        ```csharp
        command.BeginRendering(target, null, 0, 0, 0, 1)
            .SetGraphicsPipeline(pipeline)
            .Draw(vertexCount)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(target, framebuffer, stridePixels);
        ```

        color attachmentへの書き込みを、後続のcopy sourceとして見えるようにします。texture layout/stateの具体的な遷移はbackendが処理します。

        ### Compute結果を後段shaderで読む

        ```csharp
        command.SetComputePipeline(computePipeline)
            .Dispatch(groupCountX)
            .Barrier(GpuStage.ComputeShader,
                     GpuStage.VertexShader | GpuStage.PixelShader)
            .SetGraphicsPipeline(graphicsPipeline)
            .Draw(vertexCount);
        ```

        producerとconsumerを正確に指定します。常に`All`へ広げると正しさは確認しやすい一方、backendが不要な待機まで挿入する可能性があります。

        ### Indirect argumentsを生成する

        ```csharp
        command.Dispatch(groupCountX)
            .Barrier(
                GpuStage.ComputeShader,
                GpuStage.DrawIndirect,
                GpuHazard.IndirectArguments);
        ```

        `GpuHazard.IndirectArguments`はGPUが書いたindirect draw/dispatch argumentを後段のcommand processorから読めるようにする追加指定です。`GpuHazard.Descriptors`はbindless descriptor更新に関する特殊hazardですが、通常の描画resource依存へ機械的に付けません。

        ## Barrierでは解決しないこと

        Barrierはcommand buffer内のGPU依存を表します。次の問題は解決しません。

        - CPUがGPU完了前にbufferやtextureを破棄・上書きすること
        - command bufferを`Finish`せずにsubmitすること
        - 別queue間の所有権移動や同期
        - application thread同士の排他制御
        - presentやreadbackのCPU側完了待ち

        CPUが結果を読む、resourceを再利用する、resizeで作り直す場合はqueueの完了境界も必要です。

        ## FinishとSubmit

        `Finish()`はcommand記録を終了します。GPUへ投入する操作ではありません。`Finish`後のcommandを`GpuQueue.Submit`へ渡します。

        ```csharp
        using GpuCommandBuffer command =
            device.MainQueue.StartCommandRecording();

        RecordCommands(command);
        command.Finish();
        device.MainQueue.Submit(command);
        ```

        `Submit`は待たずに戻ります。投入後もGPUがcommandや参照resourceを使用している可能性があるため、公開APIだけで個別submitの完了を追跡できない現在は、直後にそれらを破棄・再利用するコードへ変更しないでください。

        ## SubmitAndWait

        `SubmitAndWait(command)`はcommandをsubmitした後、main queueがidleになるまで待つ同期helperです。tutorial、GPU readback、one-shot処理など、CPUが直後に結果を必要とする場面に向いています。

        ```csharp
        command.Finish();
        device.MainQueue.SubmitAndWait(command);

        ReadOnlySpan<byte> pixels = framebuffer.Span<byte>(byteCount);
        ```

        実装上は`Submit`に続けて`WaitIdle`を呼ぶため、queue上の先行処理も完了します。毎frame使うとCPUとGPUが直列化されるので、簡潔さを優先するsample向けです。

        ## SubmitAsync

        `SubmitAsync(command, cancellationToken)`は非同期backendでGPU完了をawaitします。browser WebGPUのようにJavaScript Promiseを同期blockできない環境では、このmethodを使用します。

        ```csharp
        command.Finish();
        await device.MainQueue.SubmitAsync(command, cancellationToken);
        ```

        nativeの同期backendではsubmit後に同期的なidle waitを実行し、完了済み`ValueTask`を返します。そのため、`SubmitAsync`を呼ぶだけでnative rendererのCPU/GPU overlapが自動的に増えるわけではありません。

        ## WaitIdleとWaitIdleAsync

        `WaitIdle()`は既にqueueへ投入された全処理の完了を同期的に待ちます。resize、shutdown、まとめてresourceを再生成する境界に使用します。

        ```csharp
        device.MainQueue.WaitIdle();
        RecreateSizeDependentResources();
        ```

        `WaitIdleAsync()`は同じ意味の非同期版です。browser WebGPUでは`WaitIdle()`が利用できないため、必ず非同期版をawaitします。

        | method | submit | completion wait | 主な用途 |
        | --- | --- | --- | --- |
        | `Submit` | する | しない | 完了を別の仕組みで管理する低水準経路 |
        | `SubmitAndWait` | する | 同期的にqueue idleまで待つ | tutorial、readback、one-shot |
        | `SubmitAsync` | する | backendに応じてawait | browser、非同期処理 |
        | `WaitIdle` | しない | 既存queue処理を同期的に待つ | native resize、shutdown |
        | `WaitIdleAsync` | しない | 既存queue処理をawait | browser resize、shutdown |

        ## RenderGraphとの関係

        手書きcommandでは利用者が`Barrier`を置きます。RenderGraphではpassのRead/Write宣言から依存とbarrierを構築します。ただしgraphがbarrierを挿入しても、submit後のCPU完了待ちまで自動化されるわけではありません。

        ```text
        RenderGraph: pass間のproducer / consumer依存
        Queue API:   commandのsubmitとCPUから見た完了境界
        ```

        次ページでは、手書きBarrierをpassのRead/Write宣言へ置き換えるRenderGraphを扱います。Vulkan、DirectX 12、native/browser WebGPUがBarrierとqueue完了をどうlowerするかは[GPU同期の内部実装](story:Internals/Gpu/Synchronization)を参照してください。

        ## 典型的な失敗

        - compute結果が古い → producerとconsumerの間に適切な`Barrier`がない。
        - copyした画像が壊れる → `ColorOutput → Copy`のbarrierまたはtexture state遷移を確認する。
        - `Submit`へ変えたら時々壊れる → GPU完了前にresourceやcommandを再利用・破棄している。
        - browserで`WaitIdle`が例外になる → `WaitIdleAsync`または`SubmitAsync`をawaitする。
        - resizeのたびに壊れる → queue完了前にsize依存resourceを作り直している。
        - 毎frame遅い → `SubmitAndWait`や`WaitIdle`でCPU/GPUを直列化している。
        """, toc: true);
    }

}
