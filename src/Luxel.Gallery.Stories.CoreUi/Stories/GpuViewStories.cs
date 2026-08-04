using System.Security.Cryptography;
using System.Text;
using Luxel.AssetsGpu;
using Luxel.Controls;
using Luxel.Graphics;
using Luxel.Resources;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// Browser-safe GpuView stories. Files added to this project are automatically included in the
/// WebAssembly catalog; no browser host or manifest registration is required.
/// </summary>
public static class GpuViewStories
{
    [Story(CanonicalClearColorRecipe.Story, Width = 320, Height = 240, Order = 119,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget ClearColor(StoryContext ctx)
        => ctx.Snap(Frame(GpuView(
            320,
            240,
            static (device, surface, _) =>
            {
                using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
                command.BeginRendering(surface.ColorTarget, null, 0.055f, 0.07f, 0.11f, 1f)
                    .EndRendering();
                surface.CopyColorToFramebuffer(command);
                command.Finish();
                device.MainQueue.Submit(command);
            },
            animated: false)));

    [Story(CanonicalTriangleRecipe.Story, Width = 320, Height = 240, Order = 120,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget Triangle(StoryContext ctx)
    {
        if (ctx.DeviceOrNull is null || ctx.ScopedResourcesOrNull is not { } resources)
            return BuildOnlyGpuView(ctx, 320, 240);

        float[] vertices =
        [
            0, -0.72f, 0, 1, 1, 0.18f, 0.18f, 1,
            0.72f, 0.62f, 0, 1, 0.18f, 1, 0.28f, 1,
            -0.72f, 0.62f, 0, 1, 0.2f, 0.42f, 1, 1,
        ];
        const string slang = """
            // Learn/Rendering/FirstTriangle: vertex pulling shared by Vulkan (SPIR-V) and D3D12 (DXIL).

            [[vk::binding(0, 0)]]
            RWByteAddressBuffer g_buffers[];

            struct DrawArgs { uint vertexBufferIndex; };
            [[vk::push_constant]] DrawArgs g_args;

            struct Vertex
            {
                float4 position;
                float4 color;
            };

            struct VSOut
            {
                float4 position : SV_Position;
                float4 color : COLOR0;
            };

            [shader("vertex")]
            VSOut vsMain(uint vertexId : SV_VertexID)
            {
                Vertex vertex = g_buffers[g_args.vertexBufferIndex].Load<Vertex>(vertexId * 32);
                VSOut output;
                output.position = vertex.position;
                output.color = vertex.color;
                return output;
            }

            [shader("pixel")]
            float4 psMain(VSOut input) : SV_Target
            {
                return input.color;
            }
            """;
        const string wgsl = """
            struct DrawArgs { vertexBufferIndex : u32, };
            @group(0) @binding(1) var<uniform> g_args : DrawArgs;
            @group(0) @binding(0) var<storage, read> g_buffers : array<u32>;

            struct VSOut {
                @builtin(position) position : vec4<f32>,
                @location(0) color : vec4<f32>,
            };

            fn loadFloat(byteOffset : u32) -> f32 {
                return bitcast<f32>(g_buffers[g_args.vertexBufferIndex * 64u + byteOffset / 4u]);
            }

            @vertex
            fn vsMain(@builtin(vertex_index) vertexId : u32) -> VSOut {
                let base = vertexId * 32u;
                var output : VSOut;
                output.position = vec4<f32>(
                    loadFloat(base), loadFloat(base + 4u),
                    loadFloat(base + 8u), loadFloat(base + 12u));
                output.color = vec4<f32>(
                    loadFloat(base + 16u), loadFloat(base + 20u),
                    loadFloat(base + 24u), loadFloat(base + 28u));
                return output;
            }

            @fragment
            fn psMain(input : VSOut) -> @location(0) vec4<f32> {
                return input.color;
            }
            """;

        string slangSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(slang + "\n")))
            .ToLowerInvariant();
        if (slangSha256 != "f960f5bbbd677280d9b61c5e618c78affc27efa16d8b07f24c00631306ace40f")
            throw new InvalidOperationException("Inline Triangle Slang does not match the compiled shader cache.");
        GpuShaderCode native = GpuShaderCode.Load("tutorial_triangle");
        var shader = new GpuShaderCode
        {
            SpirV = native.SpirV,
            Dxil = native.Dxil,
            DxilVertex = native.DxilVertex,
            DxilPixel = native.DxilPixel,
            Wgsl = Encoding.UTF8.GetBytes(wgsl),
        };
        ResourceHandle<GpuBuffer> vertexBuffer = resources.CreateBuffer<float>(
            "triangle.vertices", vertices.Length);
        ResourceHandle<GpuPipeline> pipeline = resources.CreateGraphicsPipeline(
            "triangle.pipeline", shader, GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        WaitFor(vertexBuffer);
        WaitFor(pipeline);
        vertices.CopyTo(vertexBuffer.Value.Span<float>(vertices.Length));

        return ctx.Snap(Frame(GpuView(
            320,
            240,
            (device, surface, _) =>
            {
                uint vertexBufferIndex = vertexBuffer.Value.BindlessIndex;
                using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
                command.BeginRendering(surface.ColorTarget, null, 0.055f, 0.07f, 0.11f, 1)
                    .SetGraphicsPipeline(pipeline.Value)
                    .SetRootArguments(vertexBufferIndex)
                    .Draw(3)
                    .EndRendering();
                surface.CopyColorToFramebuffer(command);
                command.Finish();
                device.MainQueue.Submit(command);
            },
            animated: false)));
    }

    private static Widget BuildOnlyGpuView(StoryContext ctx, float width, float height)
        => ctx.Snap(Frame(GpuView(width, height,
            static (_, _, _) => throw new InvalidOperationException(
                "GpuView was realized without a ResourceSystem-backed GPU StoryContext."),
            animated: false)));

    private static void WaitFor<T>(ResourceHandle<T> handle)
        => handle.Ready.GetAwaiter().GetResult();
}
