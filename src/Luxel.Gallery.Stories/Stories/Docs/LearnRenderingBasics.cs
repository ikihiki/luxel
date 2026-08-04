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

        Grapics直下からThreeDまでの順序はcourse catalogから生成されます。2DとRasterizer Internalsは、このルートを終えた後に目的に応じて選ぶ独立トラックです。

        {RenderingCourseCatalog.ApplicationRouteMarkdown()}

        **検索キーワード:** triangle / texture / camera / render graph / glTF / blank screen / 真っ黒

        > [!IMPORTANT]
        > `GpuView` とそのrender callbackはGallery内でデモを表示するためのハーネスです。通常アプリでは `WindowSystem`、`Window`、`GpuSurface` を使います。

        ## どのAPIまで学ぶか

        R1の三角形、R2のbuffer ABIとshader cache、R3のtexture付きquadからdepth/culling/方向光に続き、R4ではframe loopとRenderGraphへ進みます。`--stage graph`でdirect描画を1 passへ移し、`--stage post`でtransient resourceとcompute post-processを追加します。R5では2D、ECSを使わない静的glTF、デバッグ、publishまで進みます。複数frame-in-flightは本番設計として説明しますが、現在の公開queue APIとtutorialはper-frame fenceをまだ提供しません。
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
        > 入門例では処理順を明確にするため`SubmitAndWait`を使います。複数frame-in-flightとfenceによる同期は後続のFrame Loopページで扱います。

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

        `CreateTexture`へwidth、height、RGBA8のpixel dataを渡します。dataは左上から右へ並ぶtightなrowを、上から下へ続けます。

        ```csharp
        const uint textureWidth = 8;
        const uint textureHeight = 8;
        byte[] pixels = CreateCheckerboard(textureWidth, textureHeight);

        using GpuTexture texture = device.CreateTexture(
            textureWidth, textureHeight, pixels, GpuFormat.Rgba8Unorm);
        ```

        上のsampleではチェック柄を作る処理を`CreateCheckerboard`へ分けています。Story Sourceにはhelperの呼び出しだけが表示され、画像生成処理の本体は含まれません。8×8のRGBA8なので、pixel dataは`8 * 8 * 4 = 256 byte`です。`CreateTexture`が戻った後は入力配列を再利用できますが、作成されたtextureは描画commandが完了するまで保持します。

        ## Samplerを作成する

        samplerはpixel間の補間と、0〜1の外側に出たUVの扱いを指定します。

        ```csharp
        using GpuSampler sampler = device.CreateSampler(
            GpuSamplerFilter.Point,
            GpuSamplerAddress.Repeat);
        ```

        `Point`は最も近い1 pixelを選び、`Linear`は周囲を補間します。`Clamp`は端のpixelを延長し、`Repeat`はUVを繰り返します。

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

        典型的な問題は、RGBA/BGRAの取り違え、UVの上下反転、textureとsamplerのindex入れ替え、描画完了前のresource破棄です。より実用的なUV、色空間、upload rowの説明は[ThreeD/Textures](story:Learn/Grapics/ThreeD/Textures)で扱います。
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

        cacheが不足している、またはsourceと`inputs.sha256`が一致しない場合、通常buildは意図的に失敗します。`CompileLuxelShaderCache`を再実行し、生成物を更新してください。pipelineとshaderがGPU commandから参照されている間は破棄せず、sourceだけを変更した古いcacheを配布しないようにします。
        """, toc: true);
    }


    [Story("Learn/Grapics/FrameLoopAndSynchronization", Order = 9)]
    public static Widget FrameLoopAndSynchronization(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # Frame loopと同期

        {{RenderingCourseCatalog.Meta("Learn/Grapics/FrameLoopAndSynchronization", "Beginner+", "Standalone", "Vulkan / DirectX 12", "DepthCullingLighting")}}

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
