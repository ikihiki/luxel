using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>Resources上でCPU assetを取得し、GPU/runtimeへ展開してshaderで利用する方法。</summary>
public static class LearnResourceAssets
{
    [Story("Learn/Resources/Assets/Overview", Order = 8, Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => $$"""
        # Assets overview

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/Overview", "Beginner", "Standalone / Gallery / Headless", "CPU assets / optional GPU", "Resources reload and lifetime")}}

        AssetsはResourcesが管理する**値の種類**です。`Luxel.Assets`はファイル形式やGPU APIに依存しないCPU表現を定義し、`Luxel.Assets.Gltf`などのloaderがその値を作ります。`Luxel.AssetsGpu`と`Luxel.AssetRuntime`は、CPU assetをGPU resourceやECS runtimeへ展開します。

        ```text
        URI
         └─ ResourceSystem / Source / Step
             └─ AssetDocument（CPU、形式非依存）
                 ├─ AssetMesh / AssetMaterial / AssetTexture / AssetSkin ...
                 ├─ AssetsGpu → GpuMesh / GpuMaterial / GpuTexture / GpuSkin
                 └─ AssetRuntime → ECS entity / instance・joint・morph buffer
                                      └─ shaderがbindless indexで読む
        ```

        ## パッケージの役割

        | パッケージ | 役割 |
        | --- | --- |
        | `Luxel.Assets` | mesh、material、texture、scene graph、animationなどの純CPUデータ |
        | `Luxel.Assets.Gltf` | `.gltf` / `.glb`を`AssetDocument`へ変換するStepとloader |
        | `Luxel.AssetsGpu` | `Asset* → Gpu*` upload、dedup registry、material buffer |
        | `Luxel.AssetRuntime` | sceneをECSへ展開し、animation、skinning、render extractionを実行 |
        | `Luxel.Resources` | URI、cache、依存DAG、reload、ownershipを管理 |

        `AssetDocument`自体はResourcesにもGPUにも依存しません。そのためimport tool、validator、headless testはCPU assetだけを扱えます。アプリでURIから共有・reloadしたいときにResourcesを組み合わせ、描画するときだけGPU/runtime層を追加します。

        ## Assetsサブカテゴリの学習ルート

        1. [TypesAndRelationships](story:Learn/Resources/Assets/TypesAndRelationships) — どのようなassetがあり、どう参照し合うか
        2. [LoadingAndGpu](story:Learn/Resources/Assets/LoadingAndGpu) — URIから取得し、GPUへuploadする方法
        3. [ShaderCalculations](story:Learn/Resources/Assets/ShaderCalculations) — buffer ABIとshader内の計算
        4. [GltfRuntime](story:Learn/Resources/Assets/GltfRuntime) — scene、animation、skinning、morphの更新順

        ## 2つの利用経路

        **個別asset経路**では、`AssetGpuRegistry`またはResources Stepを使って`AssetMesh → GpuMesh`のように変換します。独自sceneやprogrammatic assetに向きます。

        **glTF scene経路**では、`AssetDocument → SceneAssets`としてECS entityと描画用bufferをまとめて構築します。node hierarchy、animation、skin、morphを含むsceneに向きます。

        > [!IMPORTANT]
        > CPU asset、GPU mirror、runtime instanceは別の寿命を持ちます。GPU bufferやtextureを参照するdrawが残っている間に、registry、`SceneAssets`、resource handleを先に破棄しないでください。
        """;

    [Story("Learn/Resources/Assets/TypesAndRelationships", Order = 9, Toc = true)]
    public static StoryResult TypesAndRelationships(StoryContext ctx) => $$"""
        # Assetの種類と関係

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/TypesAndRelationships", "Beginner", "Tools / Headless / Runtime", "CPU asset model", "Assets overview")}}

        `AssetDocument`は同時にロードされたassetをまとめるコンテナです。各assetはindexではなく直接参照でつながっており、documentから取り出した後も単体で利用できます。

        ## Documentとscene graph

        | 型 | 内容 |
        | --- | --- |
        | `AssetDocument` | mesh、material、texture、sampler、skin、animation、camera、light、node、sceneの一覧 |
        | `AssetScene` | 複数のroot nodeを持つscene |
        | `AssetNode` | TRSまたはmatrix、children、mesh / skin / camera / light attachment |

        `AssetNode.LocalMatrix`は`Scale * Rotation * Translation`で作られ、`OverrideMatrix`がある場合はそちらを使います。`Children`がauthoritativeで、parentは保持しません。

        ## Meshとprimitive

        `AssetMesh`は複数の`AssetPrimitive`で構成されます。primitiveは原則1 draw callの単位で、頂点属性、optional index、material、morph targetを持ちます。

        | 頂点属性 | CPU表現 | 用途 |
        | --- | --- | --- |
        | position | `Vector3[] Positions` | 頂点位置。必須 |
        | normal | `Vector3[]? Normals` | lighting |
        | tangent | `Vector4[]? Tangents` | normal mapping、wはhandedness |
        | UV | `TexCoord0` / `TexCoord1` | texture sampling |
        | color | `Vector4[]? Color0` | vertex color |
        | joints | `ushort[]? Joints0` | skinのjoint index、1頂点4個 |
        | weights | `Vector4[]? Weights0` | joint weight |

        `AssetMorphTarget`は基準position / normal / tangentへのdeltaを持ちます。最終頂点はshaderまたはCPU側で、基準値へ各targetのdeltaを重み付き加算して求めます。

        ## Material、texture、sampler

        `AssetMaterial`の既定modelはPBR metallic-roughnessです。主な値は次です。

        - `BaseColorFactor`とoptional `BaseColorTexture`
        - `MetallicFactor`、`RoughnessFactor`とmetallic-roughness texture（B=metallic、G=roughness）
        - normal、occlusion、emissive textureと各factor
        - `Opaque` / `Mask` / `Blend`、`AlphaCutoff`、`DoubleSided`
        - `Unlit`または`CustomShaderId`によるcustom shader選択

        `AssetTexture`はdecode済みpixel、size、format、mip数、optional samplerを持ちます。`AssetSampler`はfilterとU/V wrapを持ちます。materialからtextureへは`AssetTextureRef`で直接参照し、UV setとoffset / rotation / scaleも指定できます。

        > [!NOTE]
        > asset modelが保持できる値と、現在の標準shaderが計算する値は同じ範囲ではありません。現在の`scene_pbr_tex`はbase color textureと簡易diffuse lightingまでです。metallic、roughness、normal、occlusion、emissiveの全PBR評価を行うには対応shaderとGPU ABIの拡張が必要です。

        ## Skin、animation、camera、light

        - `AssetSkin`: joint nodeの直接参照、同順のinverse bind matrix、optional skeleton root。
        - `AssetAnimation`: nodeとpathごとのchannel。translation、rotation、scale、morph weightsを駆動。
        - `AssetCamera`: perspective / orthographicの投影parameter。
        - `AssetLight`: directional / point / spot、linear RGB、強度、range、spot cone。

        cameraとlightもCPU assetには含まれますが、rendererがどのbufferへencodeし、どのshader式で評価するかは描画実装側の責務です。
        """;

    [Story("Learn/Resources/Assets/LoadingAndGpu", Order = 10, Toc = true)]
    public static StoryResult LoadingAndGpu(StoryContext ctx) => $$"""
        # AssetをロードしてGPUで使う

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/LoadingAndGpu", "Intermediate", "Standalone / Browser / Game", "Resources + AssetsGpu", "Asset types")}}

        ## CPU assetを取得する

        glTFをResourcesから取得するには、Sourceに加えて`GltfResourceStep`を登録します。外部`.bin`やimageを参照するglTFでは、Step内の`LoadContext.Load()`によって別URIのdependencyがDAGへ接続されます。

        ```csharp
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());

        using ResourceHandle<AssetDocument> document =
            resources.Load<AssetDocument>("models/scene.glb");
        await document.Ready;
        AssetMesh firstMesh = document.Value.Meshes[0];
        ```

        `AssetDocument`から必要なmesh、material、animationを直接参照できます。CPU検査だけならGPU deviceは不要です。

        ## AssetsGpuのStepを登録する

        `InstallAssetGpuLifecycle()`はdevice-boundな`AssetGpuRegistry`を作り、次のようなgeneric Stepを登録し、deferred dispose前のqueue idle hookも設定します。

        ```text
        AssetTexture  → GpuTexture
        AssetSampler  → GpuSampler
        AssetMaterial → GpuMaterial
        AssetMesh     → GpuMesh
        AssetSkin     → GpuSkin
        ```

        ```csharp
        using AssetGpuInstallation gpuAssets =
            resources.InstallAssetGpuLifecycle(device);
        ```

        lifecycle tokenはdeviceより先にDisposeします。registryは同じCPU asset objectのuploadをdeduplicateし、material登録時にはtextureとsampler、mesh登録時にはprimitiveのmaterialを再帰登録します。

        ## Program valueをGPU assetへ変換する

        既にCPU側で持っているassetは`ResourceScope.Create<TInput,TOutput>()`でBorrowed inputとして登録し、AssetsGpu Stepへ渡せます。

        ```csharp
        using ResourceScope scope = resources.CreateScope("scene/main");
        ResourceHandle<GpuMesh> mesh =
            scope.Create<AssetMesh, GpuMesh>("mesh/player", cpuMesh);
        await mesh.Ready;
        ```

        `GpuMesh`はprimitiveごとのvertex / index bufferを持ち、`GpuMaterial`はGPU texture / samplerへの借用参照を持ちます。`GpuSkin`は毎フレーム更新可能なjoint matrixの`RenderBuffer<Matrix4x4>`を持ちます。

        ## glTF sceneをruntimeへ展開する

        scene graph全体を使う場合は`SceneAssetsResourceStep(device, world)`を登録し、`AssetDocument → SceneAssets`へ変換します。fragmentで部分bufferを取得する場合は`SceneBufferStep`と`SceneMaterialTextureStep`も明示登録します。

        ```csharp
        resources.AddStep<AssetDocument, SceneAssets>(
            new SceneAssetsResourceStep(device, world));
        resources.AddStep<SceneAssets, GpuBuffer>(new SceneBufferStep(device));
        resources.AddStep<SceneAssets, GpuTexture>(new SceneMaterialTextureStep());
        ```

        `SceneAssets`はprimitive GPU state、material array、skin、node-to-entity mappingを保持します。これは汎用`AssetGpuRegistry`経路とは別のglTF runtime経路です。必要な出力型に応じて登録するStepを選び、両者が自動的に同じcacheを共有すると仮定しないでください。

        ## 描画へ渡す

        runtime entityへ`DrawMesh`、`DrawInstance`、`DrawMaterial`、必要なら`DrawSkinning`を付けると、`DrawableCollector.Collect(world, renderGraph)`がvertex、index、instance、material、jointのimport済みhandleを`DrawItem`として返します。draw loopはhandleから実bufferのbindless indexをroot argumentsへ設定します。

        ## 寿命の順序

        1. drawとRenderGraphの利用を終了する。
        2. scope / resource handle / `SceneAssets`を解放する。
        3. `AssetGpuInstallation`またはregistryを解放する。
        4. 最後に`GpuDevice`を破棄する。
        """;

    [Story("Learn/Resources/Assets/ShaderCalculations", Order = 11, Toc = true)]
    public static StoryResult ShaderCalculations(StoryContext ctx) => $$"""
        # Assetをshaderで計算する

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/ShaderCalculations", "Intermediate", "GPU runtime", "Slang / bindless buffers", "Loading and GPU upload")}}

        Assetをshaderで使うときは、CPU型をそのまま渡すのではなく、**固定strideのGPU ABIへencodeし、bindless indexをroot argumentsで渡します**。CPU structとshaderのoffsetが1 byteでもずれると、位置、法線、material indexが別の値として読まれます。

        ## 基本のbuffer ABI

        現在のscene shaderで使う主なlayoutは次です。

        | データ | Stride | Layout |
        | --- | ---: | --- |
        | 通常vertex | 32B | position 12 + normal 12 + UV 8 |
        | skinned vertex | 56B | 通常vertex 32 + packed joints 8 + weights 16 |
        | instance | 80B | world matrix 64 + base color 16、またはmaterial index 4 + padding |
        | material | 32B | base color 16 + texture index 4 + sampler index 4 + flags 4 + padding 4 |
        | joint matrix | 64B | row-major `Matrix4x4` |
        | morph delta | 24B | delta position 12 + delta normal 12 |

        `GpuAssetFactory.Vertex` / `SkinnedVertex`、`SceneInstanceData`、`MaterialGpuData`、`MorphDelta`がCPU側の対応型です。

        ## Vertex indexと頂点属性を読む

        Luxelのscene shaderは`DrawIndexed`ではなく、index bufferもbindless bufferとしてvertex shaderから読みます。`indexBufIndex == 0xFFFFFFFF`ならnon-indexedです。

        ```c
        uint actualVid = indexBufIndex == 0xFFFFFFFF
            ? vertexId
            : buffers[indexBufIndex].Load<uint>(vertexId * 4);

        uint address = actualVid * 32;
        float3 position = asfloat(buffers[vertexBufIndex].Load3(address + 0));
        float3 normal   = asfloat(buffers[vertexBufIndex].Load3(address + 12));
        float2 uv       = asfloat(buffers[vertexBufIndex].Load2(address + 24));
        ```

        instance bufferからworld matrixを読み、row-vector規約でworld、view-projectionの順に変換します。

        ```c
        worldPosition = mul(float4(position, 1), world);
        clipPosition  = mul(worldPosition, viewProjection);
        worldNormal   = normalize(mul(normal, (float3x3)world));
        ```

        現在の標準shaderは一般のnon-uniform scaleに対するinverse-transpose normal matrixを作っていません。non-uniform scaleで正確な法線が必要なら、normal matrixを別途encodeするかshaderで計算する設計が必要です。

        ## Materialとtextureを読む

        instanceが持つ`materialIndex`から32B strideのmaterial bufferを引きます。`MaterialGpuData.FlagHasTexture`が立っている場合だけbindless textureとsamplerを使います。

        ```c
        materialAddress = materialIndex * 32;
        baseColor = materialBuffer.Load4(materialAddress + 0);
        textureIndex = materialBuffer.Load(materialAddress + 16);
        samplerIndex = materialBuffer.Load(materialAddress + 20);
        flags = materialBuffer.Load(materialAddress + 24);

        albedo = baseColor.rgb;
        if ((flags & 1) != 0)
            albedo *= textures[textureIndex].Sample(samplers[samplerIndex], uv).rgb;
        ```

        `GpuMaterial.ToShaderData()`が`BaseColorFactor`、`GpuTexture.BindlessIndex`、`GpuSampler.BindlessIndex`、flagを`MaterialGpuData`へencodeします。

        ## 現在のlighting計算

        `scene_pbr_lite`と`scene_pbr_tex`のpixel shaderはfull PBRではなく、固定directional lightによるPBR-liteです。

        ```text
        L       = normalize((0.6, 0.85, 0.4))
        NdotL   = saturate(dot(normalize(N), L))
        result  = albedo * (ambient + diffuseScale * NdotL)
        ```

        lite variantは`ambient = 0.25`、texture variantは概ね`0.30`です。`AssetMaterial`がmetallic、roughness、normal、occlusion、emissiveを保持していても、この標準material ABIとshaderはまだそれらを評価しません。full metallic-roughnessを実装する場合は、material bufferへfactorと各texture indexを追加し、normal mapping、BRDF、light/camera dataを同じABI規約で読む必要があります。

        ## Skinning計算

        CPUの`SkinningSystem`はrow-vector規約に合わせ、jointごとに次を計算します。

        ```text
        jointMatrix[j] = inverseBindMatrix[j] * jointWorld[j]
        ```

        vertex shaderはpackedされた4つのjoint indexを展開し、weight付き行列和を作ります。

        ```text
        skinMatrix = w0 * J[j0] + w1 * J[j1] + w2 * J[j2] + w3 * J[j3]
        skinnedPosition = position * skinMatrix
        skinnedNormal   = normal * (float3x3)skinMatrix
        ```

        weightは通常合計1を前提とします。import時に正規化されていないデータを許可する場合は、CPUまたはshaderでnormalizeする方針を決めます。

        ## Morph target計算

        morph bufferは`[target][vertex]`順の`MorphDelta`配列、weight bufferはtargetごとのfloat配列です。vertex shaderは各targetを走査します。

        ```text
        position += Σ weight[target] * deltaPosition[target, vertex]
        normal   += Σ weight[target] * deltaNormal[target, vertex]
        ```

        morph後のnormalはworld変換後にnormalizeします。target数に比例してvertex shaderの仕事量が増えるため、多数targetを同時に使う場合はactive targetの圧縮やcompute pre-deformationも検討します。

        ## ABIを変更するときのチェック

        - C# structの`StructLayout`、field順、strideをshaderと一致させる。
        - root argumentのfield順とpaddingを全backendで一致させる。
        - bindless buffer / texture / sampler indexの種類を取り違えない。
        - shader variantと`GpuPrimitive.VertexStride` / `HasSkinning`を一致させる。
        - material機能を増やしたらCPU encodeとshader decodeを同時に変更する。
        """;

    [Story("Learn/Resources/Assets/GltfRuntime", Order = 12, Toc = true)]
    public static StoryResult GltfRuntime(StoryContext ctx) => $$"""
        # glTF scene、animation、deformation

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/GltfRuntime", "Intermediate", "Game loop / ECS / GPU", "AssetRuntime + scene shaders", "Shader calculations")}}

        glTFのbuffer、image、node、skin、animationは`AssetDocument`内のCPU decode結果です。`SceneBuilder.Build(world, document, device)`または`SceneAssetsResourceStep`がGPU stateを作り、nodeをECS entityへ展開します。

        ## Scene graphをECSへ展開する

        `SceneAssets.NodeEntities`は`AssetNode → Entity`を保持します。各nodeのlocal transformをentityへ書き、`TransformPropagateSystem.Run(world)`が親子関係からglobal transformを計算します。描画前にtransform伝播を完了させます。

        `SceneRenderExtractor`はmesh entityをprimitiveごとにまとめ、`SceneInstanceData`のworld matrixとbase colorを`RenderBuffer`へ書きます。`DrawList`の`InstanceStart` / `InstanceCount`をroot argumentsとdraw countへ反映します。

        ## Animationの更新順

        `SceneAnimationPlayer.Sample(time)`は各channelを評価します。

        - translation / scale: linear interpolationまたはstep
        - rotation: quaternion slerpまたはstep
        - weights: targetごとのlinear interpolationまたはstep

        推奨するframe順は次です。

        ```text
        1. animation.Sample(time)
        2. TransformPropagateSystem.Run(world)
        3. SkinningSystem.Run(world, sceneAssets)
        4. joint / morph / instance RenderBufferを更新・flush
        5. DrawableCollectorまたはSceneRenderExtractorでdraw dataを抽出
        6. RenderGraphを実行
        7. resources.Pump()
        ```

        hostのphase設計に応じて`Pump()`位置は変えられますが、描画が読むbufferの更新はdraw commandを組む前に完了させます。

        ## Skinning

        `AssetSkin.Joints`の順序と`InverseBindMatrices`の順序は一致します。頂点の`Joints0`はこのlistへのindexです。`SkinningSystem`はjoint entityのglobal transformから行列配列を作り、`DrawSkinning`のjoint bufferをshaderへ渡します。

        skinned shaderでは56B vertex layoutが必要です。`GpuPrimitive.HasSkinning`と`VertexStride`を見て`scene_pbr_skinned`相当のpipelineを選び、通常32B vertexのshaderで読まないようにします。

        ## Morph target

        `SceneBuilder`はprimitiveのmorph targetを24Bの`MorphDelta` bufferへ展開し、nodeの初期weightを`MorphWeights` componentへ置きます。animationのWeights channelはこのcomponentを更新します。

        現在のruntimeはWeights channelのlinear / step sampleに対応しています。shaderはweight bufferを読み、positionとnormal deltaを加算します。animation dataに`CubicSpline`が指定されても、現在のsampler実装は専用tangent評価を行わずlinear相当になるため、完全なglTF cubic spline対応が必要な場合は拡張してください。

        ## Materialとshader variant

        - textureなしの簡易描画: `scene_pbr_lite`
        - base color texture + material array: `scene_pbr_tex`
        - GPU skinning: `scene_pbr_skinned`
        - morph deformation: `scene_pbr_morph`

        skinningとmorphとtextureを同時に使う単一の万能variantではありません。primitiveの属性とmaterial機能に合わせてpipeline variantを選ぶか、必要な機能を統合したshaderを用意します。

        ## Reloadと所有権

        glTFや外部buffer/imageのreloadはResources DAGから`AssetDocument`、`SceneAssets`へ伝播できます。reload後のGPU差し替えは`Pump()`境界です。旧`SceneAssets`やGPU bufferはOwnedであればdeferred disposeされるため、GPU idle hookを設定し、draw中の値を即時破棄しないようにします。

        CPU documentを別用途でも保持する場合、`AssetDocument` handleとGPU/runtime handleをそれぞれ必要な期間保持します。CPU値を持っているだけではGPU mirrorのleaseにはなりません。
        """;
}
