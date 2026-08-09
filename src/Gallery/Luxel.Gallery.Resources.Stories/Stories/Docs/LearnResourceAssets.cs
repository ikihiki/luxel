using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>CPUアセットの型体系と、必要に応じて作成するGPU表現を学ぶコース。</summary>
public static class LearnResourceAssets
{
    [Story("Learn/Resources/Assets/Overview", Order = 8, Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/Overview", $$"""
        # アセットの概要

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/Overview", "初級", "ツール / ヘッドレス / ランタイム", "CPUアセット / 任意のGPU表現", "Resourcesの再読み込みと寿命")}}

        `Luxel.Assets`は、インポート後に使用する形式非依存のCPUモデルです。URIの読み込みやGPUデバイスの所有は行いません。`Luxel.Resources`がキャッシュと寿命を管理し、`Luxel.Assets.Gltf`などのインポートパッケージが`AssetDocument`を作り、必要に応じて`Luxel.AssetsGpu`が個々のCPUオブジェクトをデバイス上の表現へ変換します。

        ```text
        入力バイト列 → インポーター → AssetDocument（CPU）
                                       ├─ 検査 / 検証 / ツール
                                       ├─ AssetGpuRegistry → GpuMesh / GpuMaterial / GpuTexture / GpuSkin
                                       └─ AssetRuntime → ECSシーンと描画データ抽出
        ```

        ## 型体系の学習順

        1. [AssetDocumentとシーングラフ](story:Learn/Resources/Assets/DocumentAndSceneGraph)
        2. [メッシュとプリミティブ](story:Learn/Resources/Assets/MeshesAndPrimitives)
        3. [マテリアル、テクスチャ、サンプラー](story:Learn/Resources/Assets/MaterialsTexturesAndSamplers)
        4. [アニメーション、スキン、カメラ、ライト](story:Learn/Resources/Assets/AnimationSkinCameraAndLight)
        5. [アセットの読み込みとGPU表現](story:Learn/Resources/Assets/LoadingAndGpu)
        6. [シェーダーABI](story:Learn/Resources/Assets/ShaderAbi)

        インポート形式固有の動作は、別の[glTF学習コース](story:Learn/Resources/Gltf/Overview)で扱います。
        """);

    [Story("Learn/Resources/Assets/DocumentAndSceneGraph", Order = 9, Toc = true)]
    public static StoryResult DocumentAndSceneGraph(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/DocumentAndSceneGraph", $$"""
        # AssetDocumentとシーングラフ

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/DocumentAndSceneGraph", "初級", "ツール / ヘッドレス / ランタイム", "CPUアセットモデル", "アセットの概要")}}

        `AssetDocument`は、1回のインポートで生成されたメッシュ、マテリアル、テクスチャ、サンプラー、スキン、アニメーション、カメラ、ライト、ノード、シーンをまとめます。公開整数インデックスではなく、オブジェクトの直接参照で関係を表します。

        `AssetScene.Roots`はルートノードを選びます。各`AssetNode`は子ノードと、任意のメッシュ、スキン、カメラ、ライトを保持します。`LocalMatrix`は明示的な行列があればそれを使い、なければ拡大縮小、回転、移動を合成します。CPUモデルに親ポインターはないため、走査時に親の変換行列を引き継ぎます。

        ```csharp
        static void Visit(AssetNode node, Matrix4x4 parent)
        {
            Matrix4x4 world = node.LocalMatrix * parent;
            if (node.Mesh is { } mesh) Inspect(mesh, world);
            foreach (AssetNode child in node.Children) Visit(child, world);
        }
        ```

        参照先オブジェクトを保持する間は、所有元のドキュメントか別の明示的な所有者も生存させてください。これらは通常のCPU値であり、ドキュメントのハンドルがGPU表現の使用権を暗黙に保持することはありません。
        """);

    [Story("Learn/Resources/Assets/MeshesAndPrimitives", Order = 10, Toc = true)]
    public static StoryResult MeshesAndPrimitives(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/MeshesAndPrimitives", $$"""
        # メッシュとプリミティブ

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/MeshesAndPrimitives", "初級", "ツール / ランタイム", "CPUメッシュモデル", "AssetDocumentとシーングラフ")}}

        `AssetMesh.Primitives`はメッシュを描画単位へ分割します。`AssetPrimitive`は`AssetVertexBuffer`、任意のインデックス、任意のマテリアル、0個以上のモーフターゲットを所有します。位置は必須です。法線、接線、2組のUV、頂点色、ジョイント、ウェイトは任意ですが、存在する場合は頂点数が一致している必要があります。

        | 型 | 主なメンバー |
        | --- | --- |
        | `AssetVertexBuffer` | 位置、法線、接線、UV、頂点色、ジョイント、ウェイト |
        | `AssetPrimitive` | 頂点属性、インデックス、マテリアル、モーフターゲット、トポロジー情報 |
        | `AssetMorphTarget` | 位置 / 法線 / 接線の差分 |
        | `AssetAabb` | 検査やカリング入力に使うローカル境界 |

        GPUへ転送する前に、頂点属性の長さとインデックス範囲を検証してください。GPUファクトリーは利用可能な属性から、通常頂点用の32バイト配置かスキニング頂点用の56バイト配置を選びます。シェーダーの選択はそのストライドと一致させる必要があります。
        """);

    [Story("Learn/Resources/Assets/MaterialsTexturesAndSamplers", Order = 11, Toc = true)]
    public static StoryResult MaterialsTexturesAndSamplers(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/MaterialsTexturesAndSamplers", $$"""
        # マテリアル、テクスチャ、サンプラー

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/MaterialsTexturesAndSamplers", "初級", "ツール / ランタイム", "CPUマテリアルモデル", "メッシュとプリミティブ")}}

        `AssetMaterial`はPBRのメタリック・ラフネス入力、アルファモード、両面描画、アンリットまたは独自シェーダーの選択、テクスチャ参照を保持します。`AssetTexture`はデコード済み画素と形式情報を保持し、`AssetSampler`はフィルタリングとU/V方向のラップ方法を保持します。`AssetTextureRef`はマテリアルのスロットを、テクスチャ、UVセット、変換情報へ接続します。

        データモデルは現在の標準シーンシェーダーより広い情報を表現できます。現在のマテリアルGPUデータが扱うのは、基本色と1組のテクスチャ・サンプラーのバインドレスインデックスです。メタリック、ラフネス、法線、オクルージョン、エミッシブを描画へ反映するには、対応するABIとシェーダーの拡張が必要です。

        `AssetGpuRegistry`はCPUオブジェクトの同一性でアップロードを重複排除します。マテリアルを登録すると参照先のテクスチャとサンプラーも再帰的に登録されるため、それらの寿命はマテリアルのGPU表現より短くならないようにしてください。
        """);

    [Story("Learn/Resources/Assets/AnimationSkinCameraAndLight", Order = 12, Toc = true)]
    public static StoryResult AnimationSkinCameraAndLight(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/AnimationSkinCameraAndLight", $$"""
        # アニメーション、スキン、カメラ、ライト

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/AnimationSkinCameraAndLight", "中級", "ツール / ECSランタイム", "CPUアセットモデル", "マテリアル、テクスチャ、サンプラー")}}

        `AssetAnimation`は、移動、回転、拡大縮小、モーフウェイトに対するサンプラーとノード・パスのチャンネルを持ちます。`AssetSkin`はジョイントノードの参照、同じ順序の逆バインド行列、任意のスケルトンルートを保持します。`AssetCamera`は透視投影または正投影の値を、`AssetLight`は平行光源、点光源、スポット光源の値を保持します。

        これらの型はインポートされた意図を表すだけで、ワールドを自動更新しません。ランタイム側でアニメーションをサンプリングし、ノード変換を伝播し、ジョイント行列を計算し、モーフウェイトを更新してから描画データを抽出します。カメラとライトにも、描画側に対応したバッファ符号化が必要です。

        ```text
        アニメーションのサンプリング → ノードのローカル値 → 変換の伝播
                                                          ├─ スキンのジョイント行列
                                                          ├─ モーフウェイト
                                                          └─ カメラ / ライト / 描画データ抽出
        ```

        glTF固有の補間と変形処理は、[アニメーション、スキニング、モーフ](story:Learn/Resources/Gltf/AnimationSkinningAndMorph)で説明します。
        """);

    [Story("Learn/Resources/Assets/LoadingAndGpu", Order = 13, Toc = true)]
    public static StoryResult LoadingAndGpu(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/LoadingAndGpu", $$"""
        # アセットの読み込みとGPU表現の作成

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/LoadingAndGpu", "中級", "単体アプリ / ブラウザー / ゲーム", "Resources + AssetsGpu", "アニメーション、スキン、カメラ、ライト")}}

        インポーターはResource Stepとして登録します。glTFでは`GltfResourceStep`を登録して`AssetDocument`を読み込みます。プログラムで生成するアセットは、CPUオブジェクトを直接公開または作成します。

        ```csharp
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> document =
            resources.Load<AssetDocument>("models/scene.glb");
        await document.Ready;
        ```

        `InstallAssetGpuLifecycle(device)`はデバイスに結び付いた`AssetGpuRegistry`を作成し、`AssetTexture → GpuTexture`、`AssetSampler → GpuSampler`、`AssetMaterial → GpuMaterial`、`AssetMesh → GpuMesh`、`AssetSkin → GpuSkin`のStepと、遅延破棄用のアイドル待機処理を登録します。

        ```csharp
        using AssetGpuInstallation installation = resources.InstallAssetGpuLifecycle(device);
        using ResourceScope scope = resources.CreateScope("scene/main");
        ResourceHandle<GpuMesh> mesh =
            scope.Create<AssetMesh, GpuMesh>("player", cpuMesh);
        await mesh.Ready;
        ```

        終了時は、まず描画を止め、次にスコープとハンドル、インストールとレジストリ、最後にデバイスを破棄します。CPUハンドルとGPUハンドルは別々の使用権です。
        """);

    [Story("Learn/Resources/Assets/ShaderAbi", Order = 14, Toc = true)]
    public static StoryResult ShaderAbi(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Assets/ShaderAbi", $$"""
        # アセットのシェーダーABI

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/ShaderAbi", "中級", "GPUランタイム", "Slang / バインドレスリソース", "アセットの読み込みとGPU表現")}}

        CPUアセットの各型は、固定ストライドのGPUレコードへ符号化されます。C#のフィールド順、構造体のパッキング、シェーダー側のオフセット、選択する頂点形式は、必ず同時に変更してください。

        | レコード | 現在のストライド |
        | --- | ---: |
        | 通常頂点 | 32バイト: 位置12、法線12、UV 8 |
        | スキニング頂点 | 56バイト: 通常頂点、圧縮ジョイント8、ウェイト16 |
        | シーンインスタンス | 80バイト |
        | `MaterialGpuData` | 32バイト |
        | ジョイント行列 | 64バイト |
        | モーフ差分 | 24バイト |

        シーンシェーダーはバインドレスインデックスで頂点バッファと任意のインデックスバッファを読みます。マテリアルデータは基本色、テクスチャインデックス、サンプラーインデックス、フラグを保持します。スキニングは4組のジョイントとウェイトを読み、モーフ処理はワールド変換前の位置と法線へ重み付き差分を加えます。

        ABIを拡張するときは、C#側の符号化、シェーダー側の復号、ストライドの検証、ルート引数、パイプライン形式を1つの変更として更新してください。`AssetMaterial`にフィールドを追加しただけでは、すべての層がその値を扱うまで描画結果は変わりません。
        """);
}
