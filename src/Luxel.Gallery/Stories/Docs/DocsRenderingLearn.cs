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

        ## 最初の4ステップ

        1. [Environment](story:Learn/Rendering/Environment) — OS、GPU、backend、shader cacheを確認
        2. [ClearColor](story:Learn/Rendering/ClearColor) — window / surface / event loop / resizeを理解
        3. [FirstTriangle](story:Learn/Rendering/FirstTriangle) — vertex buffer / shader / pipeline / commandを接続
        4. [BuffersAndBindings](story:Learn/Rendering/BuffersAndBindings) — memory kind、ABI、bindless indexを理解
        5. [Shaders](story:Learn/Rendering/Shaders) — Slangを追加してSPIR-V/DXIL cacheを更新
        6. [Demos/3D/Triangle](story:Demos/3D/Triangle) — Galleryのoffscreen実例を操作

        > [!IMPORTANT]
        > `GpuView` と `IGpuScene` はGallery内でデモを表示するためのハーネスです。通常アプリでは `WindowSystem`、`NativeWindow`、`GpuSurface` を使います。

        ## どのAPIまで学ぶか

        R1では三角形の表示までです。buffer ABI、texture、camera、depth、RenderGraph、glTFは後続ページで段階的に追加します。最初からECSやRenderGraphを使う必要はありません。
        """, toc: true);
    }

    [Story("Learn/Rendering/Environment", Order = 1)]
    public static Widget Environment(StoryContext ctx)
    {
        return DocNew(ctx, $"""
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

        **前提:** [BuffersAndBindings](story:Learn/Rendering/BuffersAndBindings)　 **次:** [Docs/GpuDevice](story:Docs/GpuDevice)

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
}
