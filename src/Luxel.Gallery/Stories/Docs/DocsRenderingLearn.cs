using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>初心者向けレンダリング学習経路。実行可能な正は samples/LuxelTriangle。</summary>
public static class DocsRenderingLearn
{
    [Story("Learn/Rendering/Overview", Order = 0)]
    public static Widget Overview(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # Rendering 学習ガイド

        **難易度:** Beginner　 **実行環境:** Standalone + Gallery　 **Backend:** Vulkan / DirectX 12

        この章は、Gallery のデモを見るだけでなく、自分のウィンドウへ描画できるところまでを順番に進みます。最小アプリの実装はリポジトリの `samples/LuxelTriangle/` が単一の正です。

        ## 15ステップの進み方

        1. [Environment](story:Learn/Rendering/Environment) — OS、GPU、backend、shader cacheを確認
        2. [ClearColor](story:Learn/Rendering/ClearColor) — window / surface / event loop / resizeを理解
        3. [FirstTriangle](story:Learn/Rendering/FirstTriangle) — vertex buffer / shader / pipeline / commandを接続
        4. [BuffersAndBindings](story:Learn/Rendering/BuffersAndBindings) — memory kind、ABI、bindless indexを理解
        5. [Shaders](story:Learn/Rendering/Shaders) — Slangを追加してSPIR-V/DXIL cacheを更新
        6. [Textures](story:Learn/Rendering/Textures) — RGBA、UV、sampler、upload lifetimeを理解
        7. [TransformsAndCamera](story:Learn/Rendering/TransformsAndCamera) — indexed cubeをMVPで動かし、resize時のaspectを保つ
        8. [DepthCullingLighting](story:Learn/Rendering/DepthCullingLighting) — depth、front face、back-face culling、Lambert光を追加
        9. [FrameLoopAndSynchronization](story:Learn/Rendering/FrameLoopAndSynchronization) — frameの寿命、submit、present、待機を整理
        10. [FirstRenderGraph](story:Learn/Rendering/FirstRenderGraph) — passとresource依存を宣言し、post-processへ進む
        11. [First2DScene](story:Learn/Rendering/First2DScene) — Scene2Dを作り、4つの2D APIを使い分ける
        12. [StaticGltf](story:Learn/Rendering/StaticGltf) — ECSなしで静的glTFを1 drawする
        13. [Debugging](story:Learn/Rendering/Debugging) — 真っ黒、shader、resize、asset問題を切り分ける
        14. [Shipping](story:Learn/Rendering/Shipping) — shader/assetsをpublishし、別cwdからsmokeする
        15. [Demos/3D/Triangle](story:Demos/3D/Triangle) — Galleryのoffscreen実例を操作

        > [!IMPORTANT]
        > `GpuView` と `IGpuScene` はGallery内でデモを表示するためのハーネスです。通常アプリでは `WindowSystem`、`NativeWindow`、`GpuSurface` を使います。

        ## どのAPIまで学ぶか

        R1の三角形、R2のbuffer ABIとshader cache、R3のtexture付きquadからdepth/culling/方向光に続き、R4ではframe loopとRenderGraphへ進みます。`--stage graph`でdirect描画を1 passへ移し、`--stage post`でtransient resourceとcompute post-processを追加します。R5では2D、ECSを使わない静的glTF、デバッグ、publishまで進みます。複数frame-in-flightは本番設計として説明しますが、現在の公開queue APIとtutorialはper-frame fenceをまだ提供しません。
        """, toc: true);
    }

    [Story("Learn/Rendering/Environment", Order = 1)]
    public static Widget Environment(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # レンダリング環境を確認する

        **難易度:** Beginner　 **実行環境:** Standalone　 **Backend:** Vulkan / DirectX 12

        **前提:** [Overview](story:Learn/Rendering/Overview)　 **次:** [ClearColor](story:Learn/Rendering/ClearColor)

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

        ## Backendとdeviceを作る最小コード

        ```csharp
        using GpuDevice device = backend switch
        {
            "dx" or "d3d12" => new GpuDevice(D3D12Backend.Create()),
            _ => new GpuDevice(VulkanBackend.Create()),
        };
        using GpuSurface surface = window.CreateSwapchain(device);
        ```

        Linuxではwindowが提供する`IVulkanWindowSurface`を`VulkanBackendOptions.WindowSurface`へ渡します。device、surface、window systemは`using`で所有者を明確にし、終了前にqueueをidleにします。

        ## 典型的な失敗

        - Linuxで `DISPLAY` がない → X11 serverを起動する
        - Linuxで `dx` を指定 → Vulkanの `vk` を使う
        - shader cache mismatch → 上記 `CompileLuxelShaderCache` を実行する
        - Vulkan deviceがない → Vulkan driverまたはlavapipeの導入を確認する
        """, toc: true);
    }

    [Story("Learn/Rendering/ClearColor", Order = 2)]
    public static Widget ClearColor(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # ウィンドウとClear Color

        **難易度:** Beginner　 **実行環境:** Standalone　 **Backend:** Vulkan / DirectX 12

        **前提:** [Environment](story:Learn/Rendering/Environment)　 **次:** [FirstTriangle](story:Learn/Rendering/FirstTriangle)

        `samples/LuxelTriangle/Program.cs` がstandaloneアプリの外枠です。責務は次の順です。

        ```text
        WindowSystem → NativeWindow → GpuDevice → GpuSurface
                     → event loop → Render → Present
        ```

        `GpuSurface`へrender targetを直接渡すのではなく、RGBA8のCPU可視framebufferを渡します。サンプルはGPU render targetをclearし、三角形を描き、`CopyTextureToBuffer`でframebufferへコピーします。三角形を外してもclear colorだけは表示されるため、window / surface / presentの切り分けに使えます。

        ```csharp
        command.BeginRendering(target, null, 0.055f, 0.07f, 0.11f, 1)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(target, framebuffer);
        command.Finish();
        device.MainQueue.SubmitAndWait(command);
        surface.Present(framebuffer, stridePixels, width, height);
        ```

        ## Resize

        resize callbackでは即座にGPU resourceを破棄せず、次のevent-loop iterationでqueueをidleにしてからsurface、render target、framebufferを作り直します。最小化中の0×0では描画を休止します。

        D3D12のtexture readback row pitchは256 byte単位なので、RGBA8のstrideは64 pixel単位へ揃えます。`Present`には実際のwidthと、揃えたstrideの両方を渡します。
        """, toc: true);
    }

    [Story("Learn/Rendering/FirstTriangle", Order = 3)]
    public static Widget FirstTriangle(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # はじめての三角形

        **難易度:** Beginner　 **実行環境:** Standalone + Gallery　 **Backend:** Vulkan / DirectX 12

        **前提:** [ClearColor](story:Learn/Rendering/ClearColor)　 **次:** [BuffersAndBindings](story:Learn/Rendering/BuffersAndBindings)

        {{StoryRef(ctx, "Demos/3D/Triangle")}}

        上の表示はGalleryのoffscreenデモです。コピーして動かす完全なstandalone実装は次の4ファイルです。

        - `samples/LuxelTriangle/LuxelTriangle.csproj`
        - `samples/LuxelTriangle/Program.cs`
        - `samples/LuxelTriangle/TriangleRenderer.cs`
        - `shaders/tutorial_triangle.slang`

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

    [Story("Learn/Rendering/BuffersAndBindings", Order = 4)]
    public static Widget BuffersAndBindings(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # バッファ、ABI、bindless binding

        **難易度:** Beginner　 **実行環境:** Standalone + Gallery　 **Backend:** Vulkan / DirectX 12

        **前提:** [FirstTriangle](story:Learn/Rendering/FirstTriangle)　 **次:** [Shaders](story:Learn/Rendering/Shaders)

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

    [Story("Learn/Rendering/Shaders", Order = 5)]
    public static Widget Shaders(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # Slang shaderとGit cache

        **難易度:** Beginner　 **実行環境:** Standalone build / publish　 **Backend:** Vulkan / DirectX 12

        **前提:** [BuffersAndBindings](story:Learn/Rendering/BuffersAndBindings)　 **次:** [Textures](story:Learn/Rendering/Textures)

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

    [Story("Learn/Rendering/Textures", Order = 6)]
    public static Widget Textures(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # Texture付きquad

        **難易度:** Beginner　 **実行環境:** Standalone　 **Backend:** Vulkan / DirectX 12

        **前提:** [Shaders](story:Learn/Rendering/Shaders)　 **次:** [TransformsAndCamera](story:Learn/Rendering/TransformsAndCamera)

        実行可能な正は `samples/LuxelTriangle/TriangleRenderer.cs`、ABIは `samples/LuxelTriangle/TutorialAbi.cs`、shaderは `shaders/tutorial_3d.slang` です。この段階では4頂点・6 indexのquadへ小さなRGBA checker textureを貼ります。

        ## 実行と期待結果

        ```powershell
        dotnet build samples/LuxelTriangle/LuxelTriangle.csproj
        dotnet test tests/Luxel.Tests/Luxel.Tests.csproj --filter TutorialAbiTests
        dotnet run --project samples/LuxelTriangle -- vk --stage texture
        dotnet run --project samples/LuxelTriangle -- vk --stage texture --frames 3
        # Windowsのみ
        dotnet run --project samples/LuxelTriangle -- dx --stage texture --frames 3
        ```

        成功すると暗い背景の中央に、4×4の橙/紫checkerを貼ったquadが表示されます。UV向きを検証するときは一時的に四隅を別色へ変えます。`--frames 3`はsmoke用で、目視確認するときはframes指定なしで起動し、windowを横長・縦長へresizeしてください。

        ## Pixel、色空間、alpha

        `GpuDevice.CreateTexture(width, height, data, GpuFormat.Rgba8Unorm)`へ渡すdataは、**左上から右へ進むtightなRGBA8 row**を上から下へ並べます。公開APIのformatには現在sRGB variantがないため、PNGなどsRGB authored colorを`Rgba8Unorm`として読む場合はshaderで明示的にsRGBからlinearへdecodeし、照明・補間をlinearで行います。`shaders/tutorial_3d.slang`は教材向けの近似として`pow(srgb, 2.2)`で明示decodeし、出力前に逆変換します。製品用の正確な変換ではIEC sRGBの低輝度linear branchも実装してください。decodeを0回か1回に固定し、sRGB formatが追加された将来にhardware decodeとshader decodeを重ねないことが重要です。

        `GpuBlendMode.None`ではalphaは出力値に残るだけで背景とは混ざりません。透過を試すなら`GpuBlendMode.AlphaBlend`を選び、tutorialはstraight alpha（RGBをalphaで事前乗算しない）に統一します。opaque教材ではalphaを1にして、色空間とblendの問題を同時に持ち込まないのが安全です。

        ## UV原点とsampler

        tutorialの規約は **CPU imageの(0,0)とUV(0,0)を左上**、`u`は右、`v`は下です。shaderやasset loaderでさらにVを反転しないでください。上下を示す非対称checkerを使うと、UVの二重反転をすぐ発見できます。

        `CreateSampler`で選べるfilterは現在`Point` / `Linear`、addressは`Clamp` / `Repeat`です。mipmap、anisotropic、border color、mirror addressは公開APIにまだありません。1 pixelの境界を観察するときはPoint、通常表示はLinear、UVを0..1へ収める教材はClamp、2倍以上へ広げる実験だけRepeatを使います。

        ```text
        CPU RGBA bytes → CreateTexture → bindless texture index ┐
        CreateSampler  → bindless sampler index ────────────────┼→ tutorial_3d.slang → sampled color
        Vertex UV + SV_VertexID / index pulling ────────────────┘
        ```

        quadも固定functionのvertex/index bindingではありません。shaderが`SV_VertexID`からindex bufferをraw loadし、そのindexでvertex bufferのposition / color / UVをpullします。6頂点を複製するのではなく、4頂点をindex列 `0,1,2, 0,2,3` で再利用します。C#とSlangでindex width、vertex stride、UV offsetを一致させてください。

        ## Upload rowとbackend差

        呼び出し側の入力は常に `width * 4` byteのtight rowで、余分なpaddingを含めません。Vulkan backendはこれをstaging copyへ渡します。D3D12 backendは内部で各rowを256 byte境界のfootprintへ詰め直しますが、そのpaddingはAPI利用者のdataへ含めません。これはreadback framebufferの`StridePixels`を64 pixelへ揃える規則とは別です。

        現在の`CreateTexture` uploadは同期的です。methodが戻った時点で呼び出し元の`ReadOnlySpan<byte>`や一時配列は再利用・解放できます。一方、生成された`GpuTexture`と`GpuSampler`、それらの`BindlessIndex`は記録済みcommandが完了するまで生存させます。rendererがtexture / sampler / vertex / index buffer / pipelineを所有し、dispose順は **queue idle → resize resource → pipeline → sampler / texture → index / vertex buffer → device** とします。

        ## Backend差を切り分ける

        Slang source、RGBA channel順、UV規約、bindless indexは共通です。VulkanはSPIR-Vとdescriptor array、D3D12はDXILとdescriptor heapを使い、D3D12だけupload footprintが256 byte row alignmentを要求します。backend画像はdriverの丸めやlinear filteringで数LSB違うことがあるため、pixel-perfect一致ではなく小さなchannel toleranceで比較します。ただし上下反転、R/B交換、1 rowずれは許容差ではなくbugです。

        ## 典型的な失敗

        - quadが白い / 黒い → texture / samplerのbindless indexとroot argsのoffsetを確認
        - RとBが入れ替わる → uploadがRGBAなのにBGRAとして作っていないか確認
        - 上下が逆 → CPU row順、UV規約、shaderのV反転を1か所だけにする
        - 右端から次rowへ色が流れる → 呼び出し側dataからD3D12用256 byte paddingを除く
        - Vulkanだけ、またはD3D12だけ崩れる → 同じshader cacheを再生成し、両backendのcompiled fileを確認
        - 色が暗すぎる / 明るすぎる → sRGB decodeを0回または1回に固定し、二重decodeを避ける
        - textureをdisposeするとdevice lost → submit完了前にbindless resourceを破棄している
        """, toc: true);
    }

    [Story("Learn/Rendering/TransformsAndCamera", Order = 7)]
    public static Widget TransformsAndCamera(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # Transformとcameraでindexed cubeを描く

        **難易度:** Beginner+　 **実行環境:** Standalone　 **Backend:** Vulkan / DirectX 12

        **前提:** [Textures](story:Learn/Rendering/Textures)　 **次:** [DepthCullingLighting](story:Learn/Rendering/DepthCullingLighting)

        実コードは `samples/LuxelTriangle/TriangleRenderer.cs`、共有ABIは `samples/LuxelTriangle/TutorialAbi.cs`、vertex pullingとMVPは `shaders/tutorial_3d.slang` を参照します。texture付きquadを24頂点・36 indexのcubeへ広げ、model / view / projectionをroot argumentsから渡します。

        ## 実行と期待結果

        ```powershell
        dotnet build samples/LuxelTriangle/LuxelTriangle.csproj
        dotnet test tests/Luxel.Tests/Luxel.Tests.csproj --filter TutorialAbiTests
        dotnet run --project samples/LuxelTriangle -- vk --stage transform
        dotnet run --project samples/LuxelTriangle -- vk --stage transform --frames 3
        dotnet run --project samples/LuxelTriangle -- vk --stage transform --size 801x603 --frames 3
        # Windowsのみ
        dotnet run --project samples/LuxelTriangle -- dx --stage transform
        dotnet run --project samples/LuxelTriangle -- dx --stage transform --frames 3
        ```

        成功時はdepth test/writeが有効、cullingは無効の、回転するtexture付きcubeが見えます。次ページでback-face cullingと光を足す前後を比較できます。windowを横長・縦長へresizeしてもcubeが横につぶれず、visible client sizeのaspectに追従することを確認します。

        ## このtutorialの座標規約

        規約を混ぜないことが最重要です。

        | 項目 | tutorialの規約 |
        | --- | --- |
        | handedness | right-handed |
        | world axes | +X=右、+Y=上、cameraの前方=-Z |
        | unit | 1.0 = 1 meter相当 |
        | UV | (0,0)=左上、+V=下 |
        | clip/depth | shader出力後のdepthは0..1 |
        | front face | screen上でcounter-clockwiseをfrontとしてmesh indexを作る |
        | matrix | CPUは`System.Numerics.Matrix4x4`で`model`と`view * projection`を作り、root argsへtransposeして渡す |

        CPUの意味上の順序はrow-vectorの `p * model * view * projection` です。実装は`Model = Matrix4x4.Transpose(model)`、`ViewProjection = Matrix4x4.Transpose(view * projection)`としてcolumn-majorのSlang fieldへ渡し、shaderでは `mul(g_args.model, float4(position, 1))`、続けて`mul(g_args.viewProjection, worldPosition)`とmatrix×column-vectorで計算します。bufferから4 rowを`Load4`して行列を組み立てる方式へ変える場合は同じtransposeを機械的に残さないでください。**掛け算順とupload layoutを別問題として確認**します。

        cameraは右手系look-at、projectionは0..1 depthのperspectiveを使います。.NET helperが異なるclip規約を返す場合はprojectionを補正し、VulkanだけYを場当たり的に反転するのではなく、Luxelの両backendで同じ最終clip規約になる1か所へ集約します。nearは0より大きく、farより十分小さくします。

        ## Indexed vertex pulling

        core APIに固定functionの`DrawIndexed`はありません。commandの`Draw(36)`が生成する`SV_VertexID`をindex-stream番号として扱い、raw index bufferからvertex indexをloadし、そのindexでvertex bufferをloadします。

        ```slang
        uint vertexIndex = g_buffers[indexBufferIndex].Load(vertexId * 4);
        Vertex vertex = g_buffers[vertexBufferIndex].Load<Vertex>(vertexIndex * vertexStride);
        float4 clip = mul(float4(vertex.position, 1.0), modelViewProjection);
        ```

        tutorialは分かりやすさのため32-bit indexを使います。24頂点に面ごとのnormal / UVを持たせ、位置が同じcornerでも面が違えば別vertexにします。8 positionだけを共有すると、UV seamとhard normalを表せません。

        ## Resizeとaspect

        projectionのaspectはrequestされたwindow sizeやreadbackのaligned strideではなく、**resize callbackで確定したvisible client width / height**から `width / height` を計算します。D3D12用`StridePixels`を使うと、64 pixel alignmentの余白ぶん画角がずれます。0×0の最小化中はprojection計算とrenderを休止し、正のsizeへ戻った次frameでcolor target、framebuffer、のちのdepth targetとprojectionをまとめて更新します。

        resizeを目視検証するときは正方形、横長、縦長を往復し、cubeの辺の比率と中心位置を確認します。自動画像比較は同じvisible size・同じframeのVulkan/D3D12画像を小さなtolerance付きで比較し、aspectの違いは許容しません。

        ## Normal transform

        平行移動をnormalへ適用せず、uniform scale + rotationだけならmodelの3x3で変換してnormalizeします。non-uniform scaleを許すなら **model 3x3のinverse-transpose** を使います。projectionやviewの平行移動をnormalへ掛けないでください。normal matrixもC# / Slangのrow/column規約と同じ規則でuploadします。

        ## 所有権とbackend差

        rendererがvertex / index buffer、texture / sampler、pipelineを全window lifetimeで所有します。model / view / projectionは毎frameの値ですが、root argumentsはcommand記録時にbytesとしてcopyされます。resize resourceはqueue idle後に作り直し、静的mesh resourceはresizeで再生成しません。

        VulkanはSPIR-V push constants、D3D12はroot constantsへ同じMVP bytesを渡します。clip-space変換やtransposeをbackend別shaderへ分岐させないでください。両backendでcubeの回転方向、front face、画角が一致することを基準にします。

        ## 典型的な失敗

        - cubeが消える → near/far、cameraの-Z前方、W、matrix multiplication orderを確認
        - 平行移動が回転する / orbitする → `model * view * projection`の順を確認
        - VulkanとD3D12で上下が違う → projection補正をbackend layerとshaderの両方で行っていないか確認
        - resizeでcubeが伸びる → `StridePixels`ではなくvisible width / heightからaspectを作る
        - 一部の面だけtextureが崩れる → index値、32-bit stride、faceごとのUV seamを確認
        - 光を入れるとnormalが傾く → directionとしてw=0で扱い、non-uniform scaleではinverse-transposeを使う
        """, toc: true);
    }

    [Story("Learn/Rendering/DepthCullingLighting", Order = 8)]
    public static Widget DepthCullingLighting(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # Depth、culling、方向光

        **難易度:** Beginner+　 **実行環境:** Standalone　 **Backend:** Vulkan / DirectX 12

        **前提:** [TransformsAndCamera](story:Learn/Rendering/TransformsAndCamera)　 **次:** [FrameLoopAndSynchronization](story:Learn/Rendering/FrameLoopAndSynchronization)

        完成形は `samples/LuxelTriangle/TriangleRenderer.cs`と`samples/LuxelTriangle/TutorialAbi.cs`、shaderは `shaders/tutorial_3d.slang` です。indexed cubeへD32 depth target、back-face culling、最小のdirectional Lambert lightを追加します。

        ## 実行と期待結果

        ```powershell
        dotnet build samples/LuxelTriangle/LuxelTriangle.csproj
        dotnet test tests/Luxel.Tests/Luxel.Tests.csproj --filter TutorialAbiTests
        dotnet run --project samples/LuxelTriangle -- vk --stage lighting
        dotnet run --project samples/LuxelTriangle -- vk --stage lighting --frames 3
        dotnet run --project samples/LuxelTriangle -- vk --stage lighting --size 801x603 --frames 3
        # Windowsのみ
        dotnet run --project samples/LuxelTriangle -- dx --stage lighting
        dotnet run --project samples/LuxelTriangle -- dx --stage lighting --frames 3
        ```

        成功すると、手前の面が奥の面を正しく隠し、外側を向く面だけが描かれ、light方向へ向いた面が明るいtexture付きcubeになります。frames指定なしでresizeし、正方形・横長・縦長でもaspect、depth、cullingが安定することを確認します。

        ## Depth target

        pipelineはcolor formatに加えて`DepthTest = true`、`DepthWrite = true`、`DepthFormat = GpuFormat.D32Float`を持ち、resize時にvisible sizeと同じ`CreateDepthTarget`を作ります。render pass開始時にcolorとdepthを毎frame clearし、`BeginRendering`へcolor targetとdepth targetの両方を渡します。通常の0..1 depthではnearが小さくfarが大きいため、比較はless相当、clear値は1.0です。

        depth targetはsampling textureやCPU framebufferではありません。rendererがresize単位で所有し、**queue idle → old framebuffer / depth / colorをdispose → new resourcesを作成**します。pipelineが参照するのはformat/stateであり個々のtarget objectではないため、size変更だけならpipeline再生成は不要です。

        ## Front faceとculling

        front faceはmesh index order、projectionのY向き、viewport変換の組み合わせで決まります。このtutorialは最終screen上のcounter-clockwiseをfrontとし、back facesをcullします。cubeを外側から見た各triangleのindexを同じ向きで並べます。

        `GpuRasterDesc`では`CullMode = GpuCullMode.Back`と`FrontFace = GpuFrontFace.CounterClockwise`を明示します。選べるcull modeは`None` / `Front` / `Back`、front faceは`CounterClockwise` / `Clockwise`です。診断時は`CullMode.None`へ戻し、(1)depthのみ、(2)cullingのみ、(3)両方、の順で有効化します。negative scaleはwindingを反転するため、教材では使わないかfront-face扱いを明示的に反転します。

        VulkanとD3D12はviewport/front-face定義が内部で異なり得ますが、Luxel利用者から見たCCW/front規約は同じであるべきです。backendごとにindexを逆順にしたりshaderでposition.yを追加反転したりせず、backend変換を1か所に閉じ込めます。

        ## 最小Directional Lambert

        directional lightは位置を持たず、world-spaceの単位方向と色・強度だけをroot argumentsへ渡します。ここではsurfaceからlightへ向かう `L` とworld normal `N`をnormalizeし、次を計算します。

        ```slang
        float ndotl = saturate(dot(normalize(worldNormal), normalize(lightDirection)));
        float3 linearRgb = albedoLinear * (ambient + lightColor * intensity * ndotl);
        return float4(linearRgb, alpha);
        ```

        textureがsRGB authoredなら[Textures](story:Learn/Rendering/Textures)のdecode後にLambertを掛けます。ambientは完全な黒面を避ける小値で、specular、shadow、gamma-correct outputはこの章の範囲外です。normalはmodel変換後のworld-space、lightもworld-spaceに統一します。dotの符号が逆なら「lightが進む方向」と「surfaceからlightへの方向」を混同しています。

        opaque cubeは`GpuBlendMode.None`、alpha=1を維持します。透明物をdepth write + cullingだけで正しく描くことはできず、sortや別passが必要なので後続課題です。

        ## Backend画像とplay検証

        1. `vk`とWindowsの`dx`を同じwindow visible size、同じ初期camera/modelで起動する。
        2. `--frames 3`のsmokeが終了コード0で完了することを確認する。
        3. 通常起動でresizeを正方形→横長→縦長→正方形と往復する。
        4. cubeの縦横比、見える面、明暗の面、textureの上下を比較する。
        5. capture比較はlinear filteringとfloat丸めを考慮した小さなchannel toleranceを使う。silhouette、edge位置、front/backの違いは許容しない。

        animation/playを追加した場合は固定deltaまたは同じframe numberでcaptureし、wall-clock時刻をgoldenへ混ぜません。backend差に見えても、resize直後だけ壊れるならaspect/depth target再生成、特定角度だけ面が消えるならwinding/normalを先に疑います。

        ## 所有権と寿命

        rendererはpipeline、mesh buffers、texture、samplerを長寿命resourceとして、color/depth targetとreadback framebufferをresize resourceとして所有します。command bufferは1 frame、root args bytesもその記録に属します。`SubmitAndWait`完了前に、pipeline、bindless resource、color/depth targetをdisposeしません。終了時は **queue idle → framebuffer / depth / color → pipeline → sampler / texture → index / vertex buffer → device** の順です。

        ## 典型的な失敗

        - 奥の面が手前へ描かれる → depth targetを渡したか、DepthTest/DepthWrite、clear=1、0..1 projectionを確認
        - cubeが丸ごと消える → front faceが逆。cullingを切ってindex windingとprojection Yを確認
        - 内側の面だけ見える → 全triangleのwinding、negative scale、front-face規約を確認
        - 面が点滅する → near/far精度、同一平面、depth targetのresize漏れを確認
        - 明るい面が逆 → light directionの意味とdotの符号、normalのspaceを合わせる
        - non-uniform scaleで光が歪む → normalへinverse-transposeを使う
        - backend間でedgeだけ1程度違う → tolerance対象。面の有無やsilhouette差はbugとして扱う
        - resize後だけdepthが壊れる → colorだけでなくdepth targetもvisible sizeで再生成する
        """, toc: true);
    }

    [Story("Learn/Rendering/FrameLoopAndSynchronization", Order = 9)]
    public static Widget FrameLoopAndSynchronization(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # Frame loopと同期

        **難易度:** Beginner+　 **実行環境:** Standalone　 **Backend:** Vulkan / DirectX 12

        **前提:** [DepthCullingLighting](story:Learn/Rendering/DepthCullingLighting)　 **次:** [FirstRenderGraph](story:Learn/Rendering/FirstRenderGraph)

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

    [Story("Learn/Rendering/FirstRenderGraph", Order = 10)]
    public static Widget FirstRenderGraph(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # はじめてのRenderGraph

        **難易度:** Beginner+　 **実行環境:** Standalone + DevTools　 **Backend:** Vulkan / DirectX 12

        **前提:** [FrameLoopAndSynchronization](story:Learn/Rendering/FrameLoopAndSynchronization)　 **次:** [First2DScene](story:Learn/Rendering/First2DScene)

        sampleは `samples/LuxelTriangle/Program.cs`、`samples/LuxelTriangle/TriangleRenderer.cs`、`samples/LuxelTriangle/TutorialAbi.cs`、scene shaderの`shaders/tutorial_3d.slang`、post-process shaderの`shaders/compute_tutorial_postprocess.slang`です。RenderGraph本体は`src/Luxel.RenderGraph/`にあります。

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

        `graph`はlightingと同じcubeを1 passのRenderGraph経由で描き、移行前後の画像を比較する段階です。`post`はsceneをtransient color/depthへ描画し、colorをtransient bufferへcopyし、compute shaderで寒色のshadowと柔らかなvignetteを加えてexternal framebufferへ書きます。どちらも3 frame smokeが終了コード0で完了し、通常起動ではresize後も同じ構成を新しいsizeで再構築します。

        shader sourceを変更した場合はcacheも更新します。

        ```powershell
        dotnet msbuild shaders/Luxel.ShaderCache.proj -t:CompileLuxelShaderCache
        ```

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

        **graphを`SubmitAndWait`より前にdisposeしてはいけません。** disposeはgraph所有のtransient resourceを解放するため、GPUがまだ参照している可能性があります。現行sampleでは完了待ちの後にdisposeします。将来frames-in-flightを導入する場合はgraphまたはそのtransient allocationをframe slotが所有し、そのslotのfence完了後に破棄・再利用します。

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


    [Story("Learn/Rendering/First2DScene", Order = 11)]
    public static Widget First2DScene(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # はじめての2Dシーン

        **難易度:** Beginner　 **実行環境:** Gallery / Standalone / Headless　 **Backend:** Vulkan / DirectX 12 / Skia CPU

        **前提:** [FirstRenderGraph](story:Learn/Rendering/FirstRenderGraph)　 **次:** [StaticGltf](story:Learn/Rendering/StaticGltf)

        {{StoryRef(ctx, "Demos/2D/Shapes")}}

        `Scene2D`は「何を描くか」をCPU側で組み立てる最小APIです。次のコードだけで角丸矩形、円、線を含むsceneを作れます。

        ```csharp
        using Luxel.TwoD;

        var scene = new Scene2D();
        scene.FillRoundedRect(0xFF2F6FED, 24, 24, 220, 120, 18);
        scene.FillCircle(0xFFFFC857, 86, 84, 28);
        scene.StrokeLine(0xFFE7EAF0, 4, 32, 176, 250, 176);
        ```

        GPUへ出すstandalone側は`Rasterizer2D`がsceneをencodeし、commandへrenderを記録します。geometryが変わらないなら`encoded`を毎frame作り直さず保持します。

        ```csharp
        using var rasterizer = new Rasterizer2D(device);
        using var encoded = rasterizer.Encode(scene);
        using GpuCommandBuffer cmd = device.MainQueue.StartCommandRecording();

        rasterizer.Render(cmd, encoded, Camera2D.Pixels,
            width, height, framebuffer);
        cmd.Finish();
        device.MainQueue.SubmitAndWait(cmd);
        ```

        ## 4つの入口の選び方

        | API | 選ぶ場面 | GPU | 注意点 |
        | --- | --- | --- | --- |
        | `Scene2D` | shape/pathを直接作る | render時のみ必要 | callerがencode/render寿命を所有 |
        | `RetainedCanvas` | objectが残りtransform/styleだけ変わる | headless構築可 | `Invalidate()`連打ではなく部分更新を使う |
        | UI `Canvas2D` | GalleryやUIへ小さな図を埋め込む | hostが提供 | standalone window APIではない |
        | `SkiaRenderer` | CI、headless test、CPU参照画像 | 不要 | image shape非対応、AA edgeはGPUと完全一致しない |

        retained treeではnodeを保持し、変更箇所だけ更新します。

        ```csharp
        var canvas = new RetainedCanvas();       // headlessでも構築可能
        UiNode card = canvas.AddChild(canvas.Root);
        card.Content = new Scene2D()
            .FillRoundedRect(Color2D.White, 20, 20, 240, 120, 16);
        card.Color = 0xFF2F6FED;
        card.Transform = Affine2D.Translate(12, 8);

        if (canvas.HasPendingChanges)
            Console.WriteLine("次のGPU renderで差分を反映する");
        ```

        UI内なら`Canvas2D(draw: scene => ...)`、headlessなら`SkiaRenderer`を使います。stroke widthはscreen pixel、world座標変換はcameraが担当します。透明imageはpremultiplied RGBAを前提にし、Skia pathではGPU bindless imageを描けません。
        """, toc: true);
    }

    [Story("Learn/Rendering/StaticGltf", Order = 12)]
    public static Widget StaticGltf(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # ECSなしで静的glTFを描く

        **難易度:** Beginner+　 **実行環境:** Gallery / Standalone　 **Backend:** Vulkan / DirectX 12

        **前提:** [First2DScene](story:Learn/Rendering/First2DScene)　 **次:** [Debugging](story:Learn/Rendering/Debugging)

        {{StoryRef(ctx, "Demos/3D/GltfBox")}}

        最初の1モデルではECS、animation、skin、morphを使いません。理解する経路はこれだけです。

        ```text
        Box.gltf → AssetDocument → AssetPrimitive → GpuPrimitive → instance buffer → Draw
        ```

        `.gltf`が参照する`.bin`やimageはgltf fileのあるdirectoryから解決されます。asset rootではなく、まず絶対pathを確定してloaderへ渡します。

        ```csharp
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "Box.gltf");
        AssetDocument doc = await new GltfLoader().LoadAsync(path);
        AssetPrimitive source = doc.Meshes[0].Primitives[0];
        using GpuPrimitive primitive = GpuAssetFactory.Upload(source, device);
        ```

        `GpuAssetFactory.Upload`はposition/normal/UVを32-byte vertexへ変換し、indexがあれば`uint` bufferも作ります。次に1 instanceだけ用意します。

        ```csharp
        using GpuBuffer instances = device.Malloc(
            (ulong)Marshal.SizeOf<SceneInstanceData>(), GpuMemoryKind.HostMapped);
        instances.Span<SceneInstanceData>(1)[0] = new SceneInstanceData
        {
            World = Matrix4x4.Identity,
            BaseColor = source.Material?.BaseColorFactor ?? Vector4.One,
        };

        bool indexed = primitive.IndexBuffer is not null;
        var args = new DrawArgs
        {
            ViewProj = Matrix4x4.Transpose(view * projection),
            VertexBufIndex = primitive.VertexBuffer.BindlessIndex,
            IndexBufIndex = indexed ? primitive.IndexBuffer!.BindlessIndex : 0xFFFFFFFFu,
            InstanceBufIndex = instances.BindlessIndex,
            InstanceStart = 0,
        };
        ```

        最後はdepth target付きpassで`scene_pbr_lite`をbindし、index countまたはvertex countをdrawします。Luxel coreに別の`DrawIndexed`はなく、shaderがindex bufferをpullします。

        ```csharp
        uint count = (uint)(indexed ? primitive.IndexCount : primitive.VertexCount);
        command.BeginRendering(color, depth, 0.05f, 0.06f, 0.09f, 1, 1)
            .SetGraphicsPipeline(pipeline)
            .SetRootArguments(args)
            .Draw(count, 1)
            .EndRendering();
        ```

        ここまでが静的1 primitiveです。scene graphと複数node/ECSは[Docs/Assets](story:Docs/Assets)、TRS animationは[GltfAnimated](story:Demos/3D/GltfAnimated)、skinは[GltfSkinned](story:Demos/3D/GltfSkinned)、morphは[GltfMorph](story:Demos/3D/GltfMorph)へ分けて進んでください。
        """, toc: true);
    }

    [Story("Learn/Rendering/Debugging", Order = 13)]
    public static Widget Debugging(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # レンダリングをデバッグする

        **難易度:** Beginner　 **実行環境:** Standalone / Gallery / DevTools　 **Backend:** Vulkan / DirectX 12

        **前提:** [StaticGltf](story:Learn/Rendering/StaticGltf)　 **次:** [Shipping](story:Learn/Rendering/Shipping)

        ## 起動しない

        1. window backend、GPU backend、device、queueのどこで失敗したか例外の先頭を確認する。
        2. Linux実窓は`DISPLAY`とVulkan ICD、Windowsの`dx`はD3D12対応GPUを確認する。
        3. shader cacheを検証・再生成する。

        ```powershell
        dotnet msbuild shaders/Luxel.ShaderCache.proj -t:CompileLuxelShaderCache
        dotnet build samples/LuxelTriangle/LuxelTriangle.csproj
        ```

        runtimeは`AppContext.BaseDirectory/shaders`を読みます。source名`foo.slang`に対しgraphicsは`foo.spv`, `foo.vs.dxil`, `foo.ps.dxil`、computeは`foo.spv`, `foo.dxil`が必要です。

        ## 真っ黒なときの最小probe

        drawを疑う前にclear→copy→presentだけへ戻します。

        ```csharp
        command.BeginRendering(target, null, 1, 0, 1, 1)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(target, framebuffer, stridePixels);
        command.Finish();
        device.MainQueue.SubmitAndWait(command);
        surface.Present(framebuffer, stridePixels, visibleWidth, visibleHeight);
        ```

        magentaが見えなければwindow/surface/copy/stride、見えればpipeline/root args/drawへ問題を絞れます。次にcullingを一時的に`None`、depthをoff、identity matrix、白textureへ一つずつ戻します。

        - 裏面だけ消える: front faceとindex windingを揃える
        - depthで全消去: depth target、clear=1、near/far、projectionを確認
        - resize後だけ壊れる: queue完了後にcolor/depth/framebufferを同じsizeで再生成
        - texture上下反転: CPU rowとUV(0,0)を左上規約へ統一
        - 暗すぎる: sRGB decode/encodeを0回または1回に固定
        - D3D12 copy破損: RGBA8 strideを64 pixel単位へalignし、visible widthと分離
        - glTF asset missing:解決した絶対pathをerrorへ出し、外部`.bin`/imageも同梱
        - 時々device lost: GPU完了前のbuffer/texture/bindless index再利用を疑う

        `SubmitAndWait`は切り分けには有効ですがCPU/GPUを直列化します。動作確認後にframe slot/fence設計へ戻し、毎frameの`WaitIdle`をperformance fixとして残さないでください。
        """, toc: true);
    }

    [Story("Learn/Rendering/Shipping", Order = 14)]
    public static Widget Shipping(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # Publishして別ディレクトリから起動する

        **難易度:** Beginner+　 **実行環境:** Published standalone app　 **Backend:** Vulkan / DirectX 12

        **前提:** [Debugging](story:Learn/Rendering/Debugging)　 **次:** このページが初心者経路の終点です。3D capstoneは[Apps/Game/Range](story:Apps/Game/Range)

        executable projectが`shaders/Luxel.Shaders.targets`をimportすると、compiled shaderはbuild/publishの`shaders/`へ入ります。loose assetは`Content`として出力とpublishの両方へ含めます。

        ```xml
        <ItemGroup>
          <Content Include="assets/**">
            <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
            <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
          </Content>
        </ItemGroup>
        <Import Project="../../shaders/Luxel.Shaders.targets" />
        ```

        codeはcwdではなくexecutable基準でassetを解決します。

        ```csharp
        string model = Path.Combine(AppContext.BaseDirectory, "assets", "Box.glb");
        string shaderDirectory = Path.Combine(AppContext.BaseDirectory, "shaders");
        if (!File.Exists(model))
            throw new FileNotFoundException($"Published model is missing: {model}", model);
        ```

        ## 別cwd smoke test

        publish directory内へ`cd`して起動するだけではcwd依存を発見できません。空の別directoryをcwdにして、executableは絶対pathで起動します。

        ```powershell
        $publish = Join-Path $env:TEMP "luxel-triangle-publish"
        $cwd = Join-Path $env:TEMP "luxel-triangle-empty-cwd"
        Remove-Item $publish,$cwd -Recurse -Force -ErrorAction Ignore
        New-Item $publish,$cwd -ItemType Directory | Out-Null

        dotnet publish samples/LuxelTriangle/LuxelTriangle.csproj `
          -c Release -o $publish

        @(
          "shaders/tutorial_triangle.spv",
          "shaders/tutorial_3d.spv",
          "shaders/compute_tutorial_postprocess.spv"
        ) | ForEach-Object {
          if (-not (Test-Path (Join-Path $publish $_))) { throw "missing: $_" }
        }

        Push-Location $cwd
        try {
          & (Join-Path $publish "LuxelTriangle.exe") vk --stage post --frames 3
          if ($LASTEXITCODE -ne 0) { throw "Vulkan smoke failed" }
        } finally { Pop-Location }
        ```

        この手順はrepository rootの`rendering-ship-verify.ps1`として実行できます。Windowsで`-IncludeRange`を付けると、`LuxelRange`のFox asset、license、static/skinned shaderも構造検査してからVulkan/D3D12で起動します。

        Windowsでは同じpublishへ`dx --stage post --frames 3`も実行します。Linuxでは実行ファイル名に`.exe`を付けず、X11/XvfbとVulkan ICDを用意します。capstoneのasset/shader配置とrun/publish手順は`samples/LuxelRange/README.md`へ続きます。
        """, toc: true);
    }
}
