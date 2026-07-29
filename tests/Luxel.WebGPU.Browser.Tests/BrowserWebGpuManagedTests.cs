using System.Text.Json;

namespace Luxel.WebGPU.Browser.Tests;

public sealed class BrowserWebGpuManagedTests
{
    [Fact]
    public async Task FactoryAndArenaExposeFixedAbi()
    {
        var interop = new FakeInterop();
        using var backend = await BrowserWebGpuBackend.CreateAsync(interop);
        using var first = backend.CreateBuffer(1, GpuMemoryKind.HostMapped);
        using var second = backend.CreateBuffer(257, GpuMemoryKind.DeviceLocal);
        Assert.Equal("WebGPU / fake", backend.Name);
        Assert.Equal(0UL, first.DeviceAddress);
        Assert.Equal(256UL, second.DeviceAddress);
        Assert.Equal(0u, first.BindlessIndex);
        Assert.Equal(1u, second.BindlessIndex);
        Assert.NotEqual(0, Pointer(first));
        Assert.Equal(0, Pointer(second));
    }

    [Fact]
    public async Task FreedArenaRangeIsReusedAndForeignResourcesAreRejected()
    {
        var interop = new FakeInterop();
        using var a = await BrowserWebGpuBackend.CreateAsync(interop);
        using var b = await BrowserWebGpuBackend.CreateAsync(interop);
        var buffer = a.CreateBuffer(16, GpuMemoryKind.DeviceLocal);
        Assert.Equal(0UL, buffer.DeviceAddress);
        buffer.Dispose();
        using var reused = a.CreateBuffer(16, GpuMemoryKind.DeviceLocal);
        Assert.Equal(0UL, reused.DeviceAddress);
        using var foreign = b.CreateRenderTarget(1, 1, GpuFormat.Rgba8Unorm);
        using var command = a.MainQueue.StartCommandRecording();
        Assert.Throws<ArgumentException>(() => command.BeginRendering(foreign, null, 0, 0, 0, 1, 1));
    }

    [Fact]
    public async Task AsyncSubmitUploadsAndPublishesReadbackOnlyAfterCompletion()
    {
        var interop = new FakeInterop { Readback = [9, 8, 7, 6] };
        using var backend = await BrowserWebGpuBackend.CreateAsync(interop);
        using var upload = backend.CreateBuffer(4, GpuMemoryKind.HostMapped);
        using var readback = backend.CreateBuffer(4, GpuMemoryKind.HostCached);
        Fill(upload, 3, 4);
        using var command = backend.MainQueue.StartCommandRecording();
        command.CopyBufferToBuffer(upload, readback, 4);
        command.Finish();
        Assert.Throws<PlatformNotSupportedException>(() => backend.MainQueue.WaitIdle());
        await backend.AsyncQueue.SubmitAsync(command);
        Assert.Equal([9, 8, 7, 6], Read(readback, 4));
        Assert.Single(interop.Uploads);
        Assert.Equal([3, 3, 3, 3], Convert.FromBase64String(interop.Uploads[0].Data));
    }

    [Fact]
    public async Task Surface_present_does_not_override_layout_size_and_duplicate_resize_is_ignored()
    {
        var interop = new FakeInterop();
        using var backend = await BrowserWebGpuBackend.CreateAsync(interop);
        using var surface = backend.CreateCanvasSurface("#canvas", 800, 600);
        using var pixels = backend.CreateBuffer(320 * 240 * 4, GpuMemoryKind.HostCached);

        surface.Present(pixels, 320, 320, 240);
        surface.Resize(800, 600);
        surface.Resize(900, 650);
        surface.Resize(900, 650);

        Assert.Equal(1, interop.SurfacePresents);
        Assert.Equal([(900, 650)], interop.SurfaceResizes);
    }

    [Fact]
    public async Task RootAndTextureReadbackValidationMatchesWebGpuLimits()
    {
        using var backend = await BrowserWebGpuBackend.CreateAsync(new FakeInterop());
        using var command = backend.MainQueue.StartCommandRecording();
        Assert.Throws<ArgumentOutOfRangeException>(() => command.SetRootConstants(new byte[BrowserWebGpuBackend.RootDataSize + 1]));
        using var texture = backend.CreateRenderTarget(3, 2, GpuFormat.Rgba8Unorm);
        using var buffer = backend.CreateBuffer(512, GpuMemoryKind.HostCached);
        Assert.Throws<ArgumentException>(() => command.CopyTextureToBuffer(texture, buffer, 3));
    }

