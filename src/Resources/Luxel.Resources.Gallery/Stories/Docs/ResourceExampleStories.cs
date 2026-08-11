using System.Runtime.InteropServices;
using System.Text.Json;
using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.Resources;
using Luxel.UI;
using static Luxel.Resources.Gallery.Stories.ResourceScenarioSupport;

namespace Luxel.Resources.Gallery.Stories;

/// <summary>ResourceSystem builder、domain、manager、publicationを実行して観測するシナリオ。</summary>
public static class ResourceExampleStories
{
    [Story("Examples/Resources/ReadyBuilder", Order = 0, SampleBundle = "resources.scenarios")]
    public static Widget ReadyBuilder(StoryContext ctx) => new ResourceScenarioWidget("readyなbuilder", (builder, h) =>
    {
        builder.Sources.Add(new FileSource(Files(("hello.txt", "hello resources")))).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], TextAsset>(new UpperTextStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
    }, async resources =>
    {
        using ResourceHandle<TextAsset> handle = resources.Load<TextAsset>("hello.txt");
        await handle.Ready;
        return $"状態={handle.Status}; 値={handle.Value.Text}";
    }, ctx.Log);

    [Story("Examples/Resources/CustomExecutionDomain", Order = 1, SampleBundle = "resources.scenarios")]
    public static Widget CustomExecutionDomain(StoryContext ctx)
    {
        ResourceExecutionDomainHandle custom = default;
        return new ResourceScenarioWidget("任意名の実行domain", (builder, h) =>
        {
            custom = builder.Domains.Add("gallery.decode").UseThreadPool(2).WithMetrics("gallery.decode").Register();
            builder.Steps.Add<RuntimeSeed, RuntimeLabel>(new RuntimeLabelStep()).RunOn(custom).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/custom-domain");
            ResourceHandle<RuntimeLabel> value = scope.Create<RuntimeSeed, RuntimeLabel>("label", new(4));
            await value.Ready;
            ResourceExecutionDomainSnapshot snapshot = resources.CaptureDomainSnapshots().Single(x => x.Id == custom.Id);
            return $"domain={snapshot.Id}; 完了={snapshot.CompletedCount}; 値={value.Value.Text}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/SerializedCompilerDomain", Order = 2, SampleBundle = "resources.scenarios")]
    public static Widget SerializedCompilerDomain(StoryContext ctx)
    {
        ResourceExecutionDomainHandle compiler = default;
        var step = new SerializedProbeStep();
        return new ResourceScenarioWidget("直列compiler domain", (builder, h) =>
        {
            compiler = builder.Domains.Add("shader.compiler").UseSerial().Register();
            builder.Steps.Add<RuntimeSeed, RuntimeLabel>(step).RunOn(compiler).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/compiler");
            ResourceHandle<RuntimeLabel>[] handles = Enumerable.Range(1, 3)
                .Select(i => scope.Create<RuntimeSeed, RuntimeLabel>($"compile-{i}", new(i))).ToArray();
            await Task.WhenAll(handles.Select(h => h.Ready));
            return $"順序={string.Join(',', step.Order)}; 最大同時実行={step.MaxActive}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/TypedManagerBinding", Order = 3, SampleBundle = "resources.scenarios")]
    public static Widget TypedManagerBinding(StoryContext ctx)
    {
        TrackingManager? manager = null;
        return new ResourceScenarioWidget("型付きmanager binding", (builder, h) =>
        {
            ResourceManagerHandle typed = builder.Managers.Add("gallery.labels").RunOn(h.CpuDomain)
                .Use(context => manager = new TrackingManager(context.Id)).Register();
            builder.Managers.Manage<RuntimeLabel>().With(typed).Register();
            builder.Steps.Add<RuntimeSeed, RuntimeLabel>(new RuntimeLabelStep()).RunOn(h.CpuDomain).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/manager");
            ResourceHandle<RuntimeLabel> value = scope.Create<RuntimeSeed, RuntimeLabel>("managed", new(7));
            await value.Ready;
            return $"manager={manager!.Id}; adopt={manager.Adopted}; 値={value.Value.Text}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/SharedRequestIdentity", Order = 4, SampleBundle = "resources.scenarios")]
    public static Widget SharedRequestIdentity(StoryContext ctx)
    {
        var step = new CountingTextStep();
        return new ResourceScenarioWidget("共有request identity", (builder, h) =>
        {
            builder.Sources.Add(new FileSource(Files(("shared.txt", "shared")))).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
            builder.Steps.Add<byte[], TextAsset>(step).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
            builder.Steps.Add<TextAsset, WordCount>(new WordCountStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            using ResourceHandle<TextAsset> text = resources.Load<TextAsset>("shared.txt");
            using ResourceHandle<WordCount> count = resources.Load<WordCount>("shared.txt");
            await Task.WhenAll(text.Ready, count.Ready);
            return $"中間Step実行={step.Runs}; 単語数={count.Value.Count}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/CustomSourceAndStep", Order = 5, SampleBundle = "resources.scenarios")]
    public static Widget CustomSourceAndStep(StoryContext ctx) => new ResourceScenarioWidget("custom SourceとStep", (builder, h) =>
    {
        builder.Sources.Add(new PackageSource(new Dictionary<string, byte[]> { ["ui/title.txt"] = Bytes("package title") }))
            .RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], TextAsset>(new UpperTextStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).ForExtensions(".txt").Register();
    }, async resources =>
    {
        using ResourceHandle<TextAsset> title = resources.Load<TextAsset>("package://ui/title.txt");
        await title.Ready;
        return $"scheme=package; 値={title.Value.Text}";
    }, ctx.Log);

    [Story("Examples/Resources/DependencyPublication", Order = 6, SampleBundle = "resources.scenarios")]
    public static Widget DependencyPublication(StoryContext ctx) => new ResourceScenarioWidget("依存とpublication", (builder, h) =>
    {
        builder.Sources.Add(new FileSource(Files(("words.txt", "one two")))).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], TextAsset>(new UpperTextStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        builder.Steps.Add<TextAsset, WordCount>(new WordCountStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
    }, async resources =>
    {
        using ResourceHandle<WordCount> count = resources.Load<WordCount>("words.txt");
        await count.Ready;
        resources.Pump();
        return $"依存完了=True; Pump公開=True; 単語数={count.Value.Count}";
    }, ctx.Log);

    [Story("Examples/Resources/ScopedRetirement", Order = 7, SampleBundle = "resources.scenarios")]
    public static Widget ScopedRetirement(StoryContext ctx)
    {
        TrackingManager? manager = null;
        return new ResourceScenarioWidget("scope retirement", (builder, h) =>
        {
            ResourceManagerHandle tracked = builder.Managers.Add("gallery.retirement").RunOn(h.CpuDomain)
                .Use(context => manager = new TrackingManager(context.Id)).Register();
            builder.Managers.Manage<RuntimeLabel>().With(tracked).Register();
            builder.Steps.Add<RuntimeSeed, RuntimeLabel>(new RuntimeLabelStep()).RunOn(h.CpuDomain).Register();
        }, async resources =>
        {
            using (ResourceScope scope = resources.CreateScope("example/retirement"))
            {
                ResourceHandle<RuntimeLabel> value = scope.Create<RuntimeSeed, RuntimeLabel>("owned", new(9));
                await value.Ready;
            }
            resources.Pump();
            await WaitUntil(() => manager!.Retired > 0);
            return $"adopt={manager!.Adopted}; retire={manager.Retired}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/ReloadKeepsLastGood", Order = 8, SampleBundle = "resources.scenarios")]
    public static Widget ReloadKeepsLastGood(StoryContext ctx)
    {
        MemoryFileSystem files = Files(("live.json", "{\"name\":\"Mina\",\"level\":1}"));
        return new ResourceScenarioWidget("last-good recovery", (builder, h) =>
        {
            builder.Sources.Add(new FileSource(files)).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
            builder.Steps.Add<byte[], JsonDocument>(new JsonStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
            builder.Steps.Add<JsonDocument, PlayerStats>(new PlayerStatsStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            resources.Watch();
            using ResourceHandle<JsonDocument> json = resources.Load<JsonDocument>("live.json");
            using ResourceHandle<PlayerStats> stats = resources.Load<PlayerStats>("live.json");
            await Task.WhenAll(json.Ready, stats.Ready);
            files.Set("live.json", Bytes("not json"));
            await PumpUntil(resources, () => json.LastReloadError is not null);
            int lastGood = stats.Value.Level;
            files.Set("live.json", Bytes("{\"name\":\"Mina\",\"level\":2}"));
            await PumpUntil(resources, () => json.LastReloadError is null && stats.Value.Level == 2);
            return $"last-good={lastGood}; 復旧={stats.Value.Level}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/DomainAndManagerMetrics", Order = 9, SampleBundle = "resources.scenarios")]
    public static Widget DomainAndManagerMetrics(StoryContext ctx) => new ResourceScenarioWidget("domainとmanager metrics", (builder, h) =>
    {
        builder.Sources.Add(new FileSource(Files(("metrics.txt", "metrics")))).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], TextAsset>(new UpperTextStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
    }, async resources =>
    {
        using ResourceHandle<TextAsset> value = resources.Load<TextAsset>("metrics.txt");
        await value.Ready;
        ResourceExecutionDomainSnapshot domain = resources.CaptureDomainSnapshots().Single(s => s.Id.Value == "resource.cpu");
        ResourceManagerSnapshot manager = resources.CaptureManagerSnapshots().Single(s => s.Id.Value == "resource.cpu-manager");
        return $"完了={domain.CompletedCount}; queue={domain.QueueDepth}; adopt={manager.AdoptedCount}; bytes={manager.LogicalBytes}";
    }, ctx.Log);

    [Story("Examples/Resources/WasmCooperativeScheduling", Order = 10, SampleBundle = "resources.scenarios")]
    public static Widget WasmCooperativeScheduling(StoryContext ctx)
    {
        ResourceExecutionDomainHandle wasm = default;
        return new ResourceScenarioWidget("WASM cooperative scheduling", (builder, h) =>
        {
            var capabilities = new ResourceExecutionDomainCapabilities(1, ResourceThreadAffinity.HostThread, ResourceProgressModel.Cooperative, OperationBudget: TimeSpan.FromMilliseconds(2));
            wasm = builder.Domains.Add("browser.owner").UseFactory(c => new CooperativeDemoDomain(c.Id, capabilities), capabilities).Register();
            builder.Steps.Add<RuntimeSeed, RuntimeLabel>(new RuntimeLabelStep()).RunOn(wasm).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/wasm");
            ResourceHandle<RuntimeLabel> value = scope.Create<RuntimeSeed, RuntimeLabel>("yield", new(1));
            await value.Ready;
            ResourceExecutionDomainSnapshot snapshot = resources.CaptureDomainSnapshots().Single(s => s.Id == wasm.Id);
            return $"progress=Cooperative; concurrency=1; 完了={snapshot.CompletedCount}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/Assets/GpuManagerInstallation", Order = 20)]
    public static Widget GpuManagerInstallation(StoryContext ctx)
    {
        TrackingManager? manager = null;
        return new ResourceScenarioWidget("GPU manager installation", (builder, h) =>
        {
            ResourceExecutionDomainHandle gpu = builder.Domains.Add("gpu.device").UseSerial().Register();
            ResourceManagerHandle gpuManager = builder.Managers.Add("gpu.manager").RunOn(gpu)
                .Use(context => manager = new TrackingManager(context.Id)).Register();
            builder.Managers.Manage<GpuContractValue>().With(gpuManager).Register();
            builder.Steps.Add<DiagnosticSeed, GpuContractValue>(new GpuContractStep()).RunOn(gpu).ManagedBy(gpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/gpu-contract");
            ResourceHandle<GpuContractValue> value = scope.Create<DiagnosticSeed, GpuContractValue>("gpu", new("gpu.manager + gpu.device domain + typed policy"));
            await value.Ready;
            return $"manager={manager!.Id}; generation={value.Version}; {value.Value.Description}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/Assets/CustomGpuParticleBuffers", Order = 21)]
    public static Widget CustomGpuParticleBuffers(StoryContext ctx)
    {
        TrackingManager? manager = null;
        return new ResourceScenarioWidget("custom GPU particle buffers", (builder, h) =>
        {
            ResourceExecutionDomainHandle gpu = builder.Domains.Add("gpu.device").UseSerial().Register();
            ResourceManagerHandle gpuManager = builder.Managers.Add("gpu.manager").RunOn(gpu)
                .Use(context => manager = new TrackingManager(context.Id)).Register();
            builder.Managers.Manage<GpuContractValue>().With(gpuManager).Register();
            builder.Steps.Add<DiagnosticSeed, GpuContractValue>(new GpuContractStep()).RunOn(gpu).ManagedBy(gpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/gpu-contract");
            ResourceHandle<GpuContractValue> value = scope.Create<DiagnosticSeed, GpuContractValue>("gpu", new("ParticleBufferをexact type bindingで管理"));
            await value.Ready;
            return $"manager={manager!.Id}; generation={value.Version}; {value.Value.Description}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/Assets/CustomGpuStructRetirement", Order = 22)]
    public static Widget CustomGpuStructRetirement(StoryContext ctx)
    {
        TrackingManager? manager = null;
        return new ResourceScenarioWidget("custom GPU struct retirement", (builder, h) =>
        {
            ResourceExecutionDomainHandle gpu = builder.Domains.Add("gpu.device").UseSerial().Register();
            ResourceManagerHandle gpuManager = builder.Managers.Add("gpu.manager").RunOn(gpu)
                .Use(context => manager = new TrackingManager(context.Id)).Register();
            builder.Managers.Manage<GpuContractValue>().With(gpuManager).Register();
            builder.Steps.Add<DiagnosticSeed, GpuContractValue>(new GpuContractStep()).RunOn(gpu).ManagedBy(gpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/gpu-contract");
            ResourceHandle<GpuContractValue> value = scope.Create<DiagnosticSeed, GpuContractValue>("gpu", new("複数handleをmanagerのretirement queueへ送る"));
            await value.Ready;
            return $"manager={manager!.Id}; generation={value.Version}; {value.Value.Description}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/Assets/GpuIndexRecycling", Order = 23)]
    public static Widget GpuIndexRecycling(StoryContext ctx)
    {
        TrackingManager? manager = null;
        return new ResourceScenarioWidget("GPU index recycling", (builder, h) =>
        {
            ResourceExecutionDomainHandle gpu = builder.Domains.Add("gpu.device").UseSerial().Register();
            ResourceManagerHandle gpuManager = builder.Managers.Add("gpu.manager").RunOn(gpu)
                .Use(context => manager = new TrackingManager(context.Id)).Register();
            builder.Managers.Manage<GpuContractValue>().With(gpuManager).Register();
            builder.Steps.Add<DiagnosticSeed, GpuContractValue>(new GpuContractStep()).RunOn(gpu).ManagedBy(gpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/gpu-contract");
            ResourceHandle<GpuContractValue> value = scope.Create<DiagnosticSeed, GpuContractValue>("gpu", new("fence完了後にmanager-local indexを再利用"));
            await value.Ready;
            return $"manager={manager!.Id}; generation={value.Version}; {value.Value.Description}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/Assets/GpuCompaction", Order = 24)]
    public static Widget GpuCompaction(StoryContext ctx)
    {
        TrackingManager? manager = null;
        return new ResourceScenarioWidget("GPU compaction", (builder, h) =>
        {
            ResourceExecutionDomainHandle gpu = builder.Domains.Add("gpu.device").UseSerial().Register();
            ResourceManagerHandle gpuManager = builder.Managers.Add("gpu.manager").RunOn(gpu)
                .Use(context => manager = new TrackingManager(context.Id)).Register();
            builder.Managers.Manage<GpuContractValue>().With(gpuManager).Register();
            builder.Steps.Add<DiagnosticSeed, GpuContractValue>(new GpuContractStep()).RunOn(gpu).ManagedBy(gpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/gpu-contract");
            ResourceHandle<GpuContractValue> value = scope.Create<DiagnosticSeed, GpuContractValue>("gpu", new("logical handleを保ったallocation relocation"));
            await value.Ready;
            return $"manager={manager!.Id}; generation={value.Version}; {value.Value.Description}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/Assets/DeviceLostRecovery", Order = 25)]
    public static Widget DeviceLostRecovery(StoryContext ctx)
    {
        TrackingManager? manager = null;
        return new ResourceScenarioWidget("device lost recovery", (builder, h) =>
        {
            ResourceExecutionDomainHandle gpu = builder.Domains.Add("gpu.device").UseSerial().Register();
            ResourceManagerHandle gpuManager = builder.Managers.Add("gpu.manager").RunOn(gpu)
                .Use(context => manager = new TrackingManager(context.Id)).Register();
            builder.Managers.Manage<GpuContractValue>().With(gpuManager).Register();
            builder.Steps.Add<DiagnosticSeed, GpuContractValue>(new GpuContractStep()).RunOn(gpu).ManagedBy(gpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/gpu-contract");
            ResourceHandle<GpuContractValue> value = scope.Create<DiagnosticSeed, GpuContractValue>("gpu", new("device generationを更新してtargeted invalidation"));
            await value.Ready;
            return $"manager={manager!.Id}; generation={value.Version}; {value.Value.Description}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/Assets/DocumentInspector", Order = 30)]
    public static Widget DocumentInspector(StoryContext ctx)
    {
        return new ResourceScenarioWidget("Asset診断", (builder, h) =>
        {
            builder.Steps.Add<AssetDocument, DiagnosticResult>(new DiagnosticStep(document =>
                $"シーン={document.Scenes.Count}、ノード={document.Nodes.Count}、メッシュ={document.Meshes.Count}、マテリアル={document.Materials.Count}"))
                .RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/assets");
            ResourceHandle<DiagnosticResult> result = scope.Create<AssetDocument, DiagnosticResult>("document", FixtureDocument());
            await result.Ready;
            return result.Value.Text;
        }, ctx.Log);
    }

    [Story("Examples/Resources/Assets/MeshPrimitiveInspector", Order = 31)]
    public static Widget MeshPrimitiveInspector(StoryContext ctx)
    {
        return new ResourceScenarioWidget("Asset診断", (builder, h) =>
        {
            builder.Steps.Add<AssetDocument, DiagnosticResult>(new DiagnosticStep(document =>
            {
                AssetPrimitive primitive = document.Meshes[0].Primitives[0];
                return $"頂点={primitive.Attributes.Positions.Length}; index={primitive.Indices?.Length ?? 0}";
            })).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/assets");
            ResourceHandle<DiagnosticResult> result = scope.Create<AssetDocument, DiagnosticResult>("primitive", FixtureDocument());
            await result.Ready;
            return result.Value.Text;
        }, ctx.Log);
    }

    [Story("Examples/Resources/Assets/MaterialTextureInspector", Order = 32)]
    public static Widget MaterialTextureInspector(StoryContext ctx)
    {
        return new ResourceScenarioWidget("Asset診断", (builder, h) =>
        {
            builder.Steps.Add<AssetDocument, DiagnosticResult>(new DiagnosticStep(document =>
                $"基本色={document.Materials[0].BaseColorFactor}; UV={document.Materials[0].BaseColorTexture!.TexCoordSet}"))
                .RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/assets");
            ResourceHandle<DiagnosticResult> result = scope.Create<AssetDocument, DiagnosticResult>("material", FixtureDocument());
            await result.Ready;
            return result.Value.Text;
        }, ctx.Log);
    }

    [Story("Examples/Resources/Assets/AnimatedSceneGraph", Order = 33)]
    public static Widget AnimatedSceneGraph(StoryContext ctx)
    {
        return new ResourceScenarioWidget("Asset runtime診断", (builder, h) =>
        {
            builder.Steps.Add<DiagnosticSeed, DiagnosticResult>(new DiagnosticSeedStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/assets");
            ResourceHandle<DiagnosticResult> result = scope.Create<DiagnosticSeed, DiagnosticResult>("animated", new("サンプリング → 伝播 → 抽出"));
            await result.Ready;
            return result.Value.Text;
        }, ctx.Log);
    }

    [Story("Examples/Resources/Assets/ShaderBufferInspector", Order = 34)]
    public static Widget ShaderBufferInspector(StoryContext ctx)
    {
        return new ResourceScenarioWidget("Asset runtime診断", (builder, h) =>
        {
            builder.Steps.Add<DiagnosticSeed, DiagnosticResult>(new DiagnosticSeedStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            using ResourceScope scope = resources.CreateScope("example/assets");
            ResourceHandle<DiagnosticResult> result = scope.Create<DiagnosticSeed, DiagnosticResult>("shader-abi",
                new($"MaterialGpuData={Marshal.SizeOf<MaterialGpuData>()}; manager metadata=allocation/index/generation"));
            await result.Ready;
            return result.Value.Text;
        }, ctx.Log);
    }

    [Story("Examples/Resources/Gltf/BoxDocumentLoad", Order = 40)]
    public static Widget BoxDocumentLoad(StoryContext ctx)
    {
        const string path = "Box.gltf";
        return new ResourceScenarioWidget("glTF document load", (builder, h) =>
        {
            builder.Sources.Add(new FileSource(Files((path, TriangleGltf)))).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
            builder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            using ResourceHandle<AssetDocument> document = resources.Load<AssetDocument>(path);
            await document.Ready;
            return $"形式={document.Value.SourceFormat}; mesh={document.Value.Meshes.Count}; node={document.Value.Nodes.Count}";
        }, ctx.Log);
    }

    [Story("Examples/Resources/Gltf/ExternalBufferTrace", Order = 41)]
    public static Widget ExternalBufferTrace(StoryContext ctx) => new ResourceScenarioWidget("glTF外部buffer", (builder, h) =>
    {
        builder.Sources.Add(new FileSource(BinaryTriangleFiles())).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
    }, async resources =>
    {
        using ResourceHandle<AssetDocument> scene = resources.Load<AssetDocument>("models/scene.gltf");
        await scene.Ready;
        return $"解決先={new ResourceUri("models/scene.gltf").Resolve("buffers/geometry.bin").Url}; mesh={scene.Value.Meshes.Count}";
    }, ctx.Log);

    [Story("Examples/Resources/Gltf/MalformedAccessorDiagnostics", Order = 42)]
    public static Widget MalformedAccessorDiagnostics(StoryContext ctx) => new ResourceScenarioWidget("glTF診断", (builder, h) =>
    {
        builder.Sources.Add(new FileSource(Files(("broken.gltf", MalformedGltf)))).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
        builder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
    }, async resources =>
    {
        using ResourceHandle<AssetDocument> broken = resources.Load<AssetDocument>("broken.gltf");
        try { await broken.Ready; return "診断なし"; }
        catch (Exception error) { return $"診断={error.GetBaseException().Message}"; }
    }, ctx.Log);

    [Story("Examples/Resources/Gltf/ExternalDependencyReload", Order = 43)]
    public static Widget ExternalDependencyReload(StoryContext ctx)
    {
        MemoryFileSystem files = BinaryTriangleFiles();
        return new ResourceScenarioWidget("glTF依存reload", (builder, h) =>
        {
            builder.Sources.Add(new FileSource(files)).RunOn(h.IoDomain).ManagedBy(h.IoManager).Register();
            builder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep()).RunOn(h.CpuDomain).ManagedBy(h.CpuManager).Register();
        }, async resources =>
        {
            resources.Watch();
            using ResourceHandle<AssetDocument> scene = resources.Load<AssetDocument>("models/scene.gltf");
            await scene.Ready;
            int before = scene.Version;
            files.Set("models/buffers/geometry.bin", TriangleBinary(.75f));
            await PumpUntil(resources, () => scene.Version > before);
            return $"generation={before}→{scene.Version}";
        }, ctx.Log);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(5);
        if (!condition()) throw new TimeoutException("retirement did not complete");
    }

    private sealed record GpuContractValue(string Description);
    private sealed class GpuContractStep : IResourceStep<DiagnosticSeed, GpuContractValue>
    {
        public Task<GpuContractValue> RunAsync(DiagnosticSeed input, ResourceUri uri, LoadContext ctx) => Task.FromResult(new GpuContractValue(input.Text));
    }

    private sealed class SerializedProbeStep : IResourceStep<RuntimeSeed, RuntimeLabel>
    {
        private int _active;
        public List<int> Order { get; } = [];
        public int MaxActive { get; private set; }
        public async Task<RuntimeLabel> RunAsync(RuntimeSeed input, ResourceUri uri, LoadContext ctx)
        {
            int active = Interlocked.Increment(ref _active);
            MaxActive = Math.Max(MaxActive, active);
            Order.Add(input.Level);
            await Task.Delay(5, ctx.Token);
            Interlocked.Decrement(ref _active);
            return new($"レベル {input.Level}");
        }
    }

    private sealed class TrackingManager(ResourceManagerId id) : CpuResourceManager(id)
    {
        public long Adopted => CaptureSnapshot().AdoptedCount;
        public long Retired => CaptureSnapshot().RetiredCount;
    }

    private sealed class CooperativeDemoDomain(ResourceExecutionDomainId id, ResourceExecutionDomainCapabilities capabilities) : IResourceExecutionDomain
    {
        private long _completed;
        public ResourceExecutionDomainId Id { get; } = id;
        public ResourceExecutionDomainCapabilities Capabilities { get; } = capabilities;
        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public async ValueTask<object> DispatchAsync(Func<CancellationToken, ValueTask<object>> work, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            object value = await work(cancellationToken);
            Interlocked.Increment(ref _completed);
            return value;
        }
        public ResourceExecutionDomainSnapshot CaptureSnapshot() => new(Id, 0, 0, Interlocked.Read(ref _completed), TimeSpan.Zero, TimeSpan.Zero);
        public ValueTask ShutdownAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
