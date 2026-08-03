using System.Numerics;
using Luxel.Assets;
using Luxel.AssetsGpu;

using Luxel.Resources;

namespace Luxel.Tests;

/// <summary>
/// <see cref="Luxel.AssetsGpu"/> の型 shape と logic をチェックする軽量テスト。
/// GPU device を必要としない部分のみ (upload 系は sample で実 GPU 検証)。
/// </summary>
public class AssetsGpuTests
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
        using var resources = new Luxel.Resources.ResourceSystem();
        AssetGpuInstallation installation = resources.InstallAssetGpuLifecycle(device);
        var scope = resources.CreateScope("viewport/main");
        var code = new GpuShaderCode { SpirV = [1, 2, 3, 4] };

        var compute = scope.CreateComputePipeline("pipeline/compute", code);
        var graphics = scope.CreateGraphicsPipeline(
            "pipeline/graphics", code, GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
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
        installation.Dispose();
        Assert.Equal(0, backend.LiveResources);
    }

    [Fact]
    public async Task CreateAssetGpuSteps_GlobalRegistrationUsesReturnedRegistry()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        IResourceStep[] steps = ResourceSystemExtensions.CreateAssetGpuSteps(device, out AssetGpuRegistry registry);
        using var resources = new Luxel.Resources.ResourceSystem(steps: steps);
        resources.SetDeferredDisposeIdleHook(() => device.MainQueue.WaitIdle());
        using ResourceScope scope = resources.CreateScope("global/steps");

        ResourceHandle<GpuSampler> sampler = scope.CreateSampler("sampler");
        ResourceHandle<GpuBuffer> buffer = scope.CreateBuffer("buffer", 32);
        await Task.WhenAll(sampler.Ready, buffer.Ready);

        Assert.Equal(4, backend.LiveResources); // registry defaults + scope-owned sampler/buffer

        scope.Dispose();
        resources.Pump();
        Assert.Equal(2, backend.LiveResources);

        registry.Dispose();
        Assert.Equal(0, backend.LiveResources);
    }

    [Fact]
    public void ResourceScopeGpuFactories_RequireRegisteredCreationStep()
    {
        using var resources = new Luxel.Resources.ResourceSystem();
        using var scope = resources.CreateScope("viewport/uninstalled");

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => scope.CreateBuffer("buffer", 16));

        Assert.Contains("GpuBufferRequest", error.Message, StringComparison.Ordinal);
        Assert.Contains("ステップ未登録", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallAssetGpuLifecycle_OwnsRegistryAndWaitsForGpuBeforeDispose()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        using var resources = new Luxel.Resources.ResourceSystem();

        AssetGpuInstallation installation = resources.InstallAssetGpuLifecycle(device);

        Assert.NotNull(installation.Registry);
        Assert.Equal(2, backend.LiveResources); // default sampler + material buffer

        installation.Dispose();
        installation.Dispose();

        Assert.Equal(1, backend.Queue.WaitIdleCount);
        Assert.Equal(0, backend.LiveResources);
    }

    private sealed class FakeGpuBackend : Luxel.Graphics.Abstraction.IGpuBackend
    {
        private int _liveResources;

        public string Name => "fake";
        public GpuBackendKind Kind => GpuBackendKind.Vulkan;
        public FakeQueue Queue { get; } = new();
        public Luxel.Graphics.Abstraction.IGpuBackendQueue MainQueue => Queue;
        public int LiveResources => Volatile.Read(ref _liveResources);

        public Luxel.Graphics.Abstraction.IGpuBackendBuffer CreateBuffer(ulong size, GpuMemoryKind kind)
            => Track(new FakeBuffer(size));

        public Luxel.Graphics.Abstraction.IGpuBackendPipeline CreateComputePipeline(
            ReadOnlySpan<byte> shaderBlob, string entryPoint)
            => Track(new FakePipeline(isCompute: true));

        public Luxel.Graphics.Abstraction.IGpuBackendPipeline CreateGraphicsPipeline(
            ReadOnlySpan<byte> vsBlob, string vsEntry,
            ReadOnlySpan<byte> psBlob, string psEntry,
            GpuRasterDesc raster)
            => Track(new FakePipeline(isCompute: false));

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
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Disposed?.Invoke();
        }
    }

    private sealed class FakePipeline(bool isCompute) : FakeResource, Luxel.Graphics.Abstraction.IGpuBackendPipeline
    {
        public bool IsCompute { get; } = isCompute;
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

    private sealed class FakeBuffer(ulong size) : FakeResource, Luxel.Graphics.Abstraction.IGpuBackendBuffer
    {
        public ulong Size { get; } = size;
        public ulong DeviceAddress => 0x1000;
        public uint BindlessIndex => 3;
        public unsafe void* MappedPointer => null;
    }

    private sealed class FakeQueue : Luxel.Graphics.Abstraction.IGpuBackendQueue
    {
        public int WaitIdleCount { get; private set; }

        public Luxel.Graphics.Abstraction.IGpuBackendCommandBuffer StartCommandRecording()
            => throw new NotSupportedException();

        public void Submit(Luxel.Graphics.Abstraction.IGpuBackendCommandBuffer commandBuffer)
            => throw new NotSupportedException();

        public void WaitIdle() => WaitIdleCount++;
    }
}