    [Fact]
    public async Task High_level_async_queue_and_canvas_surface_forward_to_browser_backend()
    {
        var interop = new FakeInterop();
        using var backend = await BrowserWebGpuBackend.CreateAsync(interop);
        using var device = new GpuDevice(backend);
        using GpuSurface surface = device.CreateCanvasSurface("#counter", 640, 360);
        using GpuBuffer pixels = device.Malloc(640u * 360u * 4u, GpuMemoryKind.HostMapped);
        using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
        command.Finish();
        await device.MainQueue.SubmitAsync(command);
        await device.MainQueue.WaitIdleAsync();
        surface.Present(pixels, 640, 640, 360);

        Assert.Equal("#counter", interop.LastCanvasToken);
        Assert.Equal((640, 360), interop.LastCanvasSize);
        Assert.Equal(1, interop.SurfacePresents);
    }

    private static unsafe nint Pointer(IGpuBackendBuffer buffer) => (nint)buffer.MappedPointer;
    private static unsafe void Fill(IGpuBackendBuffer buffer, byte value, int length) => new Span<byte>(buffer.MappedPointer, length).Fill(value);
    private static unsafe byte[] Read(IGpuBackendBuffer buffer, int length) => new ReadOnlySpan<byte>(buffer.MappedPointer, length).ToArray();

    private sealed class FakeInterop : IBrowserWebGpuInterop
    {
        private int _next = 10;
        public byte[] Readback { get; set; } = [];
        public List<(int Offset, string Data)> Uploads { get; } = [];
        public string? LastCanvasToken { get; private set; }
        public (int Width, int Height) LastCanvasSize { get; private set; }
        public int SurfacePresents { get; private set; }
        public List<(int Width, int Height)> SurfaceResizes { get; } = [];
        public Task<string> InitializeAsync() => Task.FromResult(JsonSerializer.Serialize(new { handle = ++_next, name = "WebGPU / fake" }));
        public int CreateComputePipeline(int backend, string wgslBase64, string entryPoint) => ++_next;
        public int CreateGraphicsPipeline(int backend, string vsBase64, string vsEntry, string psBase64, string psEntry, string rasterJson) => ++_next;
        public int CreateTexture(int backend, int width, int height, int format, int usage, int bindlessIndex, string dataBase64) => ++_next;
        public int CreateSampler(int backend, int filter, int address, int bindlessIndex) => ++_next;
        public int CreateCommandBuffer(int backend) => ++_next;
        public void CommandSetComputePipeline(int command, int pipeline) { }
        public void CommandSetGraphicsPipeline(int command, int pipeline) { }
        public void CommandSetRootConstants(int command, string dataBase64) { }
        public void CommandDispatch(int command, int x, int y, int z) { }
        public void CommandBeginRendering(int command, int color, int depth, float r, float g, float b, float a, float clearDepth) { }
        public void CommandEndRendering(int command) { }
        public void CommandDraw(int command, int vertexCount, int instanceCount) { }
        public void CommandCopyTextureToBuffer(int command, int texture, int destinationOffset, int bytesPerRow, int width, int height) { }
        public void CommandCopyBufferToBuffer(int command, int sourceOffset, int destinationOffset, int bytes) { }
        public void CommandBarrier(int command) { }
        public void CommandFinish(int command) { }
        public void UploadArena(int backend, int offset, string dataBase64) => Uploads.Add((offset, dataBase64));
        public int Submit(int backend, int command) => 1;
        public Task<string> CompleteAsync(int backend, int serial, string readbacksJson) => Task.FromResult(Convert.ToBase64String(Readback));
        public Task<string> WaitIdleAsync(int backend, string readbacksJson) => Task.FromResult(Convert.ToBase64String(Readback));
        public int CreateSurface(int backend, string canvasToken, int width, int height)
        {
            LastCanvasToken = canvasToken;
            LastCanvasSize = (width, height);
            return ++_next;
        }
        public void SurfacePresent(int surface, int sourceOffset, int stride, int width, int height) => SurfacePresents++;
        public void SurfaceResize(int surface, int width, int height) => SurfaceResizes.Add((width, height));
        public void Release(int kind, int handle) { }
        public void DisposeBackend(int backend) { }
    }
}
