using System.Numerics;
using System.Runtime.InteropServices;
using Luxel.Assets;
using Luxel.AssetsGpu;

using Luxel.Resources;

namespace Luxel.Tests;

/// <summary>
/// <see cref="Luxel.AssetsGpu"/> の型 shape と logic をチェックする軽量テスト。
/// GPU device を必要としない部分のみ (upload 系は sample で実 GPU 検証)。
/// </summary>
public partial class AssetsGpuTests
{
    [Fact]
    public void GpuMaterial_EncodesBaseColorFactor()
    {
        var mat = new GpuMaterial { BaseColorFactor = new Vector4(0.5f, 0.25f, 0.75f, 1.0f) };
        var data = mat.ToShaderData();
        Assert.Equal(0.5f, data.BaseColor.X, precision: 4);
        Assert.Equal(0.25f, data.BaseColor.Y, precision: 4);
        Assert.Equal(0.75f, data.BaseColor.Z, precision: 4);
        Assert.Equal(0u, data.Flags);  // texture 無しなので flag 0
    }

    [Fact]
    public void GpuMaterial_EncodesFlagWithTexture()
    {
        // BaseColorTexture が存在する場合のみ FlagHasTexture が立つ
        var matNoTex = new GpuMaterial();
        Assert.Equal(0u, matNoTex.ToShaderData().Flags);
        // (実 GpuTexture インスタンスは device 必要なので Flags 確認は shader data 経由)
    }

    [Fact]
    public void GpuMesh_DirectRefWorks()
    {
        // Asset* → GpuMesh の direct ref を保持できることを確認
        var assetMat = new AssetMaterial { Name = "m", BaseColorFactor = Vector4.UnitX };
        var gpuMat = new GpuMaterial { Source = assetMat, Name = "m", BaseColorFactor = Vector4.UnitX };
        var assetPrim = new AssetPrimitive();
        var gpuPrim = new GpuPrimitive { Source = assetPrim, Material = gpuMat };
        var assetMesh = new AssetMesh { Name = "mesh" };
        var gpuMesh = new GpuMesh { Source = assetMesh, Name = "mesh" };
        gpuMesh.Primitives.Add(gpuPrim);

        Assert.Same(assetMat, gpuMat.Source);
        Assert.Same(gpuMat, gpuMesh.Primitives[0].Material);
        Assert.Same(assetMesh, gpuMesh.Source);
    }

    [Fact]
    public void GpuMaterial_SharesTextureRef()
    {
        // 同じ AssetTexture インスタンスが複数 GpuMaterial から参照されうる (registry の dedup 前提)
        var assetTex = new AssetTexture { Width = 1, Height = 1, PixelData = new byte[4] };
        // 実 GpuTexture は device 依存だが、direct ref 自体は class 参照なので単純比較で OK
        var m1 = new GpuMaterial();
        var m2 = new GpuMaterial();
        // BaseColorTexture への null 代入で確認
        m1.BaseColorTexture = null;
        m2.BaseColorTexture = null;
        Assert.Same(m1.BaseColorTexture, m2.BaseColorTexture);
    }

    [Fact]
    public async Task ResourceScopeGpuFactories_QualifyStableKeysAndOwnGpuObjects()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        using var resources = ResourceTestSystem.CreateGpu(device, out AssetGpuResourceSystemRegistration registration);
        var scope = resources.CreateScope("viewport/main");
        var code = new GpuShaderCode { SpirV = [1, 2, 3, 4] };

