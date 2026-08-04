using Luxel.AssetsGpu;
using Luxel.Controls;
using Luxel.Graphics;
using Luxel.Resources;
using Luxel.Shaders;
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
        ResourceHandle<GpuShaderCode> shader = resources.Create<SlangSource, GpuShaderCode>(
            "triangle.slang", new SlangSource("triangle.slang", slang), "graphics");
        ResourceHandle<GpuBuffer> vertexBuffer = resources.CreateBuffer<float>(
            "triangle.vertices", vertices.Length);
        WaitFor(vertexBuffer);
        vertices.CopyTo(vertexBuffer.Value.Span<float>(vertices.Length));
        Task<ResourceHandle<GpuPipeline>> pipeline = CreatePipelineAsync();
        ctx.Initialize(pipeline);

        async Task<ResourceHandle<GpuPipeline>> CreatePipelineAsync()
        {
            await shader.Ready;
            ResourceHandle<GpuPipeline> result = resources.CreateGraphicsPipeline(
                "triangle.pipeline", shader.Value, GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
            await result.Ready;
            return result;
        }

        return ctx.Snap(Frame(GpuView(
            320,
            240,
            (device, surface, _) =>
            {
                uint vertexBufferIndex = vertexBuffer.Value.BindlessIndex;
                using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
                command.BeginRendering(surface.ColorTarget, null, 0.055f, 0.07f, 0.11f, 1)
                    .SetGraphicsPipeline(pipeline.GetAwaiter().GetResult().Value)
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
