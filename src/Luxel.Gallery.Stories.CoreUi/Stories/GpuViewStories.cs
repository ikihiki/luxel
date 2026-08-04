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
                return GpuViewRenderResult.Ready;
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
        ResourceHandle<GpuBuffer> vertexBuffer = resources.Create<float[], GpuBuffer>(
            "triangle.vertices", vertices);
        ResourceHandle<GpuPipeline> pipeline = resources.CreateGraphicsPipeline(
            "triangle.pipeline", shader, GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        Signal<ResourceState> vertexBufferState = ctx.Observe(vertexBuffer);
        Signal<ResourceState> pipelineState = ctx.Observe(pipeline);

        return ctx.Snap(Frame(GpuView(
            320,
            240,
            (device, surface, _) =>
            {
                ResourceState bufferState = vertexBufferState.Value;
                ResourceState shaderPipelineState = pipelineState.Value;
                if (!bufferState.HasValue || !shaderPipelineState.HasValue)
                    return bufferState.Status == ResourceStatus.Failed || shaderPipelineState.Status == ResourceStatus.Failed
                        ? GpuViewRenderResult.Failed
                        : GpuViewRenderResult.Loading;

                using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
                uint vertexBufferIndex = vertexBuffer.Value.BindlessIndex;
                command.BeginRendering(surface.ColorTarget, null, 0.055f, 0.07f, 0.11f, 1)
                    .SetGraphicsPipeline(pipeline.Value)
                    .SetRootArguments(vertexBufferIndex)
                    .Draw(3)
                    .EndRendering();
                surface.CopyColorToFramebuffer(command);
                command.Finish();
                device.MainQueue.Submit(command);
                return GpuViewRenderResult.Ready;
            },
            animated: false)));
    }

    [Story("Examples/3D/BuffersAndBindings", Width = 320, Height = 240, Order = 121,
        CapabilityNote = "Renders an indexed quad from separate position, index, and color buffers.")]
    public static Widget BuffersAndBindings(StoryContext ctx)
    {
        if (ctx.DeviceOrNull is not { } device || ctx.ScopedResourcesOrNull is not { } resources)
            return BuildOnlyGpuView(ctx, 320, 240);

        const string slang = """
            [[vk::binding(0, 0)]]
            RWByteAddressBuffer g_buffers[];

            struct DrawArgs
            {
                uint vertexBufferIndex;
                uint indexBufferIndex;
                uint colorBufferIndex;
            };
            [[vk::push_constant]] DrawArgs g_args;

            struct VSOut
            {
                float4 position : SV_Position;
                float4 color : COLOR0;
            };

            [shader("vertex")]
            VSOut vsMain(uint vertexId : SV_VertexID)
            {
                uint index = g_buffers[g_args.indexBufferIndex].Load<uint>(vertexId * 4);
                float2 position = asfloat(g_buffers[g_args.vertexBufferIndex].Load2(index * 8));
                float4 color = asfloat(g_buffers[g_args.colorBufferIndex].Load4(index * 16));

                VSOut output;
                output.position = float4(position, 0, 1);
                output.color = color;
                return output;
            }

            [shader("pixel")]
            float4 psMain(VSOut input) : SV_Target
            {
                return input.color;
            }
            """;

        var buffers = new BufferQuadBuffers(device);
        ResourceHandle<GpuShaderCode> shader = resources.Create<SlangSource, GpuShaderCode>(
            "buffers-and-bindings.slang", new SlangSource("buffers-and-bindings.slang", slang), "graphics");
        ResourceHandle<GpuPipeline> pipeline = resources.CreateGraphicsPipeline(
            "buffers-and-bindings.pipeline", shader, GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        Signal<ResourceState> pipelineState = ctx.Observe(pipeline);

        return ctx.Snap(Frame(GpuView(
            320,
            240,
            (gpu, surface, _) =>
            {
                ResourceState state = pipelineState.Value;
                if (!state.HasValue)
                    return state.Status == ResourceStatus.Failed
                        ? GpuViewRenderResult.Failed
                        : GpuViewRenderResult.Loading;

                var args = new BufferQuadDrawArgs
                {
                    VertexBufferIndex = buffers.Vertices.BindlessIndex,
                    IndexBufferIndex = buffers.Indices.BindlessIndex,
                    ColorBufferIndex = buffers.Colors.BindlessIndex,
                };
                using GpuCommandBuffer command = gpu.MainQueue.StartCommandRecording();
                command.BeginRendering(surface.ColorTarget, null, 0.055f, 0.07f, 0.11f, 1)
                    .SetGraphicsPipeline(pipeline.Value)
                    .SetRootArguments(args)
                    .Draw(6)
                    .EndRendering();
                surface.CopyColorToFramebuffer(command);
                command.Finish();
                gpu.MainQueue.Submit(command);
                return GpuViewRenderResult.Ready;
            },
            animated: false,
            dispose: buffers.Dispose)));
    }

    [Story("Examples/3D/Textures", Width = 320, Height = 240, Order = 122,
        CapabilityNote = "Renders a generated checker texture with a bindless texture and sampler.")]
    public static Widget Textures(StoryContext ctx)
    {
        if (ctx.DeviceOrNull is not { } device || ctx.ScopedResourcesOrNull is not { } resources)
            return BuildOnlyGpuView(ctx, 320, 240);

        const uint textureWidth = 8;
        const uint textureHeight = 8;
        byte[] pixels = CreateCheckerboard(textureWidth, textureHeight);
        GpuTexture texture = device.CreateTexture(
            textureWidth, textureHeight, pixels, GpuFormat.Rgba8Unorm);
        GpuSampler sampler;
        try
        {
            sampler = device.CreateSampler(
                GpuSamplerFilter.Point, GpuSamplerAddress.Repeat);
        }
        catch
        {
            texture.Dispose();
            throw;
        }

        const string slang = """
            [[vk::binding(1, 0)]]
            Texture2D g_textures[];
            [[vk::binding(2, 0)]]
            SamplerState g_samplers[];

            struct DrawArgs
            {
                uint textureIndex;
                uint samplerIndex;
            };
            [[vk::push_constant]] DrawArgs g_args;

            struct VSOut
            {
                float4 position : SV_Position;
                float2 uv : TEXCOORD0;
            };

            [shader("vertex")]
            VSOut vsMain(uint vertexId : SV_VertexID)
            {
                uint index = vertexId == 0 ? 0
                    : vertexId == 1 ? 1
                    : vertexId == 2 ? 2
                    : vertexId == 3 ? 0
                    : vertexId == 4 ? 2 : 3;
                bool right = index == 1 || index == 2;
                bool top = index >= 2;

                VSOut output;
                output.position = float4(
                    right ? 0.72 : -0.72,
                    top ? 0.72 : -0.72,
                    0, 1);
                output.uv = float2(right ? 1.0 : 0.0, top ? 0.0 : 1.0);
                return output;
            }

            [shader("pixel")]
            float4 psMain(VSOut input) : SV_Target
            {
                return g_textures[g_args.textureIndex]
                    .Sample(g_samplers[g_args.samplerIndex], input.uv);
            }
            """;

        ResourceHandle<GpuShaderCode> shader = resources.Create<SlangSource, GpuShaderCode>(
            "textures.slang", new SlangSource("textures.slang", slang), "graphics");
        ResourceHandle<GpuPipeline> pipeline = resources.CreateGraphicsPipeline(
            "textures.pipeline", shader, GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        Signal<ResourceState> pipelineState = ctx.Observe(pipeline);

        return ctx.Snap(Frame(GpuView(
            320,
            240,
            (gpu, surface, _) =>
            {
                ResourceState state = pipelineState.Value;
                if (!state.HasValue)
                    return state.Status == ResourceStatus.Failed
                        ? GpuViewRenderResult.Failed
                        : GpuViewRenderResult.Loading;

                var args = new TextureDrawArgs
                {
                    TextureIndex = texture.BindlessIndex,
                    SamplerIndex = sampler.BindlessIndex,
                };
                using GpuCommandBuffer command = gpu.MainQueue.StartCommandRecording();
                command.BeginRendering(surface.ColorTarget, null, 0.055f, 0.07f, 0.11f, 1)
                    .SetGraphicsPipeline(pipeline.Value)
                    .SetRootArguments(args)
                    .Draw(6)
                    .EndRendering();
                surface.CopyColorToFramebuffer(command);
                command.Finish();
                gpu.MainQueue.Submit(command);
                return GpuViewRenderResult.Ready;
            },
            animated: false,
            dispose: () =>
            {
                device.MainQueue.WaitIdle();
                sampler.Dispose();
                texture.Dispose();
            })));
    }

    private struct TextureDrawArgs
    {
        public uint TextureIndex;
        public uint SamplerIndex;
    }

    private static byte[] CreateCheckerboard(uint width, uint height)
    {
        var pixels = new byte[checked((int)(width * height * 4))];
        for (uint y = 0; y < height; y++)
        for (uint x = 0; x < width; x++)
        {
            bool light = ((x / 2) + (y / 2)) % 2 == 0;
            int offset = checked((int)((y * width + x) * 4));
            pixels[offset + 0] = light ? (byte)255 : (byte)76;
            pixels[offset + 1] = light ? (byte)160 : (byte)48;
            pixels[offset + 2] = light ? (byte)40 : (byte)176;
            pixels[offset + 3] = 255;
        }
        return pixels;
    }

    private struct BufferQuadDrawArgs
    {
        public uint VertexBufferIndex;
        public uint IndexBufferIndex;
        public uint ColorBufferIndex;
    }

    private sealed class BufferQuadBuffers : IDisposable
    {
        private readonly GpuDevice _device;
        private bool _disposed;

        public BufferQuadBuffers(GpuDevice device)
        {
            _device = device;
            float[] vertices =
            [
                -0.72f, -0.72f,
                 0.72f, -0.72f,
                 0.72f,  0.72f,
                -0.72f,  0.72f,
            ];
            uint[] indices = [0, 1, 2, 0, 2, 3];
            float[] colors =
            [
                1.00f, 0.18f, 0.18f, 1,
                0.18f, 1.00f, 0.28f, 1,
                0.20f, 0.42f, 1.00f, 1,
                1.00f, 0.82f, 0.18f, 1,
            ];

            Vertices = CreateBuffer(device, vertices);
            try
            {
                Indices = CreateBuffer(device, indices);
                try { Colors = CreateBuffer(device, colors); }
                catch
                {
                    Indices.Dispose();
                    throw;
                }
            }
            catch
            {
                Vertices.Dispose();
                throw;
            }
        }

        public GpuBuffer Vertices { get; }
        public GpuBuffer Indices { get; }
        public GpuBuffer Colors { get; }

        private static GpuBuffer CreateBuffer<T>(GpuDevice device, T[] data) where T : unmanaged
        {
            GpuBuffer buffer = device.Malloc(
                checked((ulong)data.Length * (ulong)System.Runtime.InteropServices.Marshal.SizeOf<T>()),
                GpuMemoryKind.HostMapped);
            try
            {
                data.CopyTo(buffer.Span<T>(data.Length));
                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _device.MainQueue.WaitIdle();
            Colors.Dispose();
            Indices.Dispose();
            Vertices.Dispose();
        }
    }

    private static Widget BuildOnlyGpuView(StoryContext ctx, float width, float height)
        => ctx.Snap(Frame(GpuView(width, height,
            static (_, _, _) => GpuViewRenderResult.Failed,
            animated: false)));
}
