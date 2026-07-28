using System.Text;
using Luxel.Graphics;

namespace LuxelWebGpuHeadless;

public readonly record struct HeadlessWebGpuResult(string DeviceName, uint ComputeValue, byte Red, byte Green, byte Blue, byte Alpha)
{
    public string Summary => $"webgpu-headless: device={DeviceName}, compute=0x{ComputeValue:x8}, center=rgba({Red},{Green},{Blue},{Alpha}), status=pass";
}

/// <summary>Runs the smallest public-API WebGPU compute and offscreen rendering path.</summary>
public static class HeadlessWebGpuSample
{
    public const uint ExpectedComputeValue = 0xc0ffee42;
    public const uint TargetSize = 64;

    public static HeadlessWebGpuResult Run(GpuDevice device, bool setPipelineAfterBeginRendering = false)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.BackendKind != GpuBackendKind.WebGpu)
            throw new ArgumentException("The headless WebGPU sample requires a WebGPU GpuDevice.", nameof(device));

        using var computeOutput = device.Malloc(256, GpuMemoryKind.HostCached);
        using var computePipeline = device.CreateComputePipeline(Wgsl(ComputeShader));
        var root = new RootArguments(computeOutput.BindlessIndex, ExpectedComputeValue, 0, 0);

        using (GpuCommandBuffer commands = device.MainQueue.StartCommandRecording())
        {
            commands.SetComputePipeline(computePipeline)
                .SetRootArguments(root)
                .Dispatch(1);
            commands.Finish();
            device.MainQueue.Submit(commands);
        }

        uint computeValue = computeOutput.Span<uint>(1)[0];
        if (computeValue != ExpectedComputeValue)
            throw new InvalidOperationException($"Compute validation failed: expected 0x{ExpectedComputeValue:x8}, got 0x{computeValue:x8}.");

        using var vertices = device.Malloc(3 * 2 * sizeof(float), GpuMemoryKind.HostMapped);
        float[] positions = [-0.8f, -0.8f, 0.8f, -0.8f, 0.0f, 0.8f];
        positions.CopyTo(vertices.Span<float>());
        using var target = device.CreateRenderTarget(TargetSize, TargetSize, GpuFormat.Rgba8Unorm);
        using var readback = device.Malloc(TargetSize * TargetSize * 4, GpuMemoryKind.HostCached);
        using var graphicsPipeline = device.CreateGraphicsPipeline(
            Wgsl(TriangleShader), GpuRasterDesc.Default(GpuFormat.Rgba8Unorm), "vs_main", "fs_main");

        using (GpuCommandBuffer commands = device.MainQueue.StartCommandRecording())
        {
            if (!setPipelineAfterBeginRendering) commands.SetGraphicsPipeline(graphicsPipeline);
            commands.SetRootArguments(new RootArguments(vertices.BindlessIndex, 0, 0, 0))
                .BeginRendering(target);
            if (setPipelineAfterBeginRendering) commands.SetGraphicsPipeline(graphicsPipeline);
            commands.Draw(3).EndRendering().CopyTextureToBuffer(target, readback);
            commands.Finish();
            device.MainQueue.Submit(commands);
        }

        Span<byte> pixels = readback.Span<byte>();
        int center = (checked((int)(TargetSize / 2 * TargetSize + TargetSize / 2))) * 4;
        byte red = pixels[center];
        byte green = pixels[center + 1];
        byte blue = pixels[center + 2];
        byte alpha = pixels[center + 3];
        if (red <= 200 || green >= 30 || blue >= 30 || alpha <= 200)
            throw new InvalidOperationException($"Triangle validation failed: expected a red center pixel, got RGBA=({red},{green},{blue},{alpha}).");

        return new HeadlessWebGpuResult(device.Name, computeValue, red, green, blue, alpha);
    }

    private static GpuShaderCode Wgsl(string source) => new() { Wgsl = Encoding.UTF8.GetBytes(source) };

    private readonly record struct RootArguments(uint BufferIndex, uint Value, uint Pad0, uint Pad1);

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
        struct Root { buffer_index: u32, pad0: u32, pad1: u32, pad2: u32 }
        @group(0) @binding(0) var<storage, read> arena: array<u32>;
        @group(0) @binding(1) var<uniform> root: Root;

        @vertex
        fn vs_main(@builtin(vertex_index) vertex_index: u32) -> @builtin(position) vec4<f32> {
            let word = root.buffer_index * 64u + vertex_index * 2u;
            let position = vec2<f32>(bitcast<f32>(arena[word]), bitcast<f32>(arena[word + 1u]));
            return vec4<f32>(position, 0.0, 1.0);
        }

        @fragment
        fn fs_main() -> @location(0) vec4<f32> {
            return vec4<f32>(1.0, 0.0, 0.0, 1.0);
        }
        """;
}
