using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>glTFのインポート、依存、診断、ランタイム、寿命を学ぶコース。</summary>
public static class LearnResourceGltf
{
    [Story("Learn/Resources/Gltf/Overview", Order = 15, Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/Overview", $$"""
        # glTFの概要

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/Overview", "初級", "ツール / ヘッドレス / ランタイム", "Luxel.Assets.Gltf", "アセットのシェーダーABI")}}

        `Luxel.Assets.Gltf`は`.gltf`のJSONと`.glb`コンテナーを解析し、形式非依存の`AssetDocument`へ変換します。インポーターはバッファ、画像、アクセサー、ノード関係、スキン、アニメーションを解決しますが、GPUデバイスの作成やシーンの描画は行いません。

        ```text
        .gltf/.glb + 外部バッファ / 画像
          → GltfResourceStep
          → AssetDocument
          → 検査、検証、個別アセットのGPU転送、ECSシーン構築
        ```

        このコースでは、登録と読み込み、外部依存の解決、診断、ランタイム展開、変形、再読み込み時の寿命を分けて説明します。`SceneAssets`や描画処理を追加する前に、まずCPUドキュメントの読み込みから始めてください。
        """);

    [Story("Learn/Resources/Gltf/RegistrationAndLoading", Order = 16, Toc = true)]
    public static StoryResult RegistrationAndLoading(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/RegistrationAndLoading", $$"""
        # glTFの登録と読み込み

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/RegistrationAndLoading", "初級", "ツール / ヘッドレス / ランタイム", "Resources + Assets.Gltf", "glTFの概要")}}

        インポーターは明示的に登録します。`ResourceSystem`はアセンブリを自動走査しません。ジェネリックオーバーロードはリフレクションを使わないため、ブラウザーやトリミングされたホストでも利用できます。

        ```csharp
        resources.AddStep<byte[], AssetDocument>(new GltfResourceStep());
        using ResourceHandle<AssetDocument> document =
            resources.Load<AssetDocument>("models/Box.glb");
        await document.Ready;
        if (!document.HasValue) throw document.Error!;
        ```

        `.gltf`と`.glb`はStepが宣言する拡張子で選択されます。読み込みに成功したハンドルが保持するのはCPUオブジェクトだけです。1つのプリミティブをGPUへ転送するか、完全なランタイムシーンを構築するかを決める前に、`Scenes`、`Nodes`、`Meshes`を調べてください。

        Resourceを使わない直接的なツール処理では、`GltfParser`、デコーダー、バリデーター、コンバーターを組み合わせた低水準経路も利用できます。URI依存、キャッシュ、再読み込み、所有権が必要な場合は`GltfResourceStep`を優先してください。
        """);

    [Story("Learn/Resources/Gltf/ExternalBuffersImagesAndUris", Order = 17, Toc = true)]
    public static StoryResult ExternalBuffersImagesAndUris(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/ExternalBuffersImagesAndUris", $$"""
        # 外部バッファ、画像、URI

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/ExternalBuffersImagesAndUris", "中級", "ファイル / HTTP / ワークスペース", "Resourcesの依存DAG", "glTFの登録と読み込み")}}

        JSON形式のglTFは、隣接する`.bin`や画像ファイルを参照できます。各参照は`ResourceUri.Resolve`でドキュメントURIから解決し、インポーター内で生のファイルシステムパスを連結しないでください。これにより`file`、`http`、`https`、`workspace`の各スキームと、正規化された相対パスを同じ規則で扱えます。

        `GltfResourceStep`は`LoadContext.Load<byte[]>()`で外部バイト列を読み込みます。返された各ハンドルは依存辺になるため、同じ外部バッファは共有され、その更新からドキュメントを再読み込みできます。データURIとGLB内のバッファチャンクはコンテナーから直接復号されるため、外部Sourceは不要です。

        ```text
        scene.gltfのノード
          ├─ geometry.binのbyte[]ノードに依存
          └─ albedo.pngのbyte[]ノードに依存
        ```

        参照される各スキームのSourceは読み込み前に登録してください。HTTPの相対参照では、基準ドキュメントURIのホスト情報が維持されます。
        """);

    [Story("Learn/Resources/Gltf/ValidationAndDiagnostics", Order = 18, Toc = true)]
    public static StoryResult ValidationAndDiagnostics(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/ValidationAndDiagnostics", $$"""
        # 検証と診断

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/ValidationAndDiagnostics", "中級", "インポートツール / CI", "GltfValidator + デコーダー", "外部バッファ、画像、URI")}}

        安全でないアクセサー読み取りがアセット配列になる前に検証を行います。バッファビューの範囲、アクセサーの成分型と要素型、バイトオフセットとストライド、スパースデータ、インデックス範囲、画像データ、参照先インデックスを確認してください。診断には一般的な解析失敗だけでなく、失敗したセマンティクスやインデックスも含めます。

        インポーターの失敗はResourceの失敗として扱います。初回読み込みの失敗ではハンドルが`Failed`になり、再読み込みの失敗では直前の正常な`AssetDocument`を保持したまま`LastReloadError`を記録します。呼び出し側は、まだ有効なシーンを破棄せずに診断を表示できます。

        CIでは代表的な`.gltf`と`.glb`をヘッドレスで読み込み、ドキュメント内の要素数と不正アクセサーのエラーを検証します。描画テストも必要ですが、不正なバイナリ配置を最初に検出する場所にはしないでください。
        """);

    [Story("Learn/Resources/Gltf/SceneRuntime", Order = 19, Toc = true)]
    public static StoryResult SceneRuntime(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/SceneRuntime", $$"""
        # glTFシーンのランタイム

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/SceneRuntime", "中級", "ECS / GPUランタイム", "AssetRuntime", "検証と診断")}}

        `SceneBuilder.Build(world, document, device)`はドキュメントのノードをECSエンティティへ展開し、GPU状態を持つ`SceneAssets`を作ります。Resources経由では、`AssetDocument → SceneAssets`の`SceneAssetsResourceStep`を登録します。これは`AssetGpuRegistry`で個別の`AssetMesh`をGPUへ転送する処理とは別です。

        `SceneAssets.NodeEntities`はCPUノードとエンティティの対応を保持します。ローカル変換を変更した後は`TransformPropagateSystem`を実行します。その後、`SceneRenderExtractor`または`DrawableCollector`がインスタンスデータを書き込み、インポート済みGPUバッファをRenderGraphへ渡します。

        ```text
        AssetDocument → SceneBuilder → エンティティ階層 + SceneAssets
                                      → 変換の伝播
                                      → インスタンス / マテリアル / プリミティブの抽出
                                      → 描画パス
        ```

        `SceneAssets`は、ランタイム処理が使うGPU状態と対応表を所有します。描画データの抽出と描画送信が終わるまで生存させてください。
        """);

    [Story("Learn/Resources/Gltf/AnimationSkinningAndMorph", Order = 20, Toc = true)]
    public static StoryResult AnimationSkinningAndMorph(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/AnimationSkinningAndMorph", $$"""
        # アニメーション、スキニング、モーフターゲット

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/AnimationSkinningAndMorph", "中級", "ゲームループ / ECS / GPU", "AssetRuntimeのシーンシェーダー", "glTFシーンのランタイム")}}

        各フレームでは、アニメーションチャンネルのサンプリング、変換の伝播、スキンのジョイント計算、モーフ・インスタンスバッファの更新を行ってから、描画データを抽出します。

        ```text
        1. SceneAnimationPlayer.Sample(time)
        2. TransformPropagateSystem.Run(world)
        3. SkinningSystem.Run(world, sceneAssets)
        4. ジョイント、モーフウェイト、インスタンスのRenderBufferを反映
        5. 描画データを抽出して描画
        ```

        移動と拡大縮小は線形補間またはステップ補間を使い、回転はクォータニオン補間またはステップ補間を使います。ウェイトチャンネルはモーフウェイトを更新します。現在のサンプラーはglTFの3次スプライン接線を完全には評価しません。

        ジョイントの順序は逆バインド行列の順序と一致し、頂点の`Joints0`はその一覧を参照します。モーフバッファはターゲット単位、次に頂点単位で差分を保持し、シェーダーが重み付き差分を加算します。スキンデータには56バイトのスキニング頂点シェーダーを、モーフバッファにはモーフ用の形式を選びます。1つの汎用シェーダーがすべての機能を組み合わせる前提ではありません。
        """);

    [Story("Learn/Resources/Gltf/ReloadAndLifetime", Order = 21, Toc = true)]
    public static StoryResult ReloadAndLifetime(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Gltf/ReloadAndLifetime", $$"""
        # glTFの再読み込みと寿命

        {{ResourceCourseCatalog.Meta("Learn/Resources/Gltf/ReloadAndLifetime", "中級", "エディター / ゲームループ", "Resources + AssetRuntime", "アニメーション、スキニング、モーフターゲット")}}

        読み込み前に`Watch()`を有効にします。ルートドキュメント、または外部バッファ・画像のbyte[]ノードが変わると、依存する`AssetDocument`が再読み込みされます。そのドキュメントから作られた`SceneAssets`も依存DAGを通じて再作成されます。

        値の差し替えは`ResourceSystem.Pump()`で公開されます。インポートに失敗した場合は直前の正常なシーンを使い続け、`LastReloadError`を公開し、完全な新しいドキュメントとランタイム値が成功してから交換します。所有されている旧値は遅延破棄され、GPUを使う値は破棄前に登録済みのアイドル待機処理が必要です。

        安全な終了順序は、描画データの抽出と描画使用を止め、ランタイムのハンドルとスコープを破棄し、遅延破棄を反映し、GPUアセットのインストールとランタイム所有者を破棄し、最後にデバイスを破棄する順です。CPUドキュメントの保持だけでは`SceneAssets`の寿命は延びず、`SceneAssets`の保持だけでもデバイスの明示的な所有者にはなりません。
        """);
}
