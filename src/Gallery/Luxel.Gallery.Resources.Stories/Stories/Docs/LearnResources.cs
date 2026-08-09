using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>Resourcesの取得、変換、依存、所有権、再ロードを順番に学ぶコース。</summary>
public static class LearnResources
{
    [Story("Learn/Resources/Overview", Order = 0, SampleBundle = "resources.scenarios", Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Overview", $$"""
        # Resources学習ガイド

        {{ResourceCourseCatalog.Meta("Learn/Resources/Overview", "初級", "スタンドアロン / Gallery / ヘッドレス", "バックエンド非依存 / 外部Stepは任意", "なし")}}

        `ResourceSystem`はURIから値を読むだけのローダーではありません。要求した型とURIを単位にキャッシュノードを共有し、Sourceと型付きStepをつないで目的の型を作り、依存関係・再ロード・破棄まで管理します。

        ```text
        URI ── IResourceSource ──> byte[] ── Step ──> CPU値 ── Step ──> ランタイム / GPU値
                                                          │
                                                          └── ResourceHandle<T>
        ```

        ## このシステムが提供するもの

        キャッシュキーは概念上 **`(要求型, 正規化URI)`** です。同じ型・URIを複数箇所からロードすると同じノードを共有し、各`ResourceHandle<T>`が参照権を1つ持ちます。Stepの途中で作られる中間型もノードなので、変換結果の共有、依存先の再読み込み、退避を同じ仕組みで扱えます。

        | 機能 | 担当 |
        | --- | --- |
        | URIからバイト列を取得 | `IResourceSource` |
        | 1つの型を別の型へ変換 | `IResourceStep<TIn,TOut>` |
        | キャッシュ、パイプライン合成、DAG、再読み込み | `ResourceSystem` |
        | 安定参照と参照権 | `ResourceHandle<T>` |
        | 所有者単位の一括解放 | `ResourceScope` |

        ## 推奨学習ルート

        {{ResourceCourseCatalog.LearningRouteMarkdown()}}

        ## 他のシステムとの境界

        - **Luxel.Resources**: 多フレームにまたがる値の取得、変換、共有、再ロード、寿命管理。
        - **Luxel.Assets**: `AssetDocument`など、読み込み後に扱うアセット型。詳細は[Assetsサブカテゴリ](story:Learn/Resources/Assets/Overview)で扱います。
        - **Luxel.AssetsGpu**: CPU側の値からGPUリソースを作るStepと、その登録ヘルパー。
        - **RenderGraph**: 1フレーム内のパス / リソース依存。ResourcesのDAGとは寿命と目的が異なります。

        > [!IMPORTANT]
        > SourceとStepはアセンブリ走査で自動発見されません。組込みのSource/Stepも、`ResourceSystem`の構築時または`AddSource` / `AddStep`で明示登録します。

        > [!IMPORTANT]
        > 利用側は値だけを抜き出して終わりにせず、必要な期間`ResourceHandle<T>`を保持し、不要になったらDisposeします。再読み込み後の値差し替えや通知、遅延破棄には`Pump()`境界があります。
        """);

    [Story("Learn/Resources/LoadingAndHandles", Order = 1, Toc = true)]
    public static StoryResult LoadingAndHandles(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/LoadingAndHandles", $$"""
        # 読み込みとResourceHandle

        {{ResourceCourseCatalog.Meta("Learn/Resources/LoadingAndHandles", "初級", "スタンドアロン / Gallery / ヘッドレス", "バックエンド非依存", "Resourcesの概要")}}

        ## ResourceSystemを構築する

        `ResourceSystem`本体はSourceやStepを自動生成しません。必要なインスタンスを呼び出し側で組み立てます。

        ```csharp
        using var resources = new ResourceSystem(
            sources: ResourceSystemDefaults.BuiltinSources(assetRoot: "./assets"),
            steps: ResourceSystemDefaults.BuiltinSteps());
        ```

        `BuiltinSources()`は`FileSource`と`HttpSource`、`BuiltinSteps()`は`.tex`用の`TexDecoder`だけを返します。PNG/JPEG、glTF、shader、GPU uploadなどは、それぞれのパッケージが提供するStepを追加登録してください。

        ## LoadからValueを得るまで

        `Load<T>()`は`T`を直接返さず、安定参照である`ResourceHandle<T>`を返します。

        ```csharp
        using ResourceHandle<CpuImage> image = resources.Load<CpuImage>("panel.tex");
        await image.Ready;

        if (image.HasValue)
            Use(image.Value);
        else
            Report(image.Error);
        ```

        取得時は次の順に考えます。

        1. `Load<T>(uri)`で参照権を得る。
        2. `Ready`で現在の世代の完了を待つ。
        3. `Status`、`IsReady`、`HasValue`、`Error`を確認する。
        4. 正常値を`Value`から使う。
        5. 利用期間が終わったらハンドルをDisposeする。

        ## ハンドルが公開する状態

        | API | 意味 |
        | --- | --- |
        | `Ready` | 現在の世代のロード完了タスク |
        | `Status` | `Loading` / `Ready` / `Failed` |
        | `IsReady` | `Status == Ready` |
        | `HasValue` | 利用できる正常値を保持しているか |
        | `Value` | 現在の値。確認前は型の既定値になり得る |
        | `Error` / `LastReloadError` | 直近のロード失敗 |
        | `Version` | 再読み込み / 再公開で値が更新された回数 |
        | `Reloaded` | `Pump()`上で値の差し替え後に発火 |
        | `SubscribeState()` | `Pump()`スレッド上で状態遷移を購読 |

        再読み込みに失敗しても以前の正常値があれば`HasValue`はtrueのまま、`Status`は`Ready`を維持し、失敗は`LastReloadError`に残ります。表示中のassetを一時的な編集ミスで消さないための直近の正常値を維持する設計です。

        ## 初回読み込みとPump

        初回ロード成功時の`Value`はローダー完了時に設定されるため、`await handle.Ready`の後に利用できます。一方、再読み込み後の値の差し替え、`Reloaded`、状態通知、旧値の遅延破棄は`Pump()`で適用されます。

        ## 典型的な失敗

        - **型を生成するStepが未登録**: `Load<T>()`時に「型Tを生成するステップ未登録」になります。
        - **Readyになる前にValueを使う**: `Value`はdefaultの可能性があります。`Ready`または`HasValue`を境界にします。
        - **ハンドルを破棄しない**: 参照カウントが残り、ノードと依存先が退避されません。
        - **組込みに一般画像デコーダーがあると思う**: `BuiltinSteps()`は現在`TexDecoder`だけです。
        """);

    [Story("Learn/Resources/SourcesAndUris", Order = 2, Toc = true)]
    public static StoryResult SourcesAndUris(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/SourcesAndUris", $$"""
        # SourceとリソースURI

        {{ResourceCourseCatalog.Meta("Learn/Resources/SourcesAndUris", "初級", "スタンドアロン / ヘッドレス", "ファイル / HTTP / ワークスペース", "読み込みとハンドル")}}

        ## ResourceUriの構造

        `ResourceUri`は入力文字列をスキーム、パス、クエリ、拡張子、フラグメントへ分解します。スキーム省略時は`file`として扱われます。

        | 入力 | スキーム | パス | クエリ | 拡張子 | フラグメント |
        | --- | --- | --- | --- | --- | --- |
        | `textures/panel.tex` | `file` | `textures/panel.tex` | — | `.tex` | — |
        | `https://cdn.example/a.bin?v=2` | `https` | `cdn.example/a.bin` | `v=2` | `.bin` | — |
        | `shader.slang#compute` | `file` | `shader.slang` | — | `.slang` | `compute` |

        ノードのURIキーにはスキーム、パス、クエリ、フラグメントが含まれます。クエリやフラグメントが違えば別ノードです。拡張子はStep選択の手掛かりになります。

        ## Sourceの役割

        `IResourceSource`はスキームを担当し、URIから`byte[]`を供給する入口です。

        ```csharp
        public sealed class PackageSource : IResourceSource
        {
            public IEnumerable<string> Schemes => ["package"];

            public Task<byte[]> ReadAsync(ResourceUri uri, LoadContext ctx)
                => ReadPackageEntryAsync(uri.Path, ctx.Token);

            public IReloadToken? Watch(ResourceUri uri, Action onChanged)
                => null;
        }
        ```

        - `Schemes`: このSourceが処理するスキーム。
        - `ReadAsync()`: バイト列を返す。キャンセルには`ctx.Token`を使う。
        - `Watch()`: 変更監視できるSourceだけトークンを返す。必須ではありません。

        Sourceは画像のデコードやGPUへのアップロードを行いません。それらは型付きStepの責務です。

        ## 組込みSourceと仮想ファイルシステム

        | Source | スキーム | 特徴 |
        | --- | --- | --- |
        | `FileSource` | `file` / 既定 | `IVirtualFileSystem`経由。変更監視に対応 |
        | `HttpSource` | `http` / `https` | `HttpClient`で取得 |
        | `WorkspaceSource` | `workspace` | 共有`WorkspaceFileSystem`から取得・監視 |

        `FileSource`へ渡す`IVirtualFileSystem`は差し替え可能です。`PhysicalFileSystem`は実ファイル、`MemoryFileSystem`はテストや埋め込みデータ、`WorkspaceFileSystem`は編集可能なワークスペースに向きます。

        ## Sourceを登録する

        通常はResourceSystem構築時に配列へ含めます。実行中に追加する必要がある場合だけ`AddSource()`を使います。同じスキームへ後から登録すると、そのスキームのSourceは後の登録で置き換わるため、アプリの構成ルートで一度に構成する方が追跡しやすくなります。

        ## SourceとStepの責務を分ける

        | 判断 | Source | Step |
        | --- | --- | --- |
        | URIスキームを処理する | はい | いいえ |
        | バイト列を取得する | はい | 入力として受け取る |
        | `TIn → TOut`へ変換する | いいえ | はい |
        | 拡張子 / フラグメントで候補選択される | いいえ | はい |
        | 外部サービスをコンストラクターで受け取る | 必要なら | 必要なら |
        """);

    [Story("Learn/Resources/Steps", Order = 3, Toc = true)]
    public static StoryResult Steps(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Steps", $$"""
        # Resource Stepを作成する

        {{ResourceCourseCatalog.Meta("Learn/Resources/Steps", "中級", "スタンドアロン / ヘッドレス / ブラウザー", "I/O / CPU / 外部", "SourceとURI")}}

        Stepはローダー全体ではなく、**1つの入力型から1つの出力型への変換**です。小さな型付きの辺へ分けることで、中間結果の共有と依存先の再読み込みが可能になります。

        ```text
        byte[] → CpuImage → GpuTexture
        byte[] → SlangSource → GpuShaderCode
        ```

        ## IResourceStepの契約

        ```csharp
        public sealed class TextAssetStep : IResourceStep<byte[], TextAsset>
        {
            public Executor Executor => Executor.Cpu;
            public IEnumerable<string> Extensions => [".txt", ".json"];

            public Task<TextAsset> RunAsync(
                byte[] input, ResourceUri uri, LoadContext ctx)
            {
                string text = Encoding.UTF8.GetString(input);
                return Task.FromResult(new TextAsset(text));
            }
        }
        ```

        | メンバー | 役割 |
        | --- | --- |
        | `Executor` | Stepを実行するレーン |
        | `Extensions` | 同じ出力型を作る複数のStepから候補を選ぶ情報。省略可能 |
        | `FragmentPatterns` | フラグメント付きURIを担当するパターン。省略時はフラグメントなしを担当 |
        | `RunAsync()` | `TIn`を`TOut`へ変換する本体 |

        ## 実行レーン

        公開されているレーンは`Executor.Io`、`Executor.Cpu`、`Executor.External`です。ResourceSystemは`RunAsync()`を呼ぶ前に宣言されたレーンへ移動します。

        - `Io`: SourceやI/O待機中心の処理。
        - `Cpu`: デコード、解析、変換。
        - `External`: GPU、コンパイラー、ネイティブサービスなど外部実行環境との境界。GPU専用ではありません。

        `LoadContext`の`Io` / `Cpu` / `External` 待機可能オブジェクトは、Step内でさらに明示的な実行段階の移動が必要な場合に使います。すべてのStepが冒頭で移動処理を書く必要はありません。

        ## 拡張子とフラグメント

        フラグメントなしURIでは`FragmentPatterns == null`のStepが候補です。フラグメントがある場合は一致するパターンを持つStepだけが候補になり、`mesh/*`の末尾`*`は前方一致です。

        ```csharp
        public IEnumerable<string> FragmentPatterns => ["graphics", "compute"];
        ```

        `shader.slang#compute`をロードすると、出力Stepにはフラグメント付きURIが渡りますが、入力ノードはフラグメントを外した`shader.slang`を使います。元データを共有しながらセレクターごとの出力ノードを持てます。

        `Extensions`は同じ出力型の候補が複数ある場合の選択情報です。ファイル内容を検証する仕組みではないため、`RunAsync()`側でも入力の妥当性を扱ってください。

        ## 依存サービスとLoadContext

        GPUデバイスやコンパイラーなどのサービスは、ResourceSystemからサービスロケーターとして取得せず、Stepのコンストラクターへ渡します。

        ```csharp
        public sealed class UploadStep(GpuDevice device)
            : IResourceStep<CpuImage, GpuTexture>
        {
            public Executor Executor => Executor.External;

            public Task<GpuTexture> RunAsync(
                CpuImage input, ResourceUri uri, LoadContext ctx)
            {
                GpuTexture texture = Upload(device, input);
                ctx.MarkOwned();
                return Task.FromResult(texture);
            }
        }
        ```

        `LoadContext`はキャンセルトークン、依存ロード、既存ハンドルの要求、実行段階の移動、`MarkOwned()` / `MarkBorrowed()`を提供します。

        ## 新しいリソース型を追加する手順

        `PlayerStats`のような新しい型は、次の順で追加するとSource、パイプライン、寿命の境界を保てます。

        1. リソースの公開値型を定義する。値が`IDisposable`なら所有者も決める。
        2. 直前の入力型を選ぶ。ファイル形式なら通常は`byte[]`、共通の解析結果を再利用するならその中間型にする。
        3. `IResourceStep<TIn,TOut>`を実装し、レーン、拡張子、フラグメントを宣言する。
        4. 外部サービスはStepのコンストラクターへ渡し、`RunAsync`では`ctx.Token`と`ctx.Load` / `Require`を使う。
        5. 構成ルートでジェネリックな`AddStep<TIn,TOut>()`へ登録する。
        6. `Load<TOut>()`、`Ready`、状態 / エラー、Disposeまでを呼び出し側で扱う。
        7. 同URI共有、誤った入力、再読み込みの成功 / 失敗、Owned値の破棄をヘッドレステストで確認する。

        [PlayerStatsPipeline](story:Examples/Resources/PlayerStatsPipeline)は`byte[] → JsonDocument → PlayerStats`を実際に登録・ロードし、最終値を検証します。`JsonDocument`を中間型にしたため、別のJSONリソース型も解析ノードを共有できます。

        ## 典型的な失敗

        - StepがSourceの代わりにパスを直接開き、VFSや再読み込みを迂回する。
        - `External`を`Gpu`という公開enumだと思い込む。
        - アセンブリ走査やDI containerによる自動構築を期待する。
        - 同じ出力型のStepを無計画に複数登録し、選択を拡張子やフラグメントで区別しない。
        """);

    [Story("Learn/Resources/RegistrationAndComposition", Order = 4, Toc = true)]
    public static StoryResult RegistrationAndComposition(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/RegistrationAndComposition", $$"""
        # Stepの登録とパイプライン合成

        {{ResourceCourseCatalog.Meta("Learn/Resources/RegistrationAndComposition", "中級", "スタンドアロン / ブラウザー / AOT", "バックエンド非依存", "Resource Step")}}

        ## 構築時に登録する

        アプリの構成ルートで、必要なSourceとStepを構築してResourceSystemへ渡すのが基本です。Stepが外部サービスを必要とする場合もここでコンストラクター注入します。

        ```csharp
        var decoder = new ImageSharpDecoder();
        var uploader = new UploadStep(device);

        using var resources = new ResourceSystem(
            sources: ResourceSystemDefaults.BuiltinSources("./assets"),
            steps:
            [
                ..ResourceSystemDefaults.BuiltinSteps(),
                decoder,
                uploader,
            ]);
        ```

        組込みには一般画像、glTF、shader、GPU Stepは含まれません。利用するパッケージのStepまたはinstall helperを明示的に追加します。

        ## 実行中に追加する

        `AddSource()`と`AddStep()`でも追加できます。ただし既に作られたノードのパイプラインを組み直すAPIではないため、通常は最初の`Load<T>()`より前に登録を完了させます。

        ```csharp
        resources.AddSource(new WorkspaceSource(workspace));
        resources.AddStep(new TextAssetStep());
        ```

        トリミング / AOT / ブラウザーでは、リフレクションを使わず既定インターフェイスメンバーも到達可能にするジェネリックオーバーロードを優先します。

        ```csharp
        resources.AddStep<byte[], TextAsset>(new TextAssetStep());
        ```

        ## パイプラインは出力型から逆向きに作られる

        `Load<GpuTexture>(uri)`を呼ぶと、ResourceSystemは`GpuTexture`を出力するStepを選び、その入力型`CpuImage`を同じURIで再帰ロードします。さらに`CpuImage`を出力するStepを選び、最後に`byte[]`へ到達するとスキーム対応Sourceを使います。

        ```text
        要求されたGpuTexture
          ← UploadStep(CpuImage → GpuTexture)
          ← ImageDecoder(byte[] → CpuImage)
          ← FileSource(uri → byte[])
        ```

        ## Stepの選択規則

        1. 要求された出力型で候補を探す。
        2. フラグメントの有無と`FragmentPatterns`で候補を絞る。
        3. 候補が複数ならURIの拡張子一致を優先する。
        4. 拡張子指定のない汎用Stepを代替候補にする。
        5. スコープ内変換では要求された入力型でも絞る。

        > [!NOTE]
        > 自動合成は登録済みStepの全経路を探索して「最短チェーン」を選ぶ仕組みではありません。各出力型で選ばれたStepの入力型を再帰的に解決します。同じ出力型の候補は拡張子、フラグメント、入力型で意図が明確になるよう設計してください。

        ## 登録チェックリスト

        - 対象スキームのSourceがあるか。
        - 最終型から`byte[]`まで各出力型のStepがあるか。
        - 同じ出力型の候補を拡張子またはフラグメントで区別できるか。
        - Stepの外部依存をコンストラクターへ渡したか。
        - AOT環境ではジェネリックな`AddStep<TIn,TOut>()`を使っているか。
        """);

    [Story("Learn/Resources/PipelinesAndDag", Order = 5, Toc = true)]
    public static StoryResult PipelinesAndDag(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/PipelinesAndDag", $$"""
        # 型付きパイプラインと依存関係DAG

        {{ResourceCourseCatalog.Meta("Learn/Resources/PipelinesAndDag", "中級", "スタンドアロン / ヘッドレス", "バックエンド非依存", "登録と合成")}}

        パイプラインの各段は独立したキャッシュノードです。`byte[]`やデコード済みCPU値も同じ型・URIなら共有されるため、複数の最終リソースが同じ中間結果を再利用できます。

        ## チェーンが作る依存関係

        ```text
        GpuTexture panel.tex
          └─ 依存 CpuImage panel.tex
               └─ 依存 byte[] panel.tex
                    └─ FileSource
        ```

        依存関係の辺は単なる可視化情報ではありません。依存先が差し替わったときの依存元の再読み込み、ノードを残すべきかの判定、連鎖退避に使われます。

        ## 別URIをロードする

        StepがマニフェストやglTFのように別URIを参照する場合は`LoadContext.Load<T>()`を使います。返されたハンドルは現在のノードの依存先として接続されます。

        ```csharp
        using ResourceHandle<byte[]> buffer = ctx.Load<byte[]>(bufferUri);
        await buffer.Ready;
        return Parse(input, buffer.Value);
        ```

        `LoadContext.Load()`は「このStepがURIを知っていて、その場で依存を取得する」場合に使います。

        ## 既存ハンドルを依存にする

        呼び出し側などが既に持っているhandleを現在のノードの依存として使う場合は`Require()`です。

        ```csharp
        CpuImage image = await ctx.Require(imageHandle);
        ```

        `Require()`は同じ`ResourceSystem`のhandleだけを受け付け、依存関係の辺を追加して現在の世代の値を待ちます。

        | API | 使いどころ |
        | --- | --- |
        | `ctx.Load<T>(uri)` | Step自身が別URIを解決する |
        | `ctx.Require(handle)` | 注入済み・公開済みのhandleをDAGへ接続する |

        ## 再読み込みの伝播

        Sourceのファイル変更や`Republish()`でdependencyが更新されると、直接の依存元が再読み込みキューへ入り、その出力更新がさらに後段へ伝播します。中間ノードを共有している場合も、同じノードから各依存元へ伝播します。

        ## RenderGraphのDAGとの違い

        | Resources DAG | RenderGraph DAG |
        | --- | --- |
        | 型とURIのノード | パスとフレームリソース |
        | 多フレーム寿命 | 1フレーム内 |
        | 再読み込みと退避 | パス順序とバリア |
        | ハンドルが参照権を保持 | グラフのコンパイル / 実行で一時管理 |

        Resourcesで得た`GpuTexture`をRenderGraphへ外部リソースとしてインポートすることはできますが、2つのDAGを同じものとして扱わないでください。
        """);

    [Story("Learn/Resources/ScopesAndOwnership", Order = 6, Toc = true)]
    public static StoryResult ScopesAndOwnership(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/ScopesAndOwnership", $$"""
        # ResourceScopeと所有権

        {{ResourceCourseCatalog.Meta("Learn/Resources/ScopesAndOwnership", "中級", "ゲーム / エディター / Gallery", "バックエンド非依存 / GPUは任意", "パイプラインとDAG")}}

        ## ResourceScopeの役割

        `ResourceScope`はシーン、ドキュメント、Widgetなどの論理所有者が持つ複数の参照権をまとめます。スコープ経由で取得・作成したhandleは追跡され、スコープのDisposeで一括解放されます。

        ```csharp
        using ResourceScope scope = resources.CreateScope("scene/main");
        ResourceHandle<CpuImage> image = scope.Load<CpuImage>("panel.tex");
        ```

        `scope.Load()`は通常の共有URIをロードします。同じ型・URIはスコープ外のハンドルとも同じノードを共有します。

        ## スコープ内リソースを作る

        `scope.Create()`は所有者内のローカルキーを`scope://{owner}/{localKey}`へ修飾し、明示的なローダーで値を作ります。異なる所有者が同じローカルキーを使っても別ノードになります。

        ```csharp
        ResourceHandle<Descriptor> descriptor = scope.Create(
            "material/default",
            _ => Task.FromResult(CreateDescriptor()));
        ```

        `Create<TInput,TOutput>()`ではスコープ内入力をBorrowedノードとして登録し、登録済みStepを通してoutputを作れます。プログラム上の値やdescriptorからGPU/ランタイムオブジェクトを生成する用途に向きます。

        ## OwnedとBorrowed

        `ResourceOwnership`はキャッシュ値の破棄責任を表します。

        | 所有権 | 置換 / 退避時のDispose |
        | --- | --- |
        | `Owned` | `ResourceSystem`が行う |
        | `Borrowed` | 外部所有者が行う。ResourceSystemは破棄しない |

        `Publish(uri, value)`の既定は`Owned`です。外部所有値を登録する場合は必ず明示します。

        ```csharp
        ResourceHandle<GpuDevice> deviceHandle = resources.Publish(
            "runtime://device/main",
            device,
            ResourceOwnership.Borrowed);
        ```

        Stepの結果については`ctx.MarkOwned()` / `ctx.MarkBorrowed()`で指定できます。所有する`IDisposable`を返すStepは、誰が破棄するかを実装時に決めてください。

        > [!NOTE]
        > フラグメント付きパイプラインノードは元URIの値を切り出すセレクターとして扱われ、現在の自動合成ではBorrowedに設定されます。フラグメント出力が独立した`IDisposable`を所有する設計にする場合は、この挙動を前提にStepと所有者の責任を決めてください。

        ## Scopeを使う目安

        - シーンのアンロードで関連する参照権をまとめて解放したい。
        - エディタードキュメントごとに同名のローカルリソースを持ちたい。
        - プログラム上の値を入力として登録済みStepを再利用したい。
        - 個々のハンドル Dispose漏れを所有者境界で防ぎたい。
        """);

    [Story("Learn/Resources/ReloadAndLifetime", Order = 7, Toc = true)]
    public static StoryResult ReloadAndLifetime(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/ReloadAndLifetime", $$"""
        # 再読み込み、公開、寿命

        {{ResourceCourseCatalog.Meta("Learn/Resources/ReloadAndLifetime", "中級", "ゲームループ / 開発ツール / CI", "バックエンド非依存 / GPUは任意", "スコープと所有権")}}

        ## 変更監視は読み込み前に有効化する

        `Watch()`は自動再読み込みを有効にします。ノード作成時に監視トークンを登録するため、基本手順は最初の`Load<T>()`より前です。

        ```csharp
        resources.Watch();
        using ResourceHandle<CpuImage> image = resources.Load<CpuImage>("panel.tex");
        ```

        監視対象はSourceが読む`byte[]`ノードです。`FileSource`や`WorkspaceSource`の変更通知からバイト列が再読み込みされ、依存DAGを通して後段のStepへ伝播します。

        ## 再読み込みを開始する経路

        - `Watch()`後のSourceの変更通知。
        - `Republish(uri, value)`によるプログラム上の値の差し替え。
        - `InvalidateAll()`による現在の全キャッシュノードの無効化。
        - 依存先の更新から依存元への伝播。

        `Republish()`の差し替えは次の`Pump()`で適用されます。GPUデバイス消失など、全ノードを作り直したい場合は`InvalidateAll()`を使えます。

        ## 世代と直近の正常値

        ノードは読み込み世代を持ちます。新しい再読み込みが始まると前の処理をキャンセルし、古い世代が遅れて完了しても現在値として公開しません。重複した再読み込み要求もキュー上で集約されます。

        再読み込みに失敗した場合、初回から値がなければ`Failed`です。以前の正常値があればその値を維持し、`Status`は`Ready`、`LastReloadError`に失敗を記録します。次の成功でエラーは消去されます。

        ## Pump境界

        ゲームループやGalleryホストから`Pump()`を継続的に呼びます。

        ```csharp
        while (running)
        {
            PollEvents();
            resources.Pump();
            UpdateAndRender();
        }
        ```

        `Pump()`では主に次を処理します。

        1. 再読み込み後の値の差し替えと`Version`更新。
        2. `Reloaded`イベントと状態購読通知。
        3. 依存元の再読み込みの開始。
        4. 置換 / 退避で残した古いOwned値の遅延破棄。

        初回ロードの値は`Ready`完了時に利用できます。「すべてのロード結果がPumpまで見えない」わけではありません。

        ## 退避と遅延破棄

        ハンドルをDisposeして参照カウントが0になり、依存元もないノードは退避できます。ノードを消すと依存関係の辺を外し、同じ条件を満たした入力ノードへ連鎖退避します。

        Owned値の置換 / 退避時のDisposeは`Pump()`まで遅延されます。GPUなど外部実行環境の値を安全に破棄する前にアイドル待機が必要なら`SetDeferredDisposeIdleHook()`を設定します。

        ## 典型的な失敗

        - **Pumpを呼ばない**: 再読み込み処理が完了しても差し替え、イベント、遅延破棄が進みません。
        - **ロード後に初めてWatchを呼ぶ**: 既存ノードへ遡って監視を登録するAPIではありません。
        - **Ownedの外部値をPublishする**: 既定Ownedなので、外部所有者が破棄する値にはBorrowedを明示します。
        - **handleを保持しない**: 参照権がなくなるとノードを退避可能になり、利用期間とキャッシュの寿命が一致しません。
        """);
}
