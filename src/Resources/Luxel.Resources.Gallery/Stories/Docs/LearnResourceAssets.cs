using Luxel.UI;

using static Luxel.Gallery.Story;

namespace Luxel.Resources.Gallery.Stories;

/// <summary>CPU asset modelとGPU manager integrationを学ぶコース。</summary>
[StoryMeta("Learn/Resources/Assets")]
public static class LearnResourceAssets
{
    [Story]
    public static StoryResult Overview(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # アセットの概要

        {{Toc()}}

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/Overview", "中級", "Tools / Runtime / GPU", "Resources + Assets", "前章")}}

        `Luxel.Assets`は形式非依存のCPUモデルです。`AssetDocument`がscene、node、mesh、material、texture、sampler、animation、skin、camera、lightをまとめます。import packageはResource StepとしてCPU documentを生成し、`Luxel.AssetsGpu`はGPU domain、manager、typed policy、Asset向けStepをbuilderへ登録します。

        ```luxel-story
        0
        ```

        ```text
        bytes → importer → AssetDocument → inspection / runtime / GPU-managed values
        ```
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/Assets/Overview"][0]));

    [Story]
    public static StoryResult DocumentAndSceneGraph(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # AssetDocumentとシーングラフ

        {{Toc()}}

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/DocumentAndSceneGraph", "中級", "Tools / Runtime / GPU", "Resources + Assets", "前章")}}

        `AssetScene.Roots`がroot nodeを選び、`AssetNode`はchildrenと任意のmesh、skin、camera、lightを参照します。`LocalMatrix`を親のworld matrixへ合成して走査します。CPU objectへの参照を保持する間はdocumentまたは明示ownerも保持します。

        ```luxel-story
        0
        ```

        ```csharp
        static void Visit(AssetNode node, Matrix4x4 parent)
        {
            Matrix4x4 world = node.LocalMatrix * parent;
            foreach (AssetNode child in node.Children) Visit(child, world);
        }
        ```
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/Assets/DocumentAndSceneGraph"][0]));

    [Story]
    public static StoryResult MeshesAndPrimitives(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # メッシュとプリミティブ

        {{Toc()}}

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/MeshesAndPrimitives", "中級", "Tools / Runtime / GPU", "Resources + Assets", "前章")}}

        `AssetMesh.Primitives`は描画単位です。各`AssetPrimitive`はvertex attributes、任意のindex、material、morph targetを持ちます。positionは必須で、存在する属性の要素数とindex範囲をGPU転送前に検証します。GPU Stepはvertex layoutとshader ABIが一致する表現を生成します。

        ```luxel-story
        0
        ```
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/Assets/MeshesAndPrimitives"][0]));

    [Story]
    public static StoryResult MaterialsTexturesAndSamplers(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # マテリアル、テクスチャ、サンプラー

        {{Toc()}}

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/MaterialsTexturesAndSamplers", "中級", "Tools / Runtime / GPU", "Resources + Assets", "前章")}}

        `AssetMaterial`はPBR入力とtexture slotを保持し、`AssetTexture`はdecode済みpixel、`AssetSampler`はfilterとwrapを保持します。GPU capabilityはCPU object identityでuploadをdeduplicateし、参照先textureとsamplerを同じmanager generationへ接続します。

        ```luxel-story
        0
        ```
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/Assets/MaterialsTexturesAndSamplers"][0]));

    [Story]
    public static StoryResult AnimationSkinCameraAndLight(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # アニメーション、スキン、カメラ、ライト

        {{Toc()}}

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/AnimationSkinCameraAndLight", "中級", "Tools / Runtime / GPU", "Resources + Assets", "前章")}}

        animation channelはtranslation、rotation、scale、morph weightを更新します。skinはjoint node、inverse bind matrix、skeleton rootを保持します。runtimeはsampling、transform propagation、joint計算、morph更新、camera/light抽出の順で処理し、frameごとの一時依存はRenderGraphへ渡します。

        ```luxel-story
        0
        ```
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/Assets/AnimationSkinCameraAndLight"][0]));

    [Story]
    public static StoryResult LoadingAndGpu(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # アセットとGPU manager

        {{Toc()}}

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/LoadingAndGpu", "中級", "Tools / Runtime / GPU", "Resources + Assets", "前章")}}

        composition rootはCPU importerとGPU package extensionを`ResourceSystemBuilder`へ登録します。GPU extensionはdevice domain、`GpuResourceManager`、組込みGPU型のpolicy、Asset GPU Stepを構築完了前に追加します。`AssetGpuRegistry`は汎用GPU manager上でCPU Asset objectのdeduplicationと関連付けを提供するcapabilityです。

        ```luxel-story
        0
        ```

        ```csharp
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaultHandles core = ResourceSystemDefaults.AddCore(builder);
        builder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep())
            .RunOn(core.CpuDomain).ManagedBy(core.CpuManager).Register();
        AssetGpuResourceSystemRegistration gpu = builder.AddAssetGpu(device);
        await using ResourceSystem resources = await builder.BuildAsync();
        ```

        GPU package surfaceはdevice generationとGraphics lifecycleへ結び付きます。CPU handleとGPU handleは独立したleaseです。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/Assets/LoadingAndGpu"][0]));

    [Story]
    public static StoryResult CustomGpuResourceTypes(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # Custom GPU resource types

        {{Toc()}}

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/CustomGpuResourceTypes", "中級", "Tools / Runtime / GPU", "Resources + Assets", "前章")}}

        user-defined GPU classやstructはAsset型を継承せずexact type bindingへ登録できます。custom managerはallocation size、memory class、manager-local index、retirement、relocation、flush metadataを`ResourceManagementRecord`へ格納します。作成StepはGPU domainとmanagerを明示します。

        ```luxel-story
        0
        ```

        ```csharp
        ResourceManagerHandle particles = builder.Managers.Add("gpu.particles")
            .RunOn(gpu.Domain).Use(ctx => new ParticleBufferManager(ctx.Id, device)).Register();
        builder.Managers.Manage<ParticleBuffer>().With(particles).Register();
        builder.Steps.Add<ParticleSeed, ParticleBuffer>(new ParticleBufferStep(device))
            .RunOn(gpu.Domain).ManagedBy(particles).Owned().Register();
        ```

        logical identityは`ResourceHandle<ParticleBuffer>`が維持し、物理allocationとdevice generationはmanager recordに保存します。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/Assets/CustomGpuResourceTypes"][0]));

    [Story]
    public static StoryResult GpuMemoryAndIndexes(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # GPU memoryとindex

        {{Toc()}}

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/GpuMemoryAndIndexes", "中級", "Tools / Runtime / GPU", "Resources + Assets", "前章")}}

        GPU managerはcommitted/resident/logical bytes、heap class、fragmentation、budgetを追跡します。bindless descriptor indexはmanager-local index spaceに属し、retired generationを参照するfenceが完了してからfree listへ戻します。

        ```luxel-story
        0
        ```

        compactionはstable logical handleを保ち、allocationとdescriptorを移動してmanagement recordを更新します。snapshotはbudget pressure、pending retirement、index使用率、relocation件数を診断画面へ公開します。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/Assets/GpuMemoryAndIndexes"][0]));

    [Story]
    public static StoryResult DeviceLossAndRecovery(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # Device lossとrecovery

        {{Toc()}}

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/DeviceLossAndRecovery", "中級", "Tools / Runtime / GPU", "Resources + Assets", "前章")}}

        Graphics lifecycleはMessagePipeでloss eventをcoordinatorへ通知します。coordinatorはGPU managerをpauseし、失われたdevice generationのworkをcancelし、replacement deviceをactivateしてから`InvalidateManager(gpuManagerId)`でGPU nodeを再生成します。

        ```luxel-story
        0
        ```

        owned deviceはcoordinatorがshutdownし、borrowed deviceはhost ownerが管理します。CPU documentとlast-good診断は維持され、GPU generationがreadyになった時点でpublicationされます。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/Assets/DeviceLossAndRecovery"][0]));

    [Story]
    public static StoryResult ShaderAbi(StoryContext ctx) => StoryResult.FromMarkdown($$"""
        # アセットのシェーダーABI

        {{Toc()}}

        {{ResourceCourseCatalog.Meta("Learn/Resources/Assets/ShaderAbi", "中級", "Tools / Runtime / GPU", "Resources + Assets", "前章")}}

        C# struct layout、shader offset、vertex stride、bindless indexの意味は1つのABIです。custom GPU structのpolicyはlogical/committed size、alignment、index space、device generationをmanagement metadataへ記録します。

        ```luxel-story
        0
        ```

        relocation可能なbufferではshaderから参照するdescriptor indexをmanagerが更新し、fence-safe publication後にallocationを交換します。ABI testはCPU encoding、shader decoding、stride、alignment、policy metadataを同時に検証します。
        """, StoryReference.To(ResourceLearnExamples.Routes["Learn/Resources/Assets/ShaderAbi"][0]));
}
