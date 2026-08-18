using System.Runtime.InteropServices;
using Luxel.Controls;
using Luxel.Graphics.Abstraction;

namespace Luxel.Tests;

public sealed class RgbaImagePresenterTests
{
    [Fact]
    public void SameSizeUpdate_ReusesBufferAndUploadsNewPixels()
    {
        using var backend = new RecordingBackend();
        using var device = new GpuDevice(backend);
        using var presenter = new RgbaImagePresenter();
        byte[] first = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[] second = [8, 7, 6, 5, 4, 3, 2, 1];

        Assert.True(presenter.Update(device, 2, 1, first));
        RecordingBuffer buffer = backend.LastBuffer;

        Assert.False(presenter.Update(device, 2, 1, second));
        Assert.Equal(1, backend.AllocationCount);
        Assert.False(buffer.IsDisposed);
        Assert.Equal(second, buffer.Bytes());
    }

    [Fact]
    public void Resize_ReplacesAndDisposesBuffer()
    {
        using var backend = new RecordingBackend();
        using var device = new GpuDevice(backend);
        using var presenter = new RgbaImagePresenter();

        Assert.True(presenter.Update(device, 1, 1, new byte[4]));
        RecordingBuffer first = backend.LastBuffer;

        Assert.True(presenter.Update(device, 2, 1, new byte[8]));
        Assert.Equal(2, backend.AllocationCount);
        Assert.True(first.IsDisposed);
        Assert.Equal(2, presenter.Width);
        Assert.Equal(1, presenter.Height);
    }

    [Fact]
    public void Dispose_ReleasesBufferAndClearsDimensions()
    {
        using var backend = new RecordingBackend();
        using var device = new GpuDevice(backend);
        var presenter = new RgbaImagePresenter();
        presenter.Update(device, 1, 1, new byte[4]);
        RecordingBuffer buffer = backend.LastBuffer;

        presenter.Dispose();
        presenter.Dispose();

        Assert.True(buffer.IsDisposed);
        Assert.Equal(0, presenter.Width);
        Assert.Equal(0, presenter.Height);
    }

    private sealed class RecordingBackend : IGpuBackend
    {
        private readonly RecordingQueue _queue = new();

        public string Name => nameof(RecordingBackend);
        public GpuBackendKind Kind => GpuBackendKind.Vulkan;
        public IGpuBackendQueue MainQueue => _queue;
        public int AllocationCount { get; private set; }
        public RecordingBuffer LastBuffer { get; private set; } = null!;

        public IGpuBackendBuffer CreateBuffer(ulong size, GpuMemoryKind kind)
        {
            AllocationCount++;
            LastBuffer = new RecordingBuffer(size, (uint)AllocationCount);
            return LastBuffer;
        }

        public IGpuBackendPipeline CreateComputePipeline(ReadOnlySpan<byte> shaderBlob, string entryPoint)
            => throw new NotSupportedException();
        public IGpuBackendPipeline CreateGraphicsPipeline(
            ReadOnlySpan<byte> vsBlob, ReadOnlySpan<byte> psBlob, GpuGraphicsPipelineDesc description)
            => throw new NotSupportedException();
        public IGpuBackendTexture CreateRenderTarget(uint width, uint height, GpuFormat format)
            => throw new NotSupportedException();
        public IGpuBackendTexture CreateDepthTarget(uint width, uint height, GpuFormat format)
            => throw new NotSupportedException();
        public IGpuBackendTexture CreateSampledTexture(
            uint width, uint height, GpuFormat format, ReadOnlySpan<byte> data)
            => throw new NotSupportedException();
        public IGpuBackendSampler CreateSampler(GpuSamplerFilter filter, GpuSamplerAddress address)
            => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed unsafe class RecordingBuffer(ulong size, uint bindlessIndex) : IGpuBackendBuffer
    {
        private void* _memory = NativeMemory.Alloc(checked((nuint)size));

        public ulong Size { get; } = size;
        public ulong DeviceAddress => 0;
        public uint BindlessIndex { get; } = bindlessIndex;
        public void* MappedPointer => _memory;
        public bool IsDisposed => _memory is null;

        public byte[] Bytes() => new ReadOnlySpan<byte>(_memory, checked((int)Size)).ToArray();

        public void Dispose()
        {
            if (_memory is null) return;
            NativeMemory.Free(_memory);
            _memory = null;
        }
    }

    private sealed class RecordingQueue : IGpuBackendQueue
    {
        public IGpuBackendCommandBuffer StartCommandRecording() => throw new NotSupportedException();
        public void Submit(IGpuBackendCommandBuffer commandBuffer) => throw new NotSupportedException();
        public void WaitIdle() { }
    }
}
