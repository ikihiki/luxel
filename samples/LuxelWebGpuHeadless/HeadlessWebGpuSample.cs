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
        byte[] checkerboard =
        [
            255, 255, 255, 255, 0, 0, 0, 255,
            0, 0, 0, 255, 255, 255, 255, 255,
        ];
        using var sampledTexture = device.CreateTexture(2, 2, checkerboard);
        using var sampler = device.CreateSampler(GpuSamplerFilter.Point);
        using var target = device.CreateRenderTarget(TargetSize, TargetSize, GpuFormat.Rgba8Unorm);
        using var readback = device.Malloc(TargetSize * TargetSize * 4, GpuMemoryKind.HostCached);
        using var graphicsPipeline = device.CreateGraphicsPipeline(
            Wgsl(TriangleShader), new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm), VertexEntry: "vs_main", PixelEntry: "fs_main"));

        using (GpuCommandBuffer commands = device.MainQueue.StartCommandRecording())
        {
            if (!setPipelineAfterBeginRendering) commands.SetGraphicsPipeline(graphicsPipeline)
            .SetRasterizerState(GpuRasterizerState.Default)
            .SetDepthStencilState(GpuDepthStencilState.Default)
            .SetBlendState(GpuBlendState.None);
            commands.SetRootArguments(new RootArguments(vertices.BindlessIndex, sampledTexture.BindlessIndex, sampler.BindlessIndex, 0))
                .BeginRendering(target);
            if (setPipelineAfterBeginRendering) commands.SetGraphicsPipeline(graphicsPipeline)
            .SetRasterizerState(GpuRasterizerState.Default)
            .SetDepthStencilState(GpuDepthStencilState.Default)
            .SetBlendState(GpuBlendState.None);
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
        if (red <= 200 || green <= 200 || blue <= 200 || alpha <= 200)
            throw new InvalidOperationException($"Sampled checkerboard validation failed: expected a white texel, got RGBA=({red},{green},{blue},{alpha}).");

        using var invalidTarget = device.CreateRenderTarget(1, 1, GpuFormat.Rgba8Unorm);
        using var invalidReadback = device.Malloc(4, GpuMemoryKind.HostCached);
        using (GpuCommandBuffer commands = device.MainQueue.StartCommandRecording())
        {
            commands.SetGraphicsPipeline(graphicsPipeline)
                .SetRasterizerState(GpuRasterizerState.Default)
                .SetDepthStencilState(GpuDepthStencilState.Default)
                .SetBlendState(GpuBlendState.None)
                .SetRootArguments(new RootArguments(vertices.BindlessIndex, 16, sampler.BindlessIndex, 0))
                .BeginRendering(invalidTarget)
                .Draw(3).EndRendering().CopyTextureToBuffer(invalidTarget, invalidReadback);
            commands.Finish();
            device.MainQueue.Submit(commands);
        }
        Span<byte> invalidPixel = invalidReadback.Span<byte>();
        if (invalidPixel[0] <= 200 || invalidPixel[1] >= 30 || invalidPixel[2] <= 200 || invalidPixel[3] <= 200)
            throw new InvalidOperationException(
                $"Invalid sampled-resource index was not rejected by the shader ABI sentinel: RGBA=({invalidPixel[0]},{invalidPixel[1]},{invalidPixel[2]},{invalidPixel[3]}).");

        return new HeadlessWebGpuResult(device.Name, computeValue, red, green, blue, alpha);
    }

    private static GpuShaderCode Wgsl(string source) => new() { Wgsl = Encoding.UTF8.GetBytes(source) };

    private readonly record struct RootArguments(uint BufferIndex, uint Resource0, uint Resource1, uint Pad0);

    private const string ComputeShader = """
        struct Root { buffer_index: u32, value: u32, pad0: u32, pad1: u32 }
        @group(0) @binding(0) var<storage, read_write> arena: array<u32>;
        @group(0) @binding(1) var<uniform> root: Root;

        @compute @workgroup_size(1)
        fn main() {
            arena[root.buffer_index * 64u] = root.value;
        }
        """;

    private static readonly string TriangleShader = BuildTriangleShader();

    private static string BuildTriangleShader()
    {
        var source = new StringBuilder("""
            struct Root { buffer_index: u32, texture_index: u32, sampler_index: u32, pad0: u32 }
            @group(0) @binding(0) var<storage, read> arena: array<u32>;
            @group(0) @binding(1) var<uniform> root: Root;

            """);
        for (uint i = 0; i < 16; i++)
            source.AppendLine($"@group(1) @binding({i}) var sampled_texture_{i}: texture_2d<f32>;");
        for (uint i = 0; i < 16; i++)
            source.AppendLine($"@group(1) @binding({16 + i}) var sampled_sampler_{i}: sampler;");

        source.AppendLine("fn sample_selected(texture_index: u32, sampler_index: u32) -> vec4<f32> {");
        source.AppendLine("  switch texture_index {");
        for (uint texture = 0; texture < 16; texture++)
        {
            source.AppendLine($"    case {texture}u: {{ switch sampler_index {{");
            for (uint sampler = 0; sampler < 16; sampler++)
                source.AppendLine($"      case {sampler}u: {{ return textureSample(sampled_texture_{texture}, sampled_sampler_{sampler}, vec2<f32>(0.25, 0.25)); }}");
            source.AppendLine("      default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }");
            source.AppendLine("    } }");
        }
        source.AppendLine("    default: { return vec4<f32>(1.0, 0.0, 1.0, 1.0); }");
        source.AppendLine("  }");
        source.AppendLine("}");
        source.Append("""

            @vertex
            fn vs_main(@builtin(vertex_index) vertex_index: u32) -> @builtin(position) vec4<f32> {
                let word = root.buffer_index * 64u + vertex_index * 2u;
                let position = vec2<f32>(bitcast<f32>(arena[word]), bitcast<f32>(arena[word + 1u]));
                return vec4<f32>(position, 0.0, 1.0);
            }

            @fragment
            fn fs_main() -> @location(0) vec4<f32> {
                return sample_selected(root.texture_index, root.sampler_index);
            }
            """);
        return source.ToString();
    }
}
