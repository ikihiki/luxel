using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>Resourcesの取得、変換、依存、所有権、再ロードを順番に学ぶコース。</summary>
public static class LearnResources
{
    [Story("Learn/Resources/Overview", Order = 0, SampleBundle = "resources.scenarios", Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Overview", $$"""
        # Resources 学習ガイド

        {{ResourceCourseCatalog.Meta("Learn/Resources/Overview", "Beginner", "Standalone / Gallery / Headless", "Backend neutral / optional external steps", "なし")}}

        `ResourceSystem`はURIから値を読むだけのloaderではありません。要求した型とURIを単位にcache nodeを共有し、Sourceとtyped Stepをつないで目的の型を作り、依存関係・再ロード・破棄まで管理します。

        ```text
        URI ── IResourceSource ──> byte[] ── Step ──> CPU value ── Step ──> runtime / GPU value
                                                          │
                                                          └── ResourceHandle<T>
        ```

        ## このシステムが提供するもの

        cache keyは概念上 **`(requested type, normalized URI)`** です。同じ型・URIを複数箇所からロードすると同じnodeを共有し、各`ResourceHandle<T>`がleaseを1つ持ちます。Stepの途中で作られる中間型もnodeなので、変換結果の共有、dependency reload、evictionを同じ仕組みで扱えます。

        | 機能 | 担当 |
        | --- | --- |
        | URIからbytesを取得 | `IResourceSource` |
        | 1つの型を別の型へ変換 | `IResourceStep<TIn,TOut>` |
        | cache、pipeline合成、DAG、reload | `ResourceSystem` |
        | 安定参照とlease | `ResourceHandle<T>` |
        | owner単位の一括解放 | `ResourceScope` |

        ## 推奨学習ルート

        {{ResourceCourseCatalog.LearningRouteMarkdown()}}

        ## 他のシステムとの境界

        - **Luxel.Resources**: 多フレームにまたがる値の取得、変換、共有、再ロード、寿命管理。
        - **Luxel.Assets**: `AssetDocument`など、読み込み後に扱うアセット型。詳細は[Assetsサブカテゴリ](story:Learn/Resources/Assets/Overview)で扱います。
        - **Luxel.AssetsGpu**: CPU側の値からGPUリソースを作るStepと、その登録ヘルパ。
        - **RenderGraph**: 1フレーム内のpass/resource依存。ResourcesのDAGとは寿命と目的が異なります。

        > [!IMPORTANT]
        > SourceとStepはassembly scanで自動発見されません。組込みのSource/Stepも、`ResourceSystem`の構築時または`AddSource` / `AddStep`で明示登録します。

        > [!IMPORTANT]
        > 利用側は値だけを抜き出して終わりにせず、必要な期間`ResourceHandle<T>`を保持し、不要になったらDisposeします。reload後の値差し替えや通知、deferred disposeには`Pump()`境界があります。
        """);

    [Story("Learn/Resources/LoadingAndHandles", Order = 1, Toc = true)]
    public static StoryResult LoadingAndHandles(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/LoadingAndHandles", $$"""
        # Loading and ResourceHandle

        {{ResourceCourseCatalog.Meta("Learn/Resources/LoadingAndHandles", "Beginner", "Standalone / Gallery / Headless", "Backend neutral", "Resources overview")}}

        ## ResourceSystemを構築する

        `ResourceSystem`本体はSourceやStepを自動生成しません。必要なインスタンスを呼び出し側で組み立てます。

        ```csharp
        using var resources = new ResourceSystem(
            sources: ResourceSystemDefaults.BuiltinSources(assetRoot: "./assets"),
            steps: ResourceSystemDefaults.BuiltinSteps());
        ```

        `BuiltinSources()`は`FileSource`と`HttpSource`、`BuiltinSteps()`は`.tex`用の`TexDecoder`だけを返します。PNG/JPEG、glTF、shader、GPU uploadなどは、それぞれのパッケージが提供するStepを追加登録してください。

        ## LoadからValueまで

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

        1. `Load<T>(uri)`でleaseを得る。
        2. `Ready`で現在generationの完了を待つ。
        3. `Status`、`IsReady`、`HasValue`、`Error`を確認する。
        4. 正常値を`Value`から使う。
        5. 利用期間が終わったらhandleをDisposeする。

        ## Handleが公開する状態

        | API | 意味 |
        | --- | --- |
        | `Ready` | 現在generationのロード完了Task |
        | `Status` | `Loading` / `Ready` / `Failed` |
        | `IsReady` | `Status == Ready` |
        | `HasValue` | 利用できる正常値を保持しているか |
        | `Value` | 現在の値。確認前は型のdefaultになり得る |
        | `Error` / `LastReloadError` | 直近のロード失敗 |
        | `Version` | reload / republishで値が更新された回数 |
        | `Reloaded` | `Pump()`上で値の差し替え後に発火 |
        | `SubscribeState()` | `Pump()` thread上で状態遷移を購読 |

        reloadに失敗しても以前の正常値があれば`HasValue`はtrueのまま、`Status`はReadyを維持し、失敗は`LastReloadError`に残ります。表示中のassetを一時的な編集ミスで消さないためのlast-good-value設計です。

        ## 初回ロードとPump

        初回ロード成功時の`Value`はloader完了時に設定されるため、`await handle.Ready`の後に利用できます。一方、reload後のvalue swap、`Reloaded`、state通知、旧値の遅延破棄は`Pump()`で適用されます。

        ## 典型的な失敗

        - **型を作るStepが未登録**: `Load<T>()`時に「型Tを生成するステップ未登録」になります。
        - **Ready前にValueを使う**: `Value`はdefaultの可能性があります。`Ready`または`HasValue`を境界にします。
        - **handleを破棄しない**: refcountが残り、nodeと依存がevictされません。
        - **組込みに一般画像decoderがあると思う**: `BuiltinSteps()`は現在`TexDecoder`だけです。
        """);

    [Story("Learn/Resources/SourcesAndUris", Order = 2, Toc = true)]
    public static StoryResult SourcesAndUris(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/SourcesAndUris", $$"""
        # Sources and resource URIs

        {{ResourceCourseCatalog.Meta("Learn/Resources/SourcesAndUris", "Beginner", "Standalone / Headless", "File / HTTP / workspace", "Loading and handles")}}

        ## ResourceUriの構造

        `ResourceUri`は入力文字列をscheme、path、query、extension、fragmentへ分解します。scheme省略時は`file`として扱われます。

        | 入力 | Scheme | Path | Query | Extension | Fragment |
        | --- | --- | --- | --- | --- | --- |
        | `textures/panel.tex` | `file` | `textures/panel.tex` | — | `.tex` | — |
        | `https://cdn.example/a.bin?v=2` | `https` | `cdn.example/a.bin` | `v=2` | `.bin` | — |
        | `shader.slang#compute` | `file` | `shader.slang` | — | `.slang` | `compute` |

        nodeのURI keyにはscheme、path、query、fragmentが含まれます。queryやfragmentが違えば別nodeです。extensionはStep選択の手掛かりになります。

        ## Sourceの役割

        `IResourceSource`はschemeを担当し、URIから`byte[]`を供給する入口です。

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

        - `Schemes`: このSourceが処理するscheme。
        - `ReadAsync()`: bytesを返す。cancelには`ctx.Token`を使う。
        - `Watch()`: 変更監視できるSourceだけtokenを返す。必須ではありません。

        Sourceは画像decodeやGPU uploadを行いません。それらはtyped Stepの責務です。

        ## 組込みSourceとVFS

        | Source | Scheme | 特徴 |
        | --- | --- | --- |
        | `FileSource` | `file` / 既定 | `IVirtualFileSystem`経由。変更監視に対応 |
        | `HttpSource` | `http` / `https` | `HttpClient`で取得 |
        | `WorkspaceSource` | `workspace` | 共有`WorkspaceFileSystem`から取得・監視 |

        `FileSource`へ渡す`IVirtualFileSystem`は差し替え可能です。`PhysicalFileSystem`は実ファイル、`MemoryFileSystem`はtestや埋め込みデータ、`WorkspaceFileSystem`は編集可能なworkspaceに向きます。

        ## Sourceを登録する

        通常はResourceSystem構築時に配列へ含めます。実行中に追加する必要がある場合だけ`AddSource()`を使います。同じschemeへ後から登録すると、そのschemeのSourceは後の登録で置き換わるため、アプリのcomposition rootで一度に構成する方が追跡しやすくなります。

        ## SourceとStepの責務を分ける

        | 判断 | Source | Step |
        | --- | --- | --- |
        | URI schemeを処理する | Yes | No |
        | bytesを取得する | Yes | 入力として受け取る |
        | `TIn → TOut`へ変換する | No | Yes |
        | extension / fragmentで候補選択される | No | Yes |
        | 外部serviceをctorで受け取る | 必要なら | 必要なら |
        """);

    [Story("Learn/Resources/Steps", Order = 3, Toc = true)]
    public static StoryResult Steps(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/Steps", $$"""
        # Resource Stepを作る

        {{ResourceCourseCatalog.Meta("Learn/Resources/Steps", "Intermediate", "Standalone / Headless / Browser", "Io / Cpu / External", "Sources and URIs")}}

        Stepはloader全体ではなく、**1つの入力型から1つの出力型への変換**です。小さなtyped edgeへ分けることで、中間結果の共有と依存reloadが可能になります。

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

        | Member | 役割 |
        | --- | --- |
        | `Executor` | Stepを実行するlane |
        | `Extensions` | 同じ出力型を作る複数Stepから候補を選ぶ情報。省略可能 |
        | `FragmentPatterns` | fragment付きURIを担当するpattern。省略時はfragmentなしを担当 |
        | `RunAsync()` | `TIn`を`TOut`へ変換する本体 |

        ## Executor

        公開されているlaneは`Executor.Io`、`Executor.Cpu`、`Executor.External`です。ResourceSystemは`RunAsync()`を呼ぶ前に宣言されたlaneへ移動します。

        - `Io`: SourceやI/O待ち中心の処理。
        - `Cpu`: decode、parse、変換。
        - `External`: GPU、compiler、native serviceなど外部実行環境との境界。GPU専用ではありません。

        `LoadContext`の`Io` / `Cpu` / `External` awaitableは、Step内でさらに明示的なstage hopが必要な場合に使います。すべてのStepが冒頭でhopを書く必要はありません。

        ## Extensionとfragment

        fragmentなしURIでは`FragmentPatterns == null`のStepが候補です。fragmentがある場合は一致するpatternを持つStepだけが候補になり、`mesh/*`の末尾`*`はprefix matchです。

        ```csharp
        public IEnumerable<string> FragmentPatterns => ["graphics", "compute"];
        ```

        `shader.slang#compute`をロードすると、出力Stepにはfragment付きURIが渡りますが、入力nodeはfragmentを外した`shader.slang`を使います。元データを共有しながらselectorごとの出力nodeを持てます。

        `Extensions`は同じ出力型の候補が複数ある場合の選択情報です。ファイル内容を検証する仕組みではないため、`RunAsync()`側でも入力の妥当性を扱ってください。

        ## 依存サービスとLoadContext

        GPU deviceやcompilerなどのserviceは、ResourceSystemからservice locatorとして取得せず、Stepのconstructorへ渡します。

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

        `LoadContext`はcancellation token、依存ロード、既存handleの要求、stage hop、`MarkOwned()` / `MarkBorrowed()`を提供します。

        ## 新しいresource typeを追加する手順

        `PlayerStats`のような新しい型は、次の順で追加するとSource、pipeline、lifetimeの境界を保てます。

        1. Resourceの公開値型を定義する。値が`IDisposable`なら所有者も決める。
        2. 直前の入力型を選ぶ。ファイル形式なら通常は`byte[]`、共通parse結果を再利用するならその中間型にする。
        3. `IResourceStep<TIn,TOut>`を実装し、lane、extension、fragmentを宣言する。
        4. 外部serviceはStep constructorへ渡し、`RunAsync`では`ctx.Token`と`ctx.Load` / `Require`を使う。
        5. composition rootでgeneric `AddStep<TIn,TOut>()`へ登録する。
        6. `Load<TOut>()`、`Ready`、status/error、Disposeまでを呼び出し側で扱う。
        7. 同URI共有、誤った入力、reload成功/失敗、Owned値の破棄をheadless testで確認する。

        [PlayerStatsPipeline](story:Examples/Resources/PlayerStatsPipeline)は`byte[] → JsonDocument → PlayerStats`を実際に登録・ロードし、最終値を検証します。`JsonDocument`を中間型にしたため、別のJSON resource typeもparse nodeを共有できます。

        ## 典型的な失敗

        - StepがSourceの代わりにpathを直接開き、VFSやreloadを迂回する。
        - `External`を`Gpu`という公開enumだと思い込む。
        - assembly scanやDI containerによる自動構築を期待する。
        - 同じ出力型のStepを無計画に複数登録し、選択をextensionやfragmentで区別しない。
        """);

    [Story("Learn/Resources/RegistrationAndComposition", Order = 4, Toc = true)]
    public static StoryResult RegistrationAndComposition(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/RegistrationAndComposition", $$"""
        # Stepの登録とpipeline合成

        {{ResourceCourseCatalog.Meta("Learn/Resources/RegistrationAndComposition", "Intermediate", "Standalone / Browser / AOT", "Backend neutral", "Resource Step")}}

        ## 構築時に登録する

        アプリのcomposition rootで、必要なSourceとStepを構築してResourceSystemへ渡すのが基本です。Stepが外部serviceを必要とする場合もここでconstructor injectionします。

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

        `AddSource()`と`AddStep()`でも追加できます。ただし既に作られたnodeのpipelineを組み直すAPIではないため、通常は最初の`Load<T>()`より前に登録を完了させます。

        ```csharp
        resources.AddSource(new WorkspaceSource(workspace));
        resources.AddStep(new TextAssetStep());
        ```

        trimmed / AOT / browserでは、reflectionを使わずdefault interface memberも到達可能にするgeneric overloadを優先します。

        ```csharp
        resources.AddStep<byte[], TextAsset>(new TextAssetStep());
        ```

        ## Pipelineは出力型から逆向きに作られる

        `Load<GpuTexture>(uri)`を呼ぶと、ResourceSystemは`GpuTexture`を出力するStepを選び、その入力型`CpuImage`を同じURIで再帰ロードします。さらに`CpuImage`を出力するStepを選び、最後に`byte[]`へ到達するとscheme対応Sourceを使います。

        ```text
        requested GpuTexture
          ← UploadStep(CpuImage → GpuTexture)
          ← ImageDecoder(byte[] → CpuImage)
          ← FileSource(uri → byte[])
        ```

        ## Stepの選択規則

        1. requested output typeで候補を探す。
        2. fragmentの有無と`FragmentPatterns`で候補を絞る。
        3. 候補が複数ならURI extension一致を優先する。
        4. extension指定のないgeneric Stepをfallbackにする。
        5. scope-local変換ではrequested input typeでも絞る。

        > [!NOTE]
        > 自動合成は登録済みStepの全経路を探索して「最短chain」を選ぶ仕組みではありません。各出力型で選ばれたStepの入力型を再帰的に解決します。同じ出力型の候補はextension、fragment、入力型で意図が明確になるよう設計してください。

        ## 登録チェックリスト

        - 対象schemeのSourceがあるか。
        - 最終型から`byte[]`まで各出力型のStepがあるか。
        - 同じ出力型の候補をextensionまたはfragmentで区別できるか。
        - Stepの外部依存をconstructorへ渡したか。
        - AOT環境ではgeneric `AddStep<TIn,TOut>()`を使っているか。
        """);

    [Story("Learn/Resources/PipelinesAndDag", Order = 5, Toc = true)]
    public static StoryResult PipelinesAndDag(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/PipelinesAndDag", $$"""
        # Typed pipelines and dependency DAG

        {{ResourceCourseCatalog.Meta("Learn/Resources/PipelinesAndDag", "Intermediate", "Standalone / Headless", "Backend neutral", "Registration and composition")}}

        pipelineの各段は独立したcache nodeです。`byte[]`やdecode済みCPU値も同じ型・URIなら共有されるため、複数の最終リソースが同じ中間結果を再利用できます。

        ## Chainが作る依存

        ```text
        GpuTexture panel.tex
          └─ depends on CpuImage panel.tex
               └─ depends on byte[] panel.tex
                    └─ FileSource
        ```

        依存edgeは単なる可視化情報ではありません。dependencyが差し替わったときのdependent reload、nodeを残すべきかの判定、連鎖evictionに使われます。

        ## 別URIをロードする

        StepがmanifestやglTFのように別URIを参照する場合は`LoadContext.Load<T>()`を使います。返されたhandleは現在nodeのdependencyとして接続されます。

        ```csharp
        using ResourceHandle<byte[]> buffer = ctx.Load<byte[]>(bufferUri);
        await buffer.Ready;
        return Parse(input, buffer.Value);
        ```

        `LoadContext.Load()`は「このStepがURIを知っていて、その場で依存を取得する」場合に使います。

        ## 既存handleを依存にする

        呼び出し側などが既に持っているhandleを現在nodeの依存として使う場合は`Require()`です。

        ```csharp
        CpuImage image = await ctx.Require(imageHandle);
        ```

        `Require()`は同じ`ResourceSystem`のhandleだけを受け付け、dependency edgeを追加して現在generationの値を待ちます。

        | API | 使いどころ |
        | --- | --- |
        | `ctx.Load<T>(uri)` | Step自身が別URIを解決する |
        | `ctx.Require(handle)` | 注入済み・公開済みのhandleをDAGへ接続する |

        ## Reloadの伝播

        Sourceのfile changeや`Republish()`でdependencyが更新されると、直接のdependentがreload queueへ入り、その出力更新がさらに後段へ伝播します。中間nodeを共有している場合も、同じnodeから各dependentへ伝播します。

        ## RenderGraphのDAGとの違い

        | Resources DAG | RenderGraph DAG |
        | --- | --- |
        | 型とURIのnode | passとframe resource |
        | 多フレーム寿命 | 1フレーム内 |
        | reloadとeviction | pass順序とbarrier |
        | handleがleaseを保持 | graph compile/executeで一時管理 |

        Resourcesで得た`GpuTexture`をRenderGraphへexternal resourceとしてimportすることはできますが、2つのDAGを同じものとして扱わないでください。
        """);

    [Story("Learn/Resources/ScopesAndOwnership", Order = 6, Toc = true)]
    public static StoryResult ScopesAndOwnership(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/ScopesAndOwnership", $$"""
        # ResourceScope and ownership

        {{ResourceCourseCatalog.Meta("Learn/Resources/ScopesAndOwnership", "Intermediate", "Game / Editor / Gallery", "Backend neutral / optional GPU", "Pipeline and DAG")}}

        ## ResourceScopeの役割

        `ResourceScope`はscene、document、widgetなどの論理ownerが持つ複数のleaseをまとめます。scope経由で取得・作成したhandleは追跡され、scopeのDisposeで一括解放されます。

        ```csharp
        using ResourceScope scope = resources.CreateScope("scene/main");
        ResourceHandle<CpuImage> image = scope.Load<CpuImage>("panel.tex");
        ```

        `scope.Load()`は通常の共有URIをロードします。同じ型・URIはscope外のhandleとも同じnodeを共有します。

        ## Scope-local resourceを作る

        `scope.Create()`はowner内のlocal keyを`scope://{owner}/{localKey}`へqualifyし、明示loaderで値を作ります。異なるownerが同じlocal keyを使っても別nodeになります。

        ```csharp
        ResourceHandle<Descriptor> descriptor = scope.Create(
            "material/default",
            _ => Task.FromResult(CreateDescriptor()));
        ```

        `Create<TInput,TOutput>()`ではscope-local inputをBorrowed nodeとして登録し、登録済みStepを通してoutputを作れます。program valueやdescriptorからGPU/runtime objectを生成する用途に向きます。

        ## OwnedとBorrowed

        `ResourceOwnership`はcache値の破棄責任を表します。

        | Ownership | replacement / eviction時のDispose |
        | --- | --- |
        | `Owned` | `ResourceSystem`が行う |
        | `Borrowed` | 外部ownerが行う。ResourceSystemは破棄しない |

        `Publish(uri, value)`の既定は`Owned`です。外部所有値を登録する場合は必ず明示します。

        ```csharp
        ResourceHandle<GpuDevice> deviceHandle = resources.Publish(
            "runtime://device/main",
            device,
            ResourceOwnership.Borrowed);
        ```

        Stepの結果については`ctx.MarkOwned()` / `ctx.MarkBorrowed()`で指定できます。所有する`IDisposable`を返すStepは、誰が破棄するかを実装時に決めてください。

        > [!NOTE]
        > fragment付きpipeline nodeは元URIの値を切り出すselectorとして扱われ、現在の自動合成ではBorrowedに設定されます。fragment出力が独立した`IDisposable`を所有する設計にする場合は、この挙動を前提にStepとownerの責任を決めてください。

        ## Scopeを使う目安

        - scene unloadで関連leaseをまとめて解放したい。
        - editor documentごとに同名のlocal resourceを持ちたい。
        - program valueを入力として登録済みStepを再利用したい。
        - 個々のhandle Dispose漏れをowner境界で防ぎたい。
        """);

    [Story("Learn/Resources/ReloadAndLifetime", Order = 7, Toc = true)]
    public static StoryResult ReloadAndLifetime(StoryContext ctx) => ResourceLearnExamples.Attach("Learn/Resources/ReloadAndLifetime", $$"""
        # Reload, publish, and lifetime

        {{ResourceCourseCatalog.Meta("Learn/Resources/ReloadAndLifetime", "Intermediate", "Game loop / DevTools / CI", "Backend neutral / optional GPU", "Scopes and ownership")}}

        ## Watchはロード前に有効化する

        `Watch()`は自動reloadを有効にします。node作成時にwatch tokenを登録するため、基本手順は最初の`Load<T>()`より前です。

        ```csharp
        resources.Watch();
        using ResourceHandle<CpuImage> image = resources.Load<CpuImage>("panel.tex");
        ```

        watch対象はSourceが読む`byte[]` nodeです。`FileSource`や`WorkspaceSource`の変更通知からbytesがreloadされ、依存DAGを通して後段のStepへ伝播します。

        ## Reloadを開始する経路

        - `Watch()`後のSource変更通知。
        - `Republish(uri, value)`によるprogram valueの差し替え。
        - `InvalidateAll()`による現在の全cache nodeの無効化。
        - dependency更新からdependentへの伝播。

        `Republish()`の差し替えは次の`Pump()`で適用されます。GPU device lostなど、全nodeを作り直したい場合は`InvalidateAll()`を使えます。

        ## Generationとlast-good-value

        nodeはload generationを持ちます。新しいreloadが始まると前の処理をcancelし、古いgenerationが遅れて完了しても現在値としてpublishしません。重複したreload requestもqueue上でcoalesceされます。

        reloadに失敗した場合、初回から値が無ければ`Failed`です。以前の正常値があればその値を維持し、`Status`はReady、`LastReloadError`に失敗を記録します。次の成功でerrorはclearされます。

        ## Pump境界

        game loopやGallery hostから`Pump()`を継続的に呼びます。

        ```csharp
        while (running)
        {
            PollEvents();
            resources.Pump();
            UpdateAndRender();
        }
        ```

        `Pump()`では主に次を処理します。

        1. reload後のvalue swapと`Version`更新。
        2. `Reloaded` eventとstate subscription通知。
        3. dependent reloadの開始。
        4. replacement / evictionで残した旧Owned値のdeferred dispose。

        初回ロードの値は`Ready`完了時に利用できます。「すべてのロード結果がPumpまで見えない」わけではありません。

        ## Evictionとdeferred dispose

        handleをDisposeしてrefcountが0になり、dependentも無いnodeはevictできます。nodeを消すとdependency edgeを外し、同じ条件を満たした入力nodeへ連鎖evictionします。

        Owned値のreplacement / eviction時のDisposeは`Pump()`まで遅延されます。GPUなど外部実行環境の値を安全に破棄する前にidle待ちが必要なら`SetDeferredDisposeIdleHook()`を設定します。

        ## 典型的な失敗

        - **Pumpを呼ばない**: reload計算が完了してもswap、event、deferred disposeが進みません。
        - **ロード後に初めてWatchを呼ぶ**: 既存nodeへ遡ってwatchを登録するAPIではありません。
        - **Ownedの外部値をPublishする**: 既定Ownedなので、外部ownerが破棄する値にはBorrowedを明示します。
        - **handleを保持しない**: leaseがなくなるとnodeがevict可能になり、利用期間とcache lifetimeが一致しません。
        """);
}
