using Luxel.UI;

namespace Luxel.Resources.Gallery.Stories;

/// <summary>ResourceSystemの構築、実行、管理、公開、回復を学ぶコース。</summary>
public static class LearnResources
{
    [Story("Learn/Resources/Overview", Order = 0, SampleBundle = "resources.scenarios", Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # Resources学習ガイド

        {{ResourceCourseCatalog.Meta("Learn/Resources/Overview", "中級", "Native / Browser / Headless", "ResourceSystemBuilder", "前章")}}

        `ResourceSystem`は型と正規化URIで論理リソースを識別し、Sourceと型付きStepから依存DAGを構築します。各generationの値はmanagerへadoptされ、Stepは登録されたexecution domainで実行されます。`Pump()`はgenerationの公開、通知、retirementをアプリケーションの境界へ揃えます。

        ```luxel-story
        0
        ```

        ```text
        request → source/step DAG → execution domain → manager adoption → Pump publication → handle
        ```

        `Luxel.Assets`はCPUデータモデル、`Luxel.AssetsGpu`はGPU管理拡張、Graphics lifecycleはdevice generation、RenderGraphはフレーム内の実行依存を担当します。ResourceのDAGは複数フレームにまたがるidentity、reload、ownershipを担当します。

        ## 学習順

        {{ResourceCourseCatalog.LearningRouteMarkdown()}}
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/Overview"][0]));

    [Story("Learn/Resources/BuilderAndComposition", Order = 1, Toc = true)]
    public static StoryResult BuilderAndComposition(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # Builderとcomposition

        {{ResourceCourseCatalog.Meta("Learn/Resources/BuilderAndComposition", "中級", "Native / Browser / Headless", "ResourceSystemBuilder", "前章")}}

        `ResourceSystemBuilder`の`Domains`、`Sources`、`Steps`、`Managers`へ構成を登録します。登録メソッドが返すhandleは同じ構築トランザクション内で参照する値オブジェクトです。

        ```luxel-story
        0
        ```

        ```csharp
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaultHandles core = ResourceSystemDefaults.AddCore(builder);
        builder.Sources.Add(new FileSource(files)).RunOn(core.IoDomain).ManagedBy(core.IoManager).Register();
        builder.Steps.Add<byte[], TextAsset>(new TextStep()).RunOn(core.CpuDomain).ManagedBy(core.CpuManager).Register();
        await using ResourceSystem resources = await builder.BuildAsync();
        ```

        `BuildAsync()`は構成検証、component生成、ready barrierを完了してからimmutableなtableを持つsystemを返します。packageはbuilder extensionを提供し、application composition rootが使用するpackageとIDを決定します。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/BuilderAndComposition"][0]));

    [Story("Learn/Resources/ExecutionDomains", Order = 2, Toc = true)]
    public static StoryResult ExecutionDomains(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # Execution domain

        {{ResourceCourseCatalog.Meta("Learn/Resources/ExecutionDomains", "中級", "Native / Browser / Headless", "ResourceSystemBuilder", "前章")}}

        execution domainはscheduler、affinity、progress model、最大同時実行数、operation budgetをまとめます。IDはcomposition rootが決め、Stepの`.RunOn(handle)`で実行場所を選びます。

        ```luxel-story
        0
        ```

        | 契約 | 用途 |
        | --- | --- |
        | `Parallel` | 独立したI/OやCPU作業 |
        | `Serialized` | compiler、device queue、順序付きservice |
        | `Cooperative` | owner context上でyieldするsingle-thread host |

        cancellationはdispatchとStepの`LoadContext.Token`へ伝播します。domain snapshotからqueue depth、active count、queue/run duration、completed countを取得できます。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/ExecutionDomains"][0]));

    [Story("Learn/Resources/ResourceManagers", Order = 3, Toc = true)]
    public static StoryResult ResourceManagers(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # Resource manager

        {{ResourceCourseCatalog.Meta("Learn/Resources/ResourceManagers", "中級", "Native / Browser / Headless", "ResourceSystemBuilder", "前章")}}

        managerは公開generationのadoption、allocation accounting、index、retirement、compaction、metricsを担当します。exact output typeのbinding、明示manager、composition rootのdefaultという規則で選択されます。

        ```luxel-story
        0
        ```

        ```csharp
        ResourceManagerHandle textures = builder.Managers.Add("gpu.textures")
            .RunOn(gpuDomain).Use(ctx => new TextureManager(ctx.Id)).Register();
        builder.Managers.Manage<GpuTexture>().With(textures).Register();
        ```

        `IoResourceManager`と`CpuResourceManager`はcore構成に利用できます。GPUやcompilerなどのmanagerは所有packageがpolicyとlifecycleを実装します。`PumpAsync`とsnapshotはpending retirementやbudget処理をsystemの進行へ接続します。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/ResourceManagers"][0]));

    [Story("Learn/Resources/IdentityAndHandles", Order = 4, Toc = true)]
    public static StoryResult IdentityAndHandles(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # Identityとhandle

        {{ResourceCourseCatalog.Meta("Learn/Resources/IdentityAndHandles", "中級", "Native / Browser / Headless", "ResourceSystemBuilder", "前章")}}

        cache identityはexact output typeと`ResourceUri.Key`の組です。同じrequestは同じ論理nodeと中間generationを共有します。qualifierが必要な管理空間は`ResourceManagementContext`で明示します。

        ```luxel-story
        0
        ```

        `ResourceHandle<T>`は論理nodeへのleaseです。`Ready`、`Status`、`HasValue`、`Value`、`Version`、`LastReloadError`を公開し、generationが交換されても同じhandleを使えます。`ResourceScope`は複数leaseとruntime valueを所有者単位で解放します。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/IdentityAndHandles"][0]));

    [Story("Learn/Resources/SourcesAndSteps", Order = 5, Toc = true)]
    public static StoryResult SourcesAndSteps(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # SourceとStep

        {{ResourceCourseCatalog.Meta("Learn/Resources/SourcesAndSteps", "中級", "Native / Browser / Headless", "ResourceSystemBuilder", "前章")}}

        `IResourceSource`はURI schemeから`byte[]`を読みます。`IResourceStep<TIn,TOut>`は1つの型付き変換を実装します。builderでdomain、manager、ownership、extension、fragment、priorityを宣言します。

        ```luxel-story
        0
        ```

        ```csharp
        builder.Sources.Add(new PackageSource(entries)).RunOn(io).ManagedBy(ioManager).Register();
        builder.Steps.Add<byte[], TextAsset>(new TextStep())
            .RunOn(decode).ManagedBy(cpuManager).ForExtensions(".txt").Owned().Register();
        ```

        Stepはdependencyを`LoadContext.Load`または`Require`で要求できます。generation固有のownershipや管理情報を返すStep contractが利用可能なpackageでは、結果metadataをmanager adoptionへ渡します。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/SourcesAndSteps"][0]));

    [Story("Learn/Resources/DependenciesAndPublication", Order = 6, Toc = true)]
    public static StoryResult DependenciesAndPublication(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # 依存関係とpublication

        {{ResourceCourseCatalog.Meta("Learn/Resources/DependenciesAndPublication", "中級", "Native / Browser / Headless", "ResourceSystemBuilder", "前章")}}

        出力型から入力型へ解決した各nodeはDAGの辺を持ちます。相対URIは親requestのURIから解決し、同じdependency requestは共有されます。dependencyの変更は依存元のgenerationを無効化します。

        ```luxel-story
        0
        ```

        完了した作業は直接observerへ通知されません。`Pump()`が成功generationの交換、状態通知、`Reloaded`、manager pumpを順序付けます。hostはobserverを実行するthreadまたはowner contextで`Pump()`を呼びます。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/DependenciesAndPublication"][0]));

    [Story("Learn/Resources/OwnershipAndRetirement", Order = 7, Toc = true)]
    public static StoryResult OwnershipAndRetirement(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # Ownershipとretirement

        {{ResourceCourseCatalog.Meta("Learn/Resources/OwnershipAndRetirement", "中級", "Native / Browser / Headless", "ResourceSystemBuilder", "前章")}}

        ownershipはgeneration単位のmanagement recordに保存されます。`Owned`値はmanagerがretirement時に破棄し、`Borrowed`値は外部ownerが破棄します。置換、stale completion、eviction、shutdownはそれぞれretire reasonとしてmanagerへ渡ります。

        ```luxel-story
        0
        ```

        scopeとhandleを解放するとleaseが減り、到達不能なgenerationはretirementへ進みます。非同期破棄やGPU fence待ちはmanagerのqueueで処理し、deviceや外部serviceを破棄する前にResourceSystemをshutdownします。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/OwnershipAndRetirement"][0]));

    [Story("Learn/Resources/ReloadAndRecovery", Order = 8, Toc = true)]
    public static StoryResult ReloadAndRecovery(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # Reloadとrecovery

        {{ResourceCourseCatalog.Meta("Learn/Resources/ReloadAndRecovery", "中級", "Native / Browser / Headless", "ResourceSystemBuilder", "前章")}}

        invalidationは対象nodeからgenerationを再作成します。進行中の世代はcancelされ、stale completionは公開されずretirementへ送られます。再作成に失敗した場合、handleはlast-good valueを維持し、診断を`LastReloadError`へ記録します。

        ```luxel-story
        0
        ```

        manager固有の外部状態を再生成した場合は`InvalidateManager(managerId)`でそのmanagerに属するnodeだけを再読み込みできます。device generationやcompiler sessionの回復をResourceSystem全体の再構築から分離できます。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/ReloadAndRecovery"][0]));

    [Story("Learn/Resources/DiagnosticsAndMetrics", Order = 9, Toc = true)]
    public static StoryResult DiagnosticsAndMetrics(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # Diagnosticsとmetrics

        {{ResourceCourseCatalog.Meta("Learn/Resources/DiagnosticsAndMetrics", "中級", "Native / Browser / Headless", "ResourceSystemBuilder", "前章")}}

        `CaptureDomainSnapshots()`はqueue depth、active count、完了数、queue/run durationを返します。`CaptureManagerSnapshots()`はadopt/retire件数、logical bytes、pending retirementを返します。manager固有snapshotはfragmentation、budget、index使用量、compaction、recovery stateを追加できます。

        ```luxel-story
        0
        ```

        診断画面ではnode statusとgeneration、domain saturation、manager memory、retirement backlogを同じ時点で採取します。queue latencyの増加とmemory pressureを分けて表示すると、scheduler調整とbudget回復を適切に選べます。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/DiagnosticsAndMetrics"][0]));

    [Story("Learn/Resources/WasmExecution", Order = 10, Toc = true)]
    public static StoryResult WasmExecution(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # WASM execution

        {{ResourceCourseCatalog.Meta("Learn/Resources/WasmExecution", "中級", "Native / Browser / Headless", "ResourceSystemBuilder", "前章")}}

        single-thread WASMではowner contextに結び付いたcooperative domainを登録します。effective concurrencyは1で、各work itemはbudget内で処理し、長い処理はhost event loopへyieldします。

        ```luxel-story
        0
        ```

        Source、Step、manager retirementはasync contractを維持します。`BuildAsync()`のready barrier、owner contextでの`Pump()`、非同期retirementを待つshutdownを使用し、同期blockで完了を待ちません。thread対応WASMでは用途ごとに別domainを構成できます。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/WasmExecution"][0]));

}
