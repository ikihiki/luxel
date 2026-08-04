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

        CanonicalTriangleRecipe.Vertex[] vertices = CanonicalTriangleRecipe.CreateVertices();
        ResourceHandle<GpuBuffer> vertexBuffer = resources.CreateBuffer<CanonicalTriangleRecipe.Vertex>(
            "triangle.vertices", vertices.Length);
        ResourceHandle<GpuPipeline> pipeline = resources.CreateGraphicsPipeline(
            "triangle.pipeline",
            TriangleShader(),
            GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        WaitFor(vertexBuffer);
        WaitFor(pipeline);
        vertices.CopyTo(vertexBuffer.Value.Span<CanonicalTriangleRecipe.Vertex>(vertices.Length));

        return ctx.Snap(Frame(GpuView(
            320,
            240,
            (device, surface, _) =>
            {
                var args = new CanonicalTriangleRecipe.DrawArgs
                {
                    VertexBufferIndex = vertexBuffer.Value.BindlessIndex,
                };
                using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
                command.BeginRendering(surface.ColorTarget, null, 0.055f, 0.07f, 0.11f, 1)
                    .SetGraphicsPipeline(pipeline.Value)
                    .SetRootArguments(args)
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

    private static GpuShaderCode TriangleShader()
    {
        GpuShaderCode native = GpuShaderCode.Load("tutorial_triangle");
        return new GpuShaderCode
        {
            SpirV = native.SpirV,
            Dxil = native.Dxil,
            DxilVertex = native.DxilVertex,
            DxilPixel = native.DxilPixel,
            Wgsl = Encoding.UTF8.GetBytes(TriangleWgsl),
        };
    }

    private const string TriangleWgsl = """
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
}
