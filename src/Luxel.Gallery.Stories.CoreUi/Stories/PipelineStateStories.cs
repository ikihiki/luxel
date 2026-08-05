using System.Runtime.InteropServices;
using Luxel.AssetsGpu;
using Luxel.Controls;
using Luxel.Graphics;
using Luxel.Resources;
using Luxel.Shaders;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>Browser-safe examples of logical graphics pipelines and independent command-time state.</summary>
public static class PipelineStateStories
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public float Px, Py, Pz, Pw, R, G, B, A; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs { public uint VertexBufferIndex; }

    private enum DemoKind { Topology, Rasterizer, Depth, Blend, Stencil, ViewportScissor, Separation }

    [Story("Examples/3D/PipelineState/Topology", Height = 320, Order = 100,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget Topology(StoryContext ctx) => Build(ctx, DemoKind.Topology);

    [Story("Examples/3D/PipelineState/Rasterizer", Height = 320, Order = 101,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget Rasterizer(StoryContext ctx) => Build(ctx, DemoKind.Rasterizer);

    [Story("Examples/3D/PipelineState/Depth", Height = 320, Order = 102,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget DepthStates(StoryContext ctx) => Build(ctx, DemoKind.Depth);

    [Story("Examples/3D/PipelineState/Blend", Height = 320, Order = 103,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget BlendState(StoryContext ctx) => Build(ctx, DemoKind.Blend);

    [Story("Examples/3D/PipelineState/Stencil", Height = 320, Order = 104,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget Stencil(StoryContext ctx) => Build(ctx, DemoKind.Stencil);

    [Story("Examples/3D/PipelineState/ViewportScissor", Height = 320, Order = 105,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget ViewportScissor(StoryContext ctx) => Build(ctx, DemoKind.ViewportScissor);

    [Story("Examples/3D/PipelineState/Separation", Height = 320, Order = 106,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget Separation(StoryContext ctx) => Build(ctx, DemoKind.Separation);

    [Story("Examples/3D/Depth", Height = 320, Order = 107,
        CapabilityNote = "Compatibility route backed by the browser-safe pipeline-state demo.")]
    public static Widget Depth(StoryContext ctx) => Build(ctx, DemoKind.Depth);

    [Story("Examples/3D/Blend", Height = 320, Order = 108,
        CapabilityNote = "Compatibility route backed by the browser-safe pipeline-state demo.")]
    public static Widget Blend(StoryContext ctx) => Build(ctx, DemoKind.Blend);

    private static Widget Build(StoryContext ctx, DemoKind kind)
    {
        if (ctx.DeviceOrNull is not { } device || ctx.ScopedResourcesOrNull is not { } resources)
            return ctx.Snap(Frame(GpuView(256, 256, static (_, _, _) => GpuViewRenderResult.Failed, animated: false)));

        var demo = new PipelineStateDemo(device, resources, ctx, kind);
        return ctx.Snap(Frame(GpuView(256, 256, demo.Render, animated: false, dispose: demo.Dispose)));
    }

    private sealed class PipelineStateDemo : IDisposable
    {
        private const uint Size = 256;
        private readonly GpuDevice _device;
        private readonly DemoKind _kind;
        private readonly List<GpuBuffer> _geometry = [];
        private readonly ResourceHandle<GpuPipeline>[] _pipelines;
        private readonly Signal<ResourceState>[] _pipelineStates;
        private readonly GpuTexture? _depth;
        private GpuViewSurface? _renderedSurface;

        internal PipelineStateDemo(GpuDevice device, ResourceScope resources, StoryContext ctx, DemoKind kind)
        {
            _device = device;
            _kind = kind;
            string prefix = $"pipeline-state.{kind.ToString().ToLowerInvariant()}";
            ResourceHandle<GpuShaderCode> shader = resources.Create<SlangSource, GpuShaderCode>(
                prefix + ".shader", new SlangSource(prefix + ".slang", ShaderSource), "graphics");

            GpuFormat? depthFormat = kind switch
            {
                DemoKind.Depth or DemoKind.Separation => GpuFormat.D32Float,
                DemoKind.Stencil => GpuFormat.Depth24PlusStencil8,
                _ => null,
            };
            if (depthFormat is { } format) _depth = device.CreateDepthTarget(Size, Size, format);

            GpuPrimitiveTopology[] topologies = kind == DemoKind.Topology
                ? [GpuPrimitiveTopology.TriangleList, GpuPrimitiveTopology.TriangleStrip]
                : [GpuPrimitiveTopology.TriangleList];
            _pipelines = topologies.Select((topology, index) => resources.CreateGraphicsPipeline(
                prefix + $".pipeline.{index}", shader,
                new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm, depthFormat), topology))).ToArray();
            _pipelineStates = _pipelines.Select(ctx.Observe).ToArray();
            CreateGeometry();
        }

        internal GpuViewRenderResult Render(GpuDevice device, GpuViewSurface surface, float time)
        {
            if (ReferenceEquals(_renderedSurface, surface)) return GpuViewRenderResult.Ready;
            if (_pipelineStates.FirstOrDefault(state => state.Value.Status == ResourceStatus.Failed) is { } failed)
                throw new InvalidOperationException($"Pipeline state demo '{_kind}' failed to load.", failed.Value.Error);
            if (_pipelineStates.Any(state => !state.Value.HasValue))
                return GpuViewRenderResult.Loading;

            using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
            switch (_kind)
            {
                case DemoKind.Topology: RenderTopology(command, surface); break;
                case DemoKind.Rasterizer: RenderRasterizer(command, surface); break;
                case DemoKind.Depth: RenderDepth(command, surface); break;
                case DemoKind.Blend: RenderBlend(command, surface); break;
                case DemoKind.Stencil: RenderStencil(command, surface); break;
                case DemoKind.ViewportScissor: RenderViewportScissor(command, surface); break;
                case DemoKind.Separation: RenderSeparation(command, surface); break;
            }
            command.EndRendering();
            surface.CopyColorToFramebuffer(command);
            command.Finish();
            device.MainQueue.Submit(command);
            _renderedSurface = surface;
            return GpuViewRenderResult.Ready;
        }

        private void RenderTopology(GpuCommandBuffer cmd, GpuViewSurface surface)
            => cmd.BeginRendering(surface.ColorTarget, null, .05f, .06f, .08f, 1)
                .SetViewport(new(8, 24, 112, 208)).SetScissor(new(8, 24, 112, 208))
                .SetGraphicsPipeline(P(0)).SetRootArguments(Args(G(0))).Draw(6)
                .SetViewport(new(136, 24, 112, 208)).SetScissor(new(136, 24, 112, 208))
                .SetGraphicsPipeline(P(1)).SetRootArguments(Args(G(1))).Draw(4);

        private void RenderRasterizer(GpuCommandBuffer cmd, GpuViewSurface surface)
            => cmd.BeginRendering(surface.ColorTarget, null, .05f, .06f, .08f, 1).SetGraphicsPipeline(P(0))
                .SetViewport(new(4, 24, 76, 208)).SetScissor(new(4, 24, 76, 208))
                .SetRasterizerState(GpuRasterizerState.Default).SetRootArguments(Args(G(0))).Draw(3)
                .SetViewport(new(90, 24, 76, 208)).SetScissor(new(90, 24, 76, 208))
                .SetRasterizerState(new(GpuCullMode.Back, GpuFrontFace.CounterClockwise)).Draw(3)
                .SetViewport(new(176, 24, 76, 208)).SetScissor(new(176, 24, 76, 208))
                .SetRasterizerState(new(GpuCullMode.Back, GpuFrontFace.Clockwise)).Draw(3);

        private void RenderDepth(GpuCommandBuffer cmd, GpuViewSurface surface)
        {
            GpuDepthStencilState testWrite = GpuDepthStencilState.Default with { DepthTest = true, DepthWrite = true };
            GpuDepthStencilState testNoWrite = testWrite with { DepthWrite = false };
            cmd.BeginRendering(surface.ColorTarget, _depth, .05f, .06f, .08f, 1, clearDepth: 1).SetGraphicsPipeline(P(0))
                .SetViewport(new(4, 24, 76, 208)).SetScissor(new(4, 24, 76, 208))
                .SetDepthStencilState(GpuDepthStencilState.Default).SetRootArguments(Args(G(0))).Draw(3).SetRootArguments(Args(G(1))).Draw(3)
                .SetViewport(new(90, 24, 76, 208)).SetScissor(new(90, 24, 76, 208))
                .SetDepthStencilState(testWrite).SetRootArguments(Args(G(0))).Draw(3).SetRootArguments(Args(G(1))).Draw(3)
                .SetViewport(new(176, 24, 76, 208)).SetScissor(new(176, 24, 76, 208))
                .SetDepthStencilState(testNoWrite).SetRootArguments(Args(G(0))).Draw(3).SetRootArguments(Args(G(1))).Draw(3);
        }

        private void RenderBlend(GpuCommandBuffer cmd, GpuViewSurface surface)
            => cmd.BeginRendering(surface.ColorTarget, null, .05f, .06f, .08f, 1).SetGraphicsPipeline(P(0))
                .SetViewport(new(8, 24, 112, 208)).SetScissor(new(8, 24, 112, 208))
                .SetBlendState(GpuBlendState.None).SetRootArguments(Args(G(0))).Draw(3).SetRootArguments(Args(G(1))).Draw(3)
                .SetViewport(new(136, 24, 112, 208)).SetScissor(new(136, 24, 112, 208))
                .SetBlendState(GpuBlendState.AlphaBlend).SetRootArguments(Args(G(0))).Draw(3).SetRootArguments(Args(G(1))).Draw(3);

        private void RenderStencil(GpuCommandBuffer cmd, GpuViewSurface surface)
        {
            var replace = new GpuStencilFaceState(GpuCompareOp.Always, GpuStencilOp.Keep, GpuStencilOp.Keep, GpuStencilOp.Replace);
            var equal = new GpuStencilFaceState(GpuCompareOp.Equal, GpuStencilOp.Keep, GpuStencilOp.Keep, GpuStencilOp.Keep);
            GpuDepthStencilState writeMask = GpuDepthStencilState.Default with
            {
                StencilTest = true, StencilFront = replace, StencilBack = replace,
                StencilReadMask = 0x0f, StencilWriteMask = 0x0f,
            };
            GpuDepthStencilState testMask = writeMask with
            {
                StencilFront = equal, StencilBack = equal, StencilWriteMask = 0,
            };
            cmd.BeginRendering(surface.ColorTarget, _depth, .05f, .06f, .08f, 1, clearDepth: 1, clearStencil: 0)
                .SetGraphicsPipeline(P(0))
                .SetViewport(new(8, 24, 112, 208)).SetScissor(new(8, 24, 112, 208))
                .SetDepthStencilState(writeMask).SetStencilReference(1).SetRootArguments(Args(G(0))).Draw(3)
                .SetDepthStencilState(testMask).SetStencilReference(1).SetRootArguments(Args(G(1))).Draw(6)
                .SetViewport(new(136, 24, 112, 208)).SetScissor(new(136, 24, 112, 208))
                .SetDepthStencilState(writeMask).SetStencilReference(2).SetRootArguments(Args(G(0))).Draw(3)
                .SetDepthStencilState(testMask).SetStencilReference(2).SetRootArguments(Args(G(1))).Draw(6);
        }

        private void RenderViewportScissor(GpuCommandBuffer cmd, GpuViewSurface surface)
            => cmd.BeginRendering(surface.ColorTarget, null, .05f, .06f, .08f, 1).SetGraphicsPipeline(P(0))
                .SetViewport(new(16, 24, 96, 208)).SetScissor(new(0, 0, 256, 256)).SetRootArguments(Args(G(0))).Draw(6)
                .SetViewport(new(144, 24, 96, 208)).SetScissor(new(144, 80, 96, 96)).SetRootArguments(Args(G(1))).Draw(6);

        private void RenderSeparation(GpuCommandBuffer cmd, GpuViewSurface surface)
        {
            GpuDepthStencilState depth = GpuDepthStencilState.Default with { DepthTest = true, DepthWrite = true };
            cmd.BeginRendering(surface.ColorTarget, _depth, .05f, .06f, .08f, 1, clearDepth: 1).SetGraphicsPipeline(P(0))
                .SetViewport(new(8, 8, 112, 112)).SetScissor(new(8, 8, 112, 112))
                .SetRasterizerState(GpuRasterizerState.Default).SetDepthStencilState(GpuDepthStencilState.Default).SetBlendState(GpuBlendState.None)
                .SetRootArguments(Args(G(0))).Draw(3)
                .SetViewport(new(136, 8, 112, 112)).SetScissor(new(136, 8, 112, 112))
                .SetBlendState(GpuBlendState.AlphaBlend).SetRootArguments(Args(G(0))).Draw(3).SetRootArguments(Args(G(1))).Draw(3)
                .SetViewport(new(8, 136, 112, 112)).SetScissor(new(8, 136, 112, 112))
                .SetBlendState(GpuBlendState.None).SetRasterizerState(new(GpuCullMode.Back, GpuFrontFace.Clockwise)).SetRootArguments(Args(G(0))).Draw(3)
                .SetViewport(new(136, 136, 112, 112)).SetScissor(new(136, 136, 112, 112))
                .SetRasterizerState(GpuRasterizerState.Default).SetDepthStencilState(depth).SetRootArguments(Args(G(0))).Draw(3).SetRootArguments(Args(G(1))).Draw(3);
        }

        private void CreateGeometry()
        {
            switch (_kind)
            {
                case DemoKind.Topology:
                    Add(V(-.8f, -.7f, 0, .95f, .25f, .18f), V(.8f, -.7f, 0, .95f, .25f, .18f), V(-.8f, .7f, 0, .95f, .25f, .18f), V(.8f, -.7f, 0, .95f, .25f, .18f), V(.8f, .7f, 0, .95f, .25f, .18f), V(-.8f, .7f, 0, .95f, .25f, .18f));
                    Add(V(-.8f, -.7f, 0, .2f, .7f, 1), V(.8f, -.7f, 0, .2f, .7f, 1), V(-.8f, .7f, 0, .2f, .7f, 1), V(.8f, .7f, 0, .2f, .7f, 1));
                    break;
                case DemoKind.Rasterizer:
                    Add(V(-.75f, .7f, 0, .95f, .65f, .12f), V(-.75f, -.7f, 0, .95f, .65f, .12f), V(.75f, -.7f, 0, .95f, .65f, .12f));
                    break;
                case DemoKind.Depth:
                    Add(V(-.8f, -.7f, .3f, .15f, .9f, .35f), V(.8f, -.7f, .3f, .15f, .9f, .35f), V(0, .75f, .3f, .15f, .9f, .35f));
                    Add(V(-.8f, .7f, .7f, .95f, .18f, .16f), V(.8f, .7f, .7f, .95f, .18f, .16f), V(0, -.75f, .7f, .95f, .18f, .16f));
                    break;
                case DemoKind.Blend:
                    Add(V(-.8f, -.7f, 0, 1, .12f, .12f), V(.8f, -.7f, 0, 1, .12f, .12f), V(0, .75f, 0, 1, .12f, .12f));
                    Add(V(-.8f, .7f, 0, .08f, .25f, 1, .5f), V(.8f, .7f, 0, .08f, .25f, 1, .5f), V(0, -.75f, 0, .08f, .25f, 1, .5f));
                    break;
                case DemoKind.Stencil:
                    Add(V(-.8f, -.75f, .5f, .18f, .2f, .26f), V(.8f, -.75f, .5f, .18f, .2f, .26f), V(0, .8f, .5f, .18f, .2f, .26f));
                    Add(Quad(1, .75f, .12f));
                    break;
                case DemoKind.ViewportScissor:
                    Add(Quad(.95f, .22f, .16f)); Add(Quad(.15f, .55f, 1));
                    break;
                case DemoKind.Separation:
                    Add(V(-.8f, .75f, .35f, .18f, .85f, .35f), V(-.8f, -.75f, .35f, .18f, .85f, .35f), V(.8f, -.75f, .35f, .18f, .85f, .35f));
                    Add(V(-.8f, -.7f, .65f, .2f, .35f, 1, .55f), V(.8f, -.7f, .65f, .2f, .35f, 1, .55f), V(0, .8f, .65f, .2f, .35f, 1, .55f));
                    break;
            }
        }

        private static Vertex[] Quad(float r, float g, float b) =>
        [
            V(-1, -1, 0, r, g, b), V(1, -1, 0, r, g, b), V(-1, 1, 0, r, g, b),
            V(1, -1, 0, r, g, b), V(1, 1, 0, r, g, b), V(-1, 1, 0, r, g, b),
        ];

        private void Add(params Vertex[] vertices)
        {
            var buffer = _device.Malloc((ulong)(vertices.Length * Marshal.SizeOf<Vertex>()), GpuMemoryKind.HostMapped);
            vertices.AsSpan().CopyTo(buffer.Span<Vertex>(vertices.Length));
            _geometry.Add(buffer);
        }

        private GpuPipeline P(int index) => _pipelines[index].Value;
        private GpuBuffer G(int index) => _geometry[index];
        private static DrawArgs Args(GpuBuffer geometry) => new() { VertexBufferIndex = geometry.BindlessIndex };
        private static Vertex V(float x, float y, float z, float r, float g, float b, float a = 1)
            => new() { Px = x, Py = y, Pz = z, Pw = 1, R = r, G = g, B = b, A = a };

        public void Dispose()
        {
            foreach (GpuBuffer buffer in _geometry) buffer.Dispose();
            _depth?.Dispose();
        }
    }

    private const string ShaderSource = """
        [[vk::binding(0, 0)]] RWByteAddressBuffer g_buffers[];
        struct DrawArgs { uint vertexBufferIndex; };
        [[vk::push_constant]] DrawArgs g_args;
        struct Vertex { float4 position; float4 color; };
        struct VSOut { float4 position : SV_Position; float4 color : COLOR0; };

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
        float4 psMain(VSOut input) : SV_Target { return input.color; }
        """;
}
