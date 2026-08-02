using Luxel.UI;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>初心者向けレンダリング学習経路。実行可能な正は samples/LuxelTriangle。</summary>
public static partial class DocsRenderingLearn
{
    [Story("Learn/Grapics/ThreeD/Textures", Order = 6, SampleBundle = "rendering.3d")]
    public static Widget Textures(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # Texture付きquad

        {RenderingCourseCatalog.Meta("Learn/Grapics/ThreeD/Textures", "Beginner", "Standalone", "Vulkan / DirectX 12", "Shaders")}

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


    [Story("Learn/Grapics/ThreeD/TransformsAndCamera", Order = 7)]
    public static Widget TransformsAndCamera(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # Transformとcameraでindexed cubeを描く

        {RenderingCourseCatalog.Meta("Learn/Grapics/ThreeD/TransformsAndCamera", "Beginner+", "Standalone", "Vulkan / DirectX 12", "Textures")}

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


    [Story("Learn/Grapics/ThreeD/DepthCullingLighting", Order = 8)]
    public static Widget DepthCullingLighting(StoryContext ctx)
    {
        return DocNew(ctx, $"""
        # Depth、culling、方向光

        {RenderingCourseCatalog.Meta("Learn/Grapics/ThreeD/DepthCullingLighting", "Beginner+", "Standalone", "Vulkan / DirectX 12", "TransformsAndCamera")}

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

        textureがsRGB authoredなら[Textures](story:Learn/Grapics/ThreeD/Textures)のdecode後にLambertを掛けます。ambientは完全な黒面を避ける小値で、specular、shadow、gamma-correct outputはこの章の範囲外です。normalはmodel変換後のworld-space、lightもworld-spaceに統一します。dotの符号が逆なら「lightが進む方向」と「surfaceからlightへの方向」を混同しています。

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


    [Story("Learn/Grapics/ThreeD/FirstRenderGraph", Order = 10)]
    public static Widget FirstRenderGraph(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # はじめてのRenderGraph

        {{RenderingCourseCatalog.Meta("Learn/Grapics/ThreeD/FirstRenderGraph", "Beginner+", "Standalone + DevTools", "Vulkan / DirectX 12", "FrameLoopAndSynchronization")}}

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



    [Story("Learn/Grapics/ThreeD/StaticGltf", Order = 12)]
    public static Widget StaticGltf(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # ECSなしで静的glTFを描く

        {{RenderingCourseCatalog.Meta("Learn/Grapics/ThreeD/StaticGltf", "Beginner+", "Gallery / Standalone", "Vulkan / DirectX 12", "FirstRenderGraph")}}

        {{StoryRef(ctx, "Examples/3D/GltfBox")}}

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

        ここまでが静的1 primitiveです。scene graphと複数node/ECSは[Reference/Guides/Assets](story:Reference/Guides/Assets)、TRS animationは[GltfAnimated](story:Examples/3D/GltfAnimated)、skinは[GltfSkinned](story:Examples/3D/GltfSkinned)、morphは[GltfMorph](story:Examples/3D/GltfMorph)へ分けて進んでください。
        """, toc: true);
    }


    [Story("Learn/Grapics/ThreeD/Debugging", Order = 13)]
    public static Widget Debugging(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # レンダリングをデバッグする

        {{RenderingCourseCatalog.Meta("Learn/Grapics/ThreeD/Debugging", "Beginner", "Standalone / Gallery / DevTools", "Vulkan / DirectX 12", "StaticGltf")}}

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


    [Story("Learn/Grapics/ThreeD/Shipping", Order = 14)]
    public static Widget Shipping(StoryContext ctx)
    {
        return DocNew(ctx, $$"""
        # Publishして別ディレクトリから起動する

        {{RenderingCourseCatalog.Meta("Learn/Grapics/ThreeD/Shipping", "Beginner+", "Published standalone app", "Vulkan / DirectX 12", "Debugging")}}

        このページが初心者経路の終点です。次は3D capstoneの[Apps/Game/Range](story:Apps/Game/Range)へ進めます。

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
