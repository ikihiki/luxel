using Luxel.UI;

namespace Luxel.Resources.Gallery.Stories;

/// <summary>CPU asset modelとGPU manager integrationを学ぶコース。</summary>
public static class LearnResourceAssets
{
    [Story("Learn/Resources/Assets/Overview", Order = 11, Toc = true)]
    public static StoryResult Overview(StoryContext ctx) => Page("Learn/Resources/Assets/Overview", "アセットの概要", """
        `Luxel.Assets`は形式非依存のCPUモデルです。`AssetDocument`がscene、node、mesh、material、texture、sampler、animation、skin、camera、lightをまとめます。import packageはResource StepとしてCPU documentを生成し、`Luxel.AssetsGpu`はGPU domain、manager、typed policy、Asset向けStepをbuilderへ登録します。

        ```text
        bytes → importer → AssetDocument → inspection / runtime / GPU-managed values
        ```
        """);

    [Story("Learn/Resources/Assets/DocumentAndSceneGraph", Order = 12, Toc = true)]
    public static StoryResult DocumentAndSceneGraph(StoryContext ctx) => Page("Learn/Resources/Assets/DocumentAndSceneGraph", "AssetDocumentとシーングラフ", """
        `AssetScene.Roots`がroot nodeを選び、`AssetNode`はchildrenと任意のmesh、skin、camera、lightを参照します。`LocalMatrix`を親のworld matrixへ合成して走査します。CPU objectへの参照を保持する間はdocumentまたは明示ownerも保持します。

        ```csharp
        static void Visit(AssetNode node, Matrix4x4 parent)
        {
            Matrix4x4 world = node.LocalMatrix * parent;
            foreach (AssetNode child in node.Children) Visit(child, world);
        }
        ```
        """);

    [Story("Learn/Resources/Assets/MeshesAndPrimitives", Order = 13, Toc = true)]
    public static StoryResult MeshesAndPrimitives(StoryContext ctx) => Page("Learn/Resources/Assets/MeshesAndPrimitives", "メッシュとプリミティブ", """
        `AssetMesh.Primitives`は描画単位です。各`AssetPrimitive`はvertex attributes、任意のindex、material、morph targetを持ちます。positionは必須で、存在する属性の要素数とindex範囲をGPU転送前に検証します。GPU Stepはvertex layoutとshader ABIが一致する表現を生成します。
        """);

    [Story("Learn/Resources/Assets/MaterialsTexturesAndSamplers", Order = 14, Toc = true)]
    public static StoryResult MaterialsTexturesAndSamplers(StoryContext ctx) => Page("Learn/Resources/Assets/MaterialsTexturesAndSamplers", "マテリアル、テクスチャ、サンプラー", """
        `AssetMaterial`はPBR入力とtexture slotを保持し、`AssetTexture`はdecode済みpixel、`AssetSampler`はfilterとwrapを保持します。GPU capabilityはCPU object identityでuploadをdeduplicateし、参照先textureとsamplerを同じmanager generationへ接続します。
        """);

    [Story("Learn/Resources/Assets/AnimationSkinCameraAndLight", Order = 15, Toc = true)]
    public static StoryResult AnimationSkinCameraAndLight(StoryContext ctx) => Page("Learn/Resources/Assets/AnimationSkinCameraAndLight", "アニメーション、スキン、カメラ、ライト", """
        animation channelはtranslation、rotation、scale、morph weightを更新します。skinはjoint node、inverse bind matrix、skeleton rootを保持します。runtimeはsampling、transform propagation、joint計算、morph更新、camera/light抽出の順で処理し、frameごとの一時依存はRenderGraphへ渡します。
        """);

    [Story("Learn/Resources/Assets/LoadingAndGpu", Order = 16, Toc = true)]
    public static StoryResult LoadingAndGpu(StoryContext ctx) => Page("Learn/Resources/Assets/LoadingAndGpu", "アセットとGPU manager", """
        composition rootはCPU importerとGPU package extensionを`ResourceSystemBuilder`へ登録します。GPU extensionはdevice domain、`GpuResourceManager`、組込みGPU型のpolicy、Asset GPU Stepを構築完了前に追加します。`AssetGpuRegistry`は汎用GPU manager上でCPU Asset objectのdeduplicationと関連付けを提供するcapabilityです。

        ```csharp
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaultHandles core = ResourceSystemDefaults.AddCore(builder);
        builder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep())
            .RunOn(core.CpuDomain).ManagedBy(core.CpuManager).Register();
        AssetGpuResourceSystemRegistration gpu = builder.AddAssetGpu(device);
        await using ResourceSystem resources = await builder.BuildAsync();
        ```

        GPU package surfaceはdevice generationとGraphics lifecycleへ結び付きます。CPU handleとGPU handleは独立したleaseです。
        """);

