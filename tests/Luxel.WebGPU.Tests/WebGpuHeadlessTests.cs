using System.Text;
using Luxel.Graphics;
using Luxel.Graphics.WebGPU;
using LuxelWebGpuHeadless;

namespace Luxel.WebGPU.Tests;

public sealed class WebGpuHeadlessTests
{
    private const string RequireAdapterEnvironmentVariable = "LUXEL_WEBGPU_REQUIRE_ADAPTER";

    [Fact]
    public void DeviceCreation_ProvidesHeadlessBackend()
    {
        using var device = TryCreate();
        if (device is null) return;
        Assert.Contains("WebGPU", device.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<PlatformNotSupportedException>(() => device.CreateSurface(0, 64, 64));
    }

    [Fact]
    public void BufferCopy_ReadsBackHostCachedShadow()
    {
        using var device = TryCreate();
        if (device is null) return;
        using var source = device.Malloc(16, GpuMemoryKind.HostMapped);
        using var destination = device.Malloc(16, GpuMemoryKind.HostCached);
        Span<uint> sourceWords = source.Span<uint>();
        sourceWords[0] = 0x12345678;
        sourceWords[1] = 0x90abcdef;

        using var commands = device.MainQueue.StartCommandRecording();
        commands.CopyBuffer(source, destination, 16);
        commands.Finish();
        device.MainQueue.Submit(commands);

        Span<uint> result = destination.Span<uint>();
        Assert.Equal(0x12345678u, result[0]);
        Assert.Equal(0x90abcdefu, result[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedHeadlessSample_ValidatesComputeAndTriangle(bool setPipelineAfterBeginRendering)
    {
        using var device = TryCreate();
        if (device is null) return;

        HeadlessWebGpuResult result = HeadlessWebGpuSample.Run(device, setPipelineAfterBeginRendering);

        Assert.Equal(HeadlessWebGpuSample.ExpectedComputeValue, result.ComputeValue);
        Assert.True(result.Red > 200);
        Assert.True(result.Green > 200);
        Assert.True(result.Blue > 200);
        Assert.True(result.Alpha > 200);
    }


    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void OddSizedBuffers_UploadCopyAndReadBackLogicalBytes(int size)
    {
        using var device = TryCreate();
        if (device is null) return;
        using var source = device.Malloc((ulong)size, GpuMemoryKind.HostMapped);
        using var destination = device.Malloc((ulong)size, GpuMemoryKind.HostCached);
        for (int i = 0; i < size; i++) source.Span<byte>()[i] = (byte)(0xa0 + i);

        using var commands = device.MainQueue.StartCommandRecording();
        commands.CopyBuffer(source, destination, (ulong)size);
        commands.Finish();
        device.MainQueue.Submit(commands);

        Assert.Equal(source.Span<byte>().ToArray(), destination.Span<byte>().ToArray());
    }

    [Fact]
    public void ZeroSizedBuffer_IsRejected()
    {
        using var device = TryCreate();
        if (device is null) return;
        Assert.Throws<ArgumentOutOfRangeException>(() => device.Malloc(0));
    }

    [Fact]
    public void Arena_ReusesDisposedRangesUnderAllocationChurn()
    {
        using var device = TryCreate();
        if (device is null) return;
        uint firstIndex = uint.MaxValue;
        for (int i = 0; i < 96; i++)
        {
            using var buffer = device.Malloc(1024 * 1024, GpuMemoryKind.DeviceLocal);
            if (i == 0) firstIndex = buffer.BindlessIndex;
            else Assert.Equal(firstIndex, buffer.BindlessIndex);
        }
    }

    [Fact]
    public void ForeignAndDisposedObjects_AreRejectedBeforeNativeCalls()
    {
        using var first = TryCreate();
        using var second = TryCreate();
        if (first is null || second is null) return;
        using var firstBuffer = first.Malloc(4);
        using var secondBuffer = second.Malloc(4, GpuMemoryKind.HostCached);
        using var commands = first.MainQueue.StartCommandRecording();
        Assert.Throws<ArgumentException>(() => commands.CopyBuffer(firstBuffer, secondBuffer, 4));

        using var foreignCommands = second.MainQueue.StartCommandRecording();
        foreignCommands.Finish();
        Assert.Throws<ArgumentException>(() => first.MainQueue.Submit(foreignCommands));

        using var disposedBuffer = first.Malloc(4);
        disposedBuffer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => commands.CopyBuffer(disposedBuffer, firstBuffer, 4));

        using var foreignTarget = second.CreateRenderTarget(1, 1);
        Assert.Throws<ArgumentException>(() => commands.BeginRendering(foreignTarget));
        using var target = first.CreateRenderTarget(1, 1);
        target.Dispose();
        Assert.Throws<ObjectDisposedException>(() => commands.BeginRendering(target));

        var shader = new GpuShaderCode { Wgsl = Encoding.UTF8.GetBytes(ValidComputeShader) };
        using var foreignPipeline = second.CreateComputePipeline(shader);
        Assert.Throws<ArgumentException>(() => commands.SetComputePipeline(foreignPipeline));
        using var disposedPipeline = first.CreateComputePipeline(shader);
        disposedPipeline.Dispose();
        Assert.Throws<ObjectDisposedException>(() => commands.SetComputePipeline(disposedPipeline));
    }

    [Fact]
    public void DisposedBackend_RejectsQueueEntrypoints()
    {
        var device = TryCreate();
        if (device is null) return;
        GpuQueue queue = device.MainQueue;
        device.Dispose();
        Assert.Throws<ObjectDisposedException>(() => queue.StartCommandRecording());
        Assert.Throws<ObjectDisposedException>(() => queue.WaitIdle());
    }

    [Fact]
    public void TextureCopy_HeightOneAcceptsTightRow()
    {
        using var device = TryCreate();
        if (device is null) return;
        using var target = device.CreateRenderTarget(1, 1);
        using var readback = device.Malloc(4, GpuMemoryKind.HostCached);
        using var commands = device.MainQueue.StartCommandRecording();
        commands.BeginRendering(target, r: 0.25f, g: 0.5f, b: 0.75f).EndRendering()
            .CopyTextureToBuffer(target, readback);
        commands.Finish();
        device.MainQueue.Submit(commands);
        Assert.InRange(readback.Span<byte>()[0], 62, 65);
    }

    [Fact]
    public void TextureCopy_PaddedRowsUseFinalRowFootprintAndRejectNarrowStride()
    {
        using var device = TryCreate();
        if (device is null) return;
        using var target = device.CreateRenderTarget(1, 2);
        using var widerTarget = device.CreateRenderTarget(2, 1);
        using var exact = device.Malloc(260, GpuMemoryKind.HostCached);
        using var tooSmall = device.Malloc(259, GpuMemoryKind.HostCached);
        using var commands = device.MainQueue.StartCommandRecording();
        Assert.Throws<ArgumentException>(() => commands.CopyTextureToBuffer(target, exact, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => commands.CopyTextureToBuffer(widerTarget, exact, 1));
        Assert.Throws<ArgumentException>(() => commands.CopyTextureToBuffer(target, tooSmall, 64));
        commands.CopyTextureToBuffer(target, exact, 64);
    }

    [Fact]
    public void DisposedRecordedBuffer_IsNotReusedUntilSynchronousSubmitCompletes()
    {
        using var device = TryCreate();
        if (device is null) return;
        var source = device.Malloc(4, GpuMemoryKind.HostMapped);
        source.Span<uint>()[0] = 0xdecafbad;
        uint sourceIndex = source.BindlessIndex;
        using var destination = device.Malloc(4, GpuMemoryKind.HostCached);
        using var commands = device.MainQueue.StartCommandRecording();
        commands.CopyBuffer(source, destination, 4);
        source.Dispose();
        using var interim = device.Malloc(4);
        Assert.NotEqual(sourceIndex, interim.BindlessIndex);
        commands.Finish();
        device.MainQueue.Submit(commands);
        Assert.Equal(0xdecafbadu, destination.Span<uint>()[0]);
        interim.Dispose();
        using var reused = device.Malloc(4);
        Assert.Equal(sourceIndex, reused.BindlessIndex);
    }

    [Fact]
    public void InvalidWgsl_IsSurfacedAsManagedException()
    {
        using var device = TryCreate();
        if (device is null) return;
        var invalid = new GpuShaderCode { Wgsl = Encoding.UTF8.GetBytes("this is not wgsl") };
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => device.CreateComputePipeline(invalid));
        Assert.Contains("WebGPU", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SampledTextureAndSampler_ExposeStableLogicalIndicesAndValidateDispose()
    {
        using var device = TryCreate();
        if (device is null) return;
        using var firstTexture = device.CreateTexture(1, 1, new byte[] { 255, 0, 0, 255 });
        using var secondTexture = device.CreateTexture(1, 1, new byte[] { 0, 255, 0, 255 });
        using var firstSampler = device.CreateSampler(GpuSamplerFilter.Point);
        using var secondSampler = device.CreateSampler(GpuSamplerFilter.Linear, GpuSamplerAddress.Repeat);
        Assert.Equal(0u, firstTexture.BindlessIndex);
        Assert.Equal(1u, secondTexture.BindlessIndex);
        Assert.Equal(0u, firstSampler.BindlessIndex);
        Assert.Equal(1u, secondSampler.BindlessIndex);

        firstTexture.Dispose();
        firstSampler.Dispose();
        Assert.Throws<ObjectDisposedException>(() => firstTexture.BindlessIndex);
        Assert.Throws<ObjectDisposedException>(() => firstSampler.BindlessIndex);
        using var reusedTexture = device.CreateTexture(1, 1, new byte[] { 0, 0, 255, 255 });
        using var reusedSampler = device.CreateSampler(GpuSamplerFilter.Point);
        Assert.Equal(0u, reusedTexture.BindlessIndex);
        Assert.Equal(0u, reusedSampler.BindlessIndex);
    }

    [Fact]
    public void SampledResourceTables_RejectTheSeventeenthLiveResource()
    {
        using var device = TryCreate();
        if (device is null) return;
        var textures = new List<GpuTexture>();
        var samplers = new List<GpuSampler>();
        try
        {
            for (int i = 0; i < WebGpuBackend.MaxSampledTextures; i++)
                textures.Add(device.CreateTexture(1, 1, new byte[] { (byte)i, 0, 0, 255 }));
            for (int i = 0; i < WebGpuBackend.MaxSamplers; i++)
                samplers.Add(device.CreateSampler(GpuSamplerFilter.Point));

            InvalidOperationException textureError = Assert.Throws<InvalidOperationException>(
                () => device.CreateTexture(1, 1, new byte[] { 0, 0, 0, 255 }));
            InvalidOperationException samplerError = Assert.Throws<InvalidOperationException>(
                () => device.CreateSampler(GpuSamplerFilter.Point));
            Assert.Contains("16", textureError.Message, StringComparison.Ordinal);
            Assert.Contains("16", samplerError.Message, StringComparison.Ordinal);
        }
        finally
        {
            foreach (GpuTexture texture in textures) texture.Dispose();
            foreach (GpuSampler sampler in samplers) sampler.Dispose();
        }
    }

    [Fact]
    public void DisposedSampledResources_AreNotReusedWhileRecordedBindGroupIsAlive()
    {
        using var device = TryCreate();
        if (device is null) return;
        var texture = device.CreateTexture(1, 1, new byte[] { 255, 255, 255, 255 });
        var sampler = device.CreateSampler(GpuSamplerFilter.Point);
        uint textureIndex = texture.BindlessIndex;
        uint samplerIndex = sampler.BindlessIndex;
        var commands = device.MainQueue.StartCommandRecording();
        texture.Dispose();
        sampler.Dispose();

        using var interimTexture = device.CreateTexture(1, 1, new byte[] { 0, 0, 0, 255 });
        using var interimSampler = device.CreateSampler(GpuSamplerFilter.Point);
        Assert.NotEqual(textureIndex, interimTexture.BindlessIndex);
        Assert.NotEqual(samplerIndex, interimSampler.BindlessIndex);

        commands.Dispose();
        interimTexture.Dispose();
        interimSampler.Dispose();
        using var reusedTexture = device.CreateTexture(1, 1, new byte[] { 255, 0, 255, 255 });
        using var reusedSampler = device.CreateSampler(GpuSamplerFilter.Point);
        Assert.Equal(textureIndex, reusedTexture.BindlessIndex);
        Assert.Equal(samplerIndex, reusedSampler.BindlessIndex);
    }

    private const string ValidComputeShader = """
        struct Root { values: vec4<u32> }
        @group(0) @binding(0) var<storage, read_write> arena: array<u32>;
        @group(0) @binding(1) var<uniform> root: Root;
        @compute @workgroup_size(1) fn main() { }
        """;

    private static GpuDevice? TryCreate()
    {
        try { return new GpuDevice(WebGpuBackend.Create()); }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            if (AdapterIsRequired())
                throw new InvalidOperationException(
                    $"WebGPU adapter/runtime is required because {RequireAdapterEnvironmentVariable} is set, but device creation failed.", exception);

            Console.WriteLine($"WebGPU test guarded because no adapter/runtime is available: {exception.Message}");
            return null;
        }
    }

    private static bool AdapterIsRequired()
        => Environment.GetEnvironmentVariable(RequireAdapterEnvironmentVariable) is string value
           && (value == "1" || bool.TryParse(value, out bool required) && required);

    private static bool IsUnavailable(Exception exception)
        => exception is WebGpuUnavailableException or FileNotFoundException or DllNotFoundException;
}
