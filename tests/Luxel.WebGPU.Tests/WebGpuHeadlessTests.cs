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
        Assert.True(result.Green < 30);
        Assert.True(result.Blue < 30);
        Assert.True(result.Alpha > 200);
    }

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
