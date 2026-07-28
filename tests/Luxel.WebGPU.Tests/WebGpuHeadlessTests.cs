using System.Runtime.InteropServices;
using System.Text;
using Luxel.Graphics.Abstraction;
using Luxel.Graphics.WebGPU;

namespace Luxel.WebGPU.Tests;

public sealed unsafe class WebGpuHeadlessTests
{
    [Fact]
    public void DeviceCreation_ProvidesHeadlessBackend()
    {
        using var backend = TryCreate();
        if (backend is null) return;
        Assert.Contains("WebGPU", backend.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<PlatformNotSupportedException>(() => backend.CreateSurface(0, 64, 64));
    }

    [Fact]
    public void BufferCopy_ReadsBackHostCachedShadow()
    {
        using var backend = TryCreate();
        if (backend is null) return;
        using var source = backend.CreateBuffer(16, GpuMemoryKind.HostMapped);
        using var destination = backend.CreateBuffer(16, GpuMemoryKind.HostCached);
        uint* sourceWords = (uint*)source.MappedPointer;
        sourceWords[0] = 0x12345678;
        sourceWords[1] = 0x90abcdef;

        using var commands = backend.MainQueue.StartCommandRecording();
        commands.CopyBufferToBuffer(source, destination, 16);
        commands.Finish();
        backend.MainQueue.Submit(commands);

        uint* result = (uint*)destination.MappedPointer;
        Assert.Equal(0x12345678u, result[0]);
        Assert.Equal(0x90abcdefu, result[1]);
    }

    [Fact]
    public void Compute_WritesArenaUsingLogicalBindlessIndex()
    {
        using var backend = TryCreate();
        if (backend is null) return;
        using var output = backend.CreateBuffer(256, GpuMemoryKind.HostCached);
        using var pipeline = backend.CreateComputePipeline(Wgsl(ComputeShader), "main");
        uint[] root = [output.BindlessIndex, 0xc0ffee42u, 0, 0];

        using var commands = backend.MainQueue.StartCommandRecording();
        commands.SetComputePipeline(pipeline);
        commands.SetRootConstants(MemoryMarshal.AsBytes(root.AsSpan()));
        commands.Dispatch(1, 1, 1);
        commands.Finish();
        backend.MainQueue.Submit(commands);

        Assert.Equal(0xc0ffee42u, ((uint*)output.MappedPointer)[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TriangleRender_ReadsBackRedCenterPixel(bool setPipelineAfterBeginRendering)
    {
        using var backend = TryCreate();
        if (backend is null) return;
        const uint size = 64;
        using var target = backend.CreateRenderTarget(size, size, GpuFormat.Rgba8Unorm);
        using var readback = backend.CreateBuffer(size * size * 4, GpuMemoryKind.HostCached);
        using var pipeline = backend.CreateGraphicsPipeline(Wgsl(TriangleShader), "vs_main", Wgsl(TriangleShader), "fs_main",
            GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));

        using var commands = backend.MainQueue.StartCommandRecording();
        if (!setPipelineAfterBeginRendering) commands.SetGraphicsPipeline(pipeline);
        commands.BeginRendering(target, null, 0, 0, 0, 1, 1);
        if (setPipelineAfterBeginRendering) commands.SetGraphicsPipeline(pipeline);
        commands.Draw(3, 1);
        commands.EndRendering();
        commands.CopyTextureToBuffer(target, readback, 0);
        commands.Finish();
        backend.MainQueue.Submit(commands);

        byte* pixels = (byte*)readback.MappedPointer;
        int center = ((int)(size / 2) * (int)size + (int)(size / 2)) * 4;
        Assert.True(pixels[center] > 200, $"Expected red center pixel, got RGBA=({pixels[center]},{pixels[center + 1]},{pixels[center + 2]},{pixels[center + 3]}).");
        Assert.True(pixels[center + 1] < 30);
        Assert.True(pixels[center + 2] < 30);
        Assert.True(pixels[center + 3] > 200);
    }

    private static WebGpuBackend? TryCreate()
    {
        try { return WebGpuBackend.Create(); }
        catch (WebGpuUnavailableException exception)
        {
            Console.WriteLine($"WebGPU test guarded because no adapter/runtime is available: {exception.Message}");
            return null;
        }
        catch (FileNotFoundException exception)
        {
            Console.WriteLine($"WebGPU test guarded because wgpu-native could not be found: {exception.Message}");
            return null;
        }
        catch (DllNotFoundException exception)
        {
            Console.WriteLine($"WebGPU test guarded because wgpu-native could not be loaded: {exception.Message}");
            return null;
        }
    }

    private static byte[] Wgsl(string source) => Encoding.UTF8.GetBytes(source);

    private const string ComputeShader = """
        struct Root { buffer_index: u32, value: u32, pad0: u32, pad1: u32 }
        @group(0) @binding(0) var<storage, read_write> arena: array<u32>;
        @group(0) @binding(1) var<uniform> root: Root;

        @compute @workgroup_size(1)
        fn main() {
            arena[root.buffer_index * 64u] = root.value;
        }
        """;

    private const string TriangleShader = """
        struct Root { values: vec4<u32> }
        @group(0) @binding(0) var<storage, read_write> arena: array<u32>;
        @group(0) @binding(1) var<uniform> root: Root;

        @vertex
        fn vs_main(@builtin(vertex_index) vertex_index: u32) -> @builtin(position) vec4<f32> {
            var positions = array<vec2<f32>, 3>(
                vec2<f32>(-0.8, -0.8),
                vec2<f32>( 0.8, -0.8),
                vec2<f32>( 0.0,  0.8));
            return vec4<f32>(positions[vertex_index], 0.0, 1.0);
        }

        @fragment
        fn fs_main() -> @location(0) vec4<f32> {
            return vec4<f32>(1.0, 0.0, 0.0, 1.0);
        }
        """;
}