    [Story("Learn/Resources/Assets/CustomGpuResourceTypes", Order = 17, Toc = true)]
    public static StoryResult CustomGpuResourceTypes(StoryContext ctx) => Page("Learn/Resources/Assets/CustomGpuResourceTypes", "Custom GPU resource types", """
        user-defined GPU classやstructはAsset型を継承せずexact type bindingへ登録できます。custom managerはallocation size、memory class、manager-local index、retirement、relocation、flush metadataを`ResourceManagementRecord`へ格納します。作成StepはGPU domainとmanagerを明示します。

        ```csharp
        ResourceManagerHandle particles = builder.Managers.Add("gpu.particles")
            .RunOn(gpu.Domain).Use(ctx => new ParticleBufferManager(ctx.Id, device)).Register();
        builder.Managers.Manage<ParticleBuffer>().With(particles).Register();
        builder.Steps.Add<ParticleSeed, ParticleBuffer>(new ParticleBufferStep(device))
            .RunOn(gpu.Domain).ManagedBy(particles).Owned().Register();
        ```

        logical identityは`ResourceHandle<ParticleBuffer>`が維持し、物理allocationとdevice generationはmanager recordに保存します。
        """);

    [Story("Learn/Resources/Assets/GpuMemoryAndIndexes", Order = 18, Toc = true)]
    public static StoryResult GpuMemoryAndIndexes(StoryContext ctx) => Page("Learn/Resources/Assets/GpuMemoryAndIndexes", "GPU memoryとindex", """
        GPU managerはcommitted/resident/logical bytes、heap class、fragmentation、budgetを追跡します。bindless descriptor indexはmanager-local index spaceに属し、retired generationを参照するfenceが完了してからfree listへ戻します。

        compactionはstable logical handleを保ち、allocationとdescriptorを移動してmanagement recordを更新します。snapshotはbudget pressure、pending retirement、index使用率、relocation件数を診断画面へ公開します。
        """);

    [Story("Learn/Resources/Assets/DeviceLossAndRecovery", Order = 19, Toc = true)]
    public static StoryResult DeviceLossAndRecovery(StoryContext ctx) => Page("Learn/Resources/Assets/DeviceLossAndRecovery", "Device lossとrecovery", """
        Graphics lifecycleはMessagePipeでloss eventをcoordinatorへ通知します。coordinatorはGPU managerをpauseし、失われたdevice generationのworkをcancelし、replacement deviceをactivateしてから`InvalidateManager(gpuManagerId)`でGPU nodeを再生成します。

        owned deviceはcoordinatorがshutdownし、borrowed deviceはhost ownerが管理します。CPU documentとlast-good診断は維持され、GPU generationがreadyになった時点でpublicationされます。
        """);

    [Story("Learn/Resources/Assets/ShaderAbi", Order = 20, Toc = true)]
    public static StoryResult ShaderAbi(StoryContext ctx) => Page("Learn/Resources/Assets/ShaderAbi", "アセットのシェーダーABI", """
        C# struct layout、shader offset、vertex stride、bindless indexの意味は1つのABIです。custom GPU structのpolicyはlogical/committed size、alignment、index space、device generationをmanagement metadataへ記録します。

        relocation可能なbufferではshaderから参照するdescriptor indexをmanagerが更新し、fence-safe publication後にallocationを交換します。ABI testはCPU encoding、shader decoding、stride、alignment、policy metadataを同時に検証します。
        """);

    private static StoryResult Page(string route, string title, string body) => ResourceLearnExamples.Attach(route, $$"""
        # {{title}}

        {{ResourceCourseCatalog.Meta(route, "中級", "Tools / Runtime / GPU", "Resources + Assets", "前章")}}

        {{body}}
        """);
}