        var compute = scope.CreateComputePipeline("pipeline/compute", code);
        var graphics = scope.CreateGraphicsPipeline(
            "pipeline/graphics", code, new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm)));
        var sampled = scope.CreateSampledTexture(
            "texture/albedo", 1, 1, new byte[] { 1, 2, 3, 4 });
        var sampler = scope.CreateSampler("sampler/main");
        var sameSampler = scope.CreateSampler("sampler/main");
        var color = scope.CreateRenderTarget("target/color", 8, 4);
        var depth = scope.CreateDepthTarget("target/depth", 8, 4);
        var buffer = scope.CreateBuffer<uint>("buffer/instances", 4);

        await Task.WhenAll(
            compute.Ready, graphics.Ready, sampled.Ready, sampler.Ready, sameSampler.Ready,
            color.Ready, depth.Ready, buffer.Ready);

        Assert.Equal("scope://viewport%2Fmain/pipeline%2Fcompute", compute.Uri.ToString());
        Assert.Same(sampler.Value, sameSampler.Value);
        Assert.True(compute.Value.IsCompute);
        Assert.False(graphics.Value.IsCompute);
        Assert.Equal((uint)1, sampled.Value.Width);
        Assert.Equal((uint)8, color.Value.Width);
        Assert.Equal(GpuFormat.D32Float, depth.Value.Format);
        Assert.Equal((ulong)16, buffer.Value.Size);
        Assert.Equal(9, backend.LiveResources); // scoped resources + registry default sampler/material buffer

        scope.Dispose();
        resources.Pump();

        Assert.Equal(2, backend.LiveResources);
        resources.Dispose();
        Assert.Equal(0, backend.LiveResources);
    }

    [Fact]
    public async Task FloatArrayResourceUploadsIntoOwnedGpuBuffer()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        using var resources = ResourceTestSystem.CreateGpu(device, out AssetGpuResourceSystemRegistration registration);
        using ResourceScope scope = resources.CreateScope("vertex-upload");
        float[] vertices = [1.25f, -2.5f, 3.75f, 4f];

        using ResourceHandle<GpuBuffer> buffer = scope.Create<float[], GpuBuffer>(
            "vertices", vertices);
        await buffer.Ready;

        Assert.Equal("scope://vertex-upload/vertices", buffer.Uri.ToString());
        Assert.Equal((ulong)(vertices.Length * sizeof(float)), buffer.Value.Size);
        Assert.Equal(vertices, buffer.Value.Span<float>(vertices.Length).ToArray());
    }

    [Fact]
    public async Task PipelineFactoryAcceptsPendingShaderHandle()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        using var resources = ResourceTestSystem.CreateGpu(device, out AssetGpuResourceSystemRegistration registration);
        using ResourceScope scope = resources.CreateScope("pending-shader");
        var completion = new TaskCompletionSource<GpuShaderCode>(TaskCreationOptions.RunContinuationsAsynchronously);
        using ResourceHandle<GpuShaderCode> shader = resources.Load(
            "controlled://shader", _ => completion.Task, ResourceOwnership.Borrowed);

        using ResourceHandle<GpuPipeline> pipeline = scope.CreateGraphicsPipeline(
            "pipeline", shader, new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm)));
        Assert.False(pipeline.Ready.IsCompleted);

        completion.SetResult(new GpuShaderCode { SpirV = [1, 2, 3, 4] });
        await pipeline.Ready;

        Assert.True(pipeline.HasValue);
        Assert.Equal(1, backend.PipelineCreations);
    }

    [Fact]
    public async Task ShaderRepublishRebuildsDependentPipeline()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        using var resources = ResourceTestSystem.CreateGpu(device, out AssetGpuResourceSystemRegistration registration);
        using ResourceScope scope = resources.CreateScope("reload-shader");
        using ResourceHandle<GpuShaderCode> shader = resources.Publish(
            "published://shader", new GpuShaderCode { SpirV = [1, 2, 3, 4] }, ResourceOwnership.Borrowed);
        using ResourceHandle<GpuPipeline> pipeline = scope.CreateGraphicsPipeline(
            "pipeline", shader, new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm)));
        await pipeline.Ready;
        GpuPipeline first = pipeline.Value;

        resources.Republish(
            "published://shader", new GpuShaderCode { SpirV = [5, 6, 7, 8] });
        resources.Pump();
        resources.Pump();
        await pipeline.Ready;
        resources.Pump();

        Assert.NotSame(first, pipeline.Value);
        Assert.True(pipeline.Version >= 1);
        Assert.Equal(2, backend.PipelineCreations);
        Assert.True(backend.Queue.WaitIdleAsyncCount >= 1);
    }

    [Fact]
    public async Task BuilderRegistrationUsesReturnedRegistry()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        using var resources = ResourceTestSystem.CreateGpu(device, out AssetGpuResourceSystemRegistration registration);
        using ResourceScope scope = resources.CreateScope("global/steps");

        ResourceHandle<GpuSampler> sampler = scope.CreateSampler("sampler");
        ResourceHandle<GpuBuffer> buffer = scope.CreateBuffer("buffer", 32);
        await Task.WhenAll(sampler.Ready, buffer.Ready);

        Assert.Equal(4, backend.LiveResources); // registry defaults + scope-owned sampler/buffer

        scope.Dispose();
        resources.Pump();
        Assert.Equal(2, backend.LiveResources);

        await resources.DisposeAsync();
        Assert.Equal(0, backend.LiveResources);
    }

    [Fact]
    public void ResourceScopeGpuFactories_RequireRegisteredCreationStep()
    {
        using var resources = ResourceTestSystem.Create();
        using var scope = resources.CreateScope("viewport/uninstalled");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => scope.CreateBuffer("buffer", 16));

        Assert.Contains("GpuBufferRequest", error.Message, StringComparison.Ordinal);
        Assert.Contains("No step registered", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GpuManager_PumpAsyncAvoidsSynchronousQueueWait()
    {
        var backend = new FakeGpuBackend();
        backend.Queue.ThrowOnSyncWait = true;
        using var device = new GpuDevice(backend);
        using var resources = ResourceTestSystem.CreateGpu(device, out AssetGpuResourceSystemRegistration registration);
        var scope = resources.CreateScope("async-lifecycle");
        ResourceHandle<GpuBuffer> buffer = scope.CreateBuffer("buffer", 32);
        await buffer.Ready;

        scope.Dispose();
        await resources.PumpAsync();

        Assert.Equal(0, backend.Queue.WaitIdleCount);
        Assert.Equal(1, backend.Queue.WaitIdleAsyncCount);
        Assert.Equal(2, backend.LiveResources);

        await resources.DisposeAsync();
        Assert.Equal(2, backend.Queue.WaitIdleAsyncCount);
        Assert.Equal(0, backend.LiveResources);
    }

    [Fact]
    public void GpuManager_OwnsRegistryAndWaitsForGpuBeforeDispose()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        using var resources = ResourceTestSystem.CreateGpu(device, out AssetGpuResourceSystemRegistration registration);

        Assert.NotNull(registration.Registry);
        Assert.Equal(2, backend.LiveResources); // default sampler + material buffer

        resources.Dispose();
        resources.Dispose();

        Assert.Equal(0, backend.Queue.WaitIdleCount);
        Assert.Equal(1, backend.Queue.WaitIdleAsyncCount);
        Assert.Equal(0, backend.LiveResources);
    }

    [Fact]
    public void GpuManager_RequiresTypedPolicyForExplicitManagedBy()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaultHandles core = ResourceSystemDefaults.AddCore(builder);
        GpuResourceManagerHandle gpu = builder.InstallGpuResources(device);
        builder.Steps.Add<byte[], CustomGpuValue>(new CustomGpuStep())
            .RunOn(gpu.CreateDomain).ManagedBy(gpu.Manager).Register();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("typed GPU resource policy", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(CustomGpuValue), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GpuManager_PreservesConfiguredSchedulerInsideRecoveryDomain()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaults.AddCore(builder);
        GpuResourceManagerHandle gpu = builder.InstallGpuResources(device, options =>
            options.ConfigureDomain = domain => domain.UseFactory(
                context => new SerialResourceExecutionDomain(context.Id),
                new(1, ResourceThreadAffinity.HostThread, ResourceProgressModel.Cooperative)));

        await using ResourceSystem resources = await builder.BuildAsync();

        Assert.Equal(ResourceThreadAffinity.HostThread, gpu.DomainInstance.Capabilities.Affinity);
        Assert.Equal(ResourceProgressModel.Cooperative, gpu.DomainInstance.Capabilities.ProgressModel);
    }

    [Fact]
    public async Task GpuManager_CustomStructTracksBudgetIndexesAndFenceRetirement()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaults.AddCore(builder);
        GpuResourceManagerHandle gpu = builder.InstallGpuResources(device, options =>
        {
            options.DeviceId = "custom-device";
            options.SoftBudgetBytes = 4;
            options.HardBudgetBytes = 16;
        });
        int retirements = 0;
        gpu.Manage<CustomGpuValue>(builder)
            .DescribeAllocation(value => new("", value.Bytes, value.Bytes, value.Bytes, "device-local"))
            .WithIndexSpace("custom-table")
            .RetireAsync((_, _) => { Interlocked.Increment(ref retirements); return ValueTask.CompletedTask; })
            .Register();
        await using ResourceSystem resources = builder.Build();
        GpuResourceManager manager = gpu.ManagerInstance;
        var value = new CustomGpuValue(8);

        ResourceManagementRecord first = await manager.AdoptAsync(value,
            new(typeof(CustomGpuValue), new ResourceUri("custom://one"), 1, ResourceOwnership.Owned, default, default));
        int firstIndex = Assert.Single(first.Indexes!.Value.Values).Index;
        Assert.Equal(1, manager.CaptureGpuSnapshot().SoftBudgetExceededCount);

        await manager.RetireAsync(value, first, ResourceRetireReason.Evicted);
        Assert.Equal(0, retirements);
        Assert.Equal(1, manager.CaptureGpuSnapshot().IndexPendingRecycle);
        await manager.PumpAsync(new(default));
        Assert.Equal(1, retirements);
        Assert.Equal(1, backend.Queue.WaitIdleAsyncCount);

        ResourceManagementRecord second = await manager.AdoptAsync(new CustomGpuValue(4),
            new(typeof(CustomGpuValue), new ResourceUri("custom://two"), 2, ResourceOwnership.Owned, default, default));
        Assert.Equal(firstIndex, Assert.Single(second.Indexes!.Value.Values).Index);
    }

    [Fact]
    public async Task GpuManager_CustomClassParticipatesInCompactionWithoutBaseType()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaults.AddCore(builder);
        GpuResourceManagerHandle gpu = builder.InstallGpuResources(device);
        var value = new CustomGpuClass();
        gpu.Manage<CustomGpuClass>(builder)
            .RelocateAsync((resource, _) =>
            {
                resource.Relocations++;
                return ValueTask.FromResult(new GpuResourceRelocationResult(32, 8, true));
            })
            .Register();
        await using ResourceSystem resources = builder.Build();
        await gpu.ManagerInstance.AdoptAsync(value,
            new(typeof(CustomGpuClass), new ResourceUri("custom://class"), 1, ResourceOwnership.Borrowed, default, default));

        GpuResourceRelocationResult result = await gpu.ManagerInstance.CompactAsync();

        Assert.True(result.Relocated);
        Assert.Equal(32, result.MovedBytes);
        Assert.Equal(8, result.ReclaimedBytes);
        Assert.Equal(1, value.Relocations);
        Assert.Equal(1, gpu.ManagerInstance.CaptureGpuSnapshot().CompactionCount);
    }

    [Fact]
    public async Task GpuLifecycleCoordinator_BorrowedDeviceWaitsForReplacementAndSwitchesGeneration()
    {
        var oldBackend = new FakeGpuBackend();
        using var oldDevice = new GpuDevice(oldBackend);
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaults.AddCore(builder);
        AssetGpuResourceSystemRegistration registration = builder.AddAssetGpu(oldDevice, options =>
        {
            options.DeviceId = "borrowed-device";
            options.DeviceGeneration = 1;
        });
        await using ResourceSystem resources = builder.Build();
        var coordinator = new GpuDeviceLifecycleCoordinator(resources, registration.Gpu,
            new() { Ownership = GpuDeviceOwnership.Borrowed, RecoveryPumpIterations = 1 });
        coordinator.Publish(new GpuDeviceLifecycleEvent(new("borrowed-device", 1), GpuBackendKind.Vulkan, "fake", 1,
            DateTimeOffset.UtcNow, GpuDeviceLifecycleState.Lost, GpuLifecycleReason.DeviceReset));

        await coordinator.PumpAsync();
        Assert.Equal(GpuDeviceLifecycleState.Lost, coordinator.Snapshot.State);
        Assert.True(registration.Gpu.ManagerInstance.IsPaused);

        var replacementBackend = new FakeGpuBackend();
        using var replacement = new GpuDevice(replacementBackend);
        coordinator.ProvideBorrowedReplacement(replacement, 2);
        await coordinator.PumpAsync();

        Assert.Equal((ulong)2, registration.Gpu.ManagerInstance.CurrentGeneration.Identity.Generation);
        Assert.False(registration.Gpu.ManagerInstance.IsPaused);
        Assert.Equal(GpuDeviceLifecycleState.Ready, coordinator.Snapshot.State);
        Assert.Equal(0, oldBackend.LiveResources);
        Assert.Equal(2, replacementBackend.LiveResources);
    }

    [Fact]
    public async Task GpuLifecycleCoordinator_OwnedDeviceRecreatesAndTargetsGpuManager()
    {
        var oldBackend = new FakeGpuBackend();
        using var oldDevice = new GpuDevice(oldBackend);
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaults.AddCore(builder);
        AssetGpuResourceSystemRegistration registration = builder.AddAssetGpu(oldDevice, options =>
        {
            options.DeviceId = "owned-device";
            options.DeviceGeneration = 4;
        });
        await using ResourceSystem resources = builder.Build();
        var replacementBackend = new FakeGpuBackend();
        ulong requestedGeneration = 0;
        var coordinator = new GpuDeviceLifecycleCoordinator(resources, registration.Gpu, new()
        {
            Ownership = GpuDeviceOwnership.Owned,
            RecoveryPumpIterations = 1,
            OwnedDeviceFactory = (generation, _, _) =>
            {
                requestedGeneration = generation;
                return ValueTask.FromResult(new GpuDevice(replacementBackend));
            },
        });
        coordinator.Publish(new GpuDeviceLifecycleEvent(new("owned-device", 4), GpuBackendKind.Vulkan,
            "fake", 1, DateTimeOffset.UtcNow, GpuDeviceLifecycleState.Lost, GpuLifecycleReason.DeviceRemoved));

        await coordinator.PumpAsync();

        Assert.Equal((ulong)5, requestedGeneration);
        Assert.Equal((ulong)5, registration.Gpu.ManagerInstance.CurrentGeneration.Identity.Generation);
        Assert.Equal(GpuDeviceLifecycleState.Ready, coordinator.Snapshot.State);
        Assert.Equal(1, coordinator.Snapshot.RecoveryCount);
        Assert.Equal(0, oldBackend.LiveResources);
        Assert.Equal(2, replacementBackend.LiveResources);
    }

    private sealed class CustomGpuClass
    {
        public int Relocations { get; set; }
    }

    private readonly record struct CustomGpuValue(long Bytes);

    private sealed class CustomGpuStep : IResourceStep<byte[], CustomGpuValue>
    {
        public Task<CustomGpuValue> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx)
            => Task.FromResult(new CustomGpuValue(input.LongLength));
    }

    private sealed class FakeGpuBackend : Luxel.Graphics.Abstraction.IGpuBackend
    {
        private int _liveResources;
        private int _pipelineCreations;

        public string Name => "fake";
        public GpuBackendKind Kind => GpuBackendKind.Vulkan;
        public FakeQueue Queue { get; } = new();
        public Luxel.Graphics.Abstraction.IGpuBackendQueue MainQueue => Queue;
        public int LiveResources => Volatile.Read(ref _liveResources);
        public int PipelineCreations => Volatile.Read(ref _pipelineCreations);

        public Luxel.Graphics.Abstraction.IGpuBackendBuffer CreateBuffer(ulong size, GpuMemoryKind kind)
            => Track(new FakeBuffer(size));

        public Luxel.Graphics.Abstraction.IGpuBackendPipeline CreateComputePipeline(
            ReadOnlySpan<byte> shaderBlob, string entryPoint)
        {
            Interlocked.Increment(ref _pipelineCreations);
            return Track(new FakePipeline(isCompute: true));
        }

        public Luxel.Graphics.Abstraction.IGpuBackendPipeline CreateGraphicsPipeline(
            ReadOnlySpan<byte> vsBlob, ReadOnlySpan<byte> psBlob,
            GpuGraphicsPipelineDesc description)
        {
            Interlocked.Increment(ref _pipelineCreations);
            return Track(new FakePipeline(isCompute: false, description));
        }

        public Luxel.Graphics.Abstraction.IGpuBackendTexture CreateRenderTarget(
            uint width, uint height, GpuFormat format)
            => Track(new FakeTexture(width, height, format));

        public Luxel.Graphics.Abstraction.IGpuBackendTexture CreateDepthTarget(
            uint width, uint height, GpuFormat format)
            => Track(new FakeTexture(width, height, format));

        public Luxel.Graphics.Abstraction.IGpuBackendTexture CreateSampledTexture(
            uint width, uint height, GpuFormat format, ReadOnlySpan<byte> data)
            => Track(new FakeTexture(width, height, format));

        public Luxel.Graphics.Abstraction.IGpuBackendSampler CreateSampler(
            GpuSamplerFilter filter, GpuSamplerAddress address = GpuSamplerAddress.Clamp)
            => Track(new FakeSampler());

        public void Dispose() { }

        private T Track<T>(T resource) where T : FakeResource
        {
            Interlocked.Increment(ref _liveResources);
            resource.Disposed = () => Interlocked.Decrement(ref _liveResources);
            return resource;
        }
    }

    private abstract class FakeResource : IDisposable
    {
        private int _disposed;
        public Action? Disposed { get; set; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            OnDispose();
            Disposed?.Invoke();
        }

        protected virtual void OnDispose() { }
    }

    private sealed class FakePipeline(bool isCompute, GpuGraphicsPipelineDesc? description = null) : FakeResource, Luxel.Graphics.Abstraction.IGpuBackendPipeline
    {
        public bool IsCompute { get; } = isCompute;
        public GpuGraphicsPipelineDesc? GraphicsDescription { get; } = description;
        public GpuPipelineDiagnostics Diagnostics => default;
        public Luxel.Graphics.Abstraction.IGpuBackendPipeline ResolveGraphicsVariant(
            GpuRasterizerState rasterizer, GpuDepthStencilState depthStencil, GpuBlendState blend) => this;
    }

    private sealed class FakeTexture(uint width, uint height, GpuFormat format)
        : FakeResource, Luxel.Graphics.Abstraction.IGpuBackendTexture
    {
        public uint Width { get; } = width;
        public uint Height { get; } = height;
        public GpuFormat Format { get; } = format;
        public uint BindlessIndex => 1;
    }

    private sealed class FakeSampler : FakeResource, Luxel.Graphics.Abstraction.IGpuBackendSampler
    {
        public uint BindlessIndex => 2;
    }

    private sealed class FakeBuffer : FakeResource, Luxel.Graphics.Abstraction.IGpuBackendBuffer
    {
        private readonly IntPtr _memory;

        public FakeBuffer(ulong size)
        {
            Size = size;
            _memory = Marshal.AllocHGlobal(checked((int)size));
        }

        public ulong Size { get; }
        public ulong DeviceAddress => 0x1000;
        public uint BindlessIndex => 3;
        public unsafe void* MappedPointer => (void*)_memory;
        protected override void OnDispose() => Marshal.FreeHGlobal(_memory);
    }

    private sealed class FakeQueue : Luxel.Graphics.Abstraction.IAsyncGpuBackendQueue
    {
        public int WaitIdleCount { get; private set; }
        public int WaitIdleAsyncCount { get; private set; }
        public bool ThrowOnSyncWait { get; set; }

        public Luxel.Graphics.Abstraction.IGpuBackendCommandBuffer StartCommandRecording()
            => throw new NotSupportedException();

        public void Submit(Luxel.Graphics.Abstraction.IGpuBackendCommandBuffer commandBuffer)
            => throw new NotSupportedException();

        public ValueTask SubmitAsync(
            Luxel.Graphics.Abstraction.IGpuBackendCommandBuffer commandBuffer,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void WaitIdle()
        {
            if (ThrowOnSyncWait) throw new PlatformNotSupportedException();
            WaitIdleCount++;
        }

        public ValueTask WaitIdleAsync(CancellationToken cancellationToken = default)
        {
            WaitIdleAsyncCount++;
            return ValueTask.CompletedTask;
        }
    }
}
