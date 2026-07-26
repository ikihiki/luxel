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

        ## 9ステップの進み方

        1. [Environment](story:Learn/Rendering/Environment) — OS、GPU、backend、shader cacheを確認
        2. [ClearColor](story:Learn/Rendering/ClearColor) — window / surface / event loop / resizeを理解
        3. [FirstTriangle](story:Learn/Rendering/FirstTriangle) — vertex buffer / shader / pipeline / commandを接続
        4. [BuffersAndBindings](story:Learn/Rendering/BuffersAndBindings) — memory kind、ABI、bindless indexを理解
        5. [Shaders](story:Learn/Rendering/Shaders) — Slangを追加してSPIR-V/DXIL cacheを更新
        6. [Textures](story:Learn/Rendering/Textures) — RGBA、UV、sampler、upload lifetimeを理解
        7. [TransformsAndCamera](story:Learn/Rendering/TransformsAndCamera) — indexed cubeをMVPで動かし、resize時のaspectを保つ
        8. [DepthCullingLighting](story:Learn/Rendering/DepthCullingLighting) — depth、front face、back-face culling、Lambert光を追加
        9. [Demos/3D/Triangle](story:Demos/3D/Triangle) — Galleryのoffscreen実例を操作

        > [!IMPORTANT]
        > `GpuView` と `IGpuScene` はGallery内でデモを表示するためのハーネスです。通常アプリでは `WindowSystem`、`NativeWindow`、`GpuSurface` を使います。

        ## どのAPIまで学ぶか

        R1の三角形、R2のbuffer ABIとshader cacheに続き、R3ではtexture付きquadからindexed cube、MVP、depth/culling、方向光まで進みます。RenderGraph、glTF、複数frame-in-flightは後続章の範囲です。最初からECSやRenderGraphを使う必要はありません。
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

        **前提:** [TransformsAndCamera](story:Learn/Rendering/TransformsAndCamera)　 **次:** [Docs/GpuDevice](story:Docs/GpuDevice)

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
}
