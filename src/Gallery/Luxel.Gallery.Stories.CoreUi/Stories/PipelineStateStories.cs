using System.Runtime.InteropServices;
using Luxel.Controls;
using Luxel.Graphics;
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
    private delegate void RecordDemo(PipelineStateDemo demo, GpuCommandBuffer command, GpuViewSurface surface);

    [Story("Examples/3D/PipelineState/Topology", Height = 320, Order = 100,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget Topology(StoryContext ctx) => Build(ctx, DemoKind.Topology,
        [
            new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm), GpuPrimitiveTopology.TriangleList),
            new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm), GpuPrimitiveTopology.TriangleStrip),
        ], static (demo, command, surface) =>
        command.BeginRendering(surface.ColorTarget, null, .05f, .06f, .08f, 1)
            .SetViewport(new(8, 24, 112, 208)).SetScissor(new(8, 24, 112, 208))
            .SetGraphicsPipeline(demo.Pipeline(0)).SetRootArguments(demo.Arguments(0)).Draw(6)
            .SetViewport(new(136, 24, 112, 208)).SetScissor(new(136, 24, 112, 208))
            .SetGraphicsPipeline(demo.Pipeline(1)).SetRootArguments(demo.Arguments(1)).Draw(4));

    [Story("Examples/3D/PipelineState/Rasterizer", Height = 320, Order = 101,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget Rasterizer(StoryContext ctx) => Build(ctx, DemoKind.Rasterizer,
        [new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm))],
        static (demo, command, surface) =>
        command.BeginRendering(surface.ColorTarget, null, .05f, .06f, .08f, 1).SetGraphicsPipeline(demo.Pipeline(0))
            .SetViewport(new(4, 24, 76, 208)).SetScissor(new(4, 24, 76, 208))
            .SetRasterizerState(GpuRasterizerState.Default).SetRootArguments(demo.Arguments(0)).Draw(3)
            .SetViewport(new(90, 24, 76, 208)).SetScissor(new(90, 24, 76, 208))
            .SetRasterizerState(new(GpuCullMode.Back, GpuFrontFace.CounterClockwise)).Draw(3)
            .SetViewport(new(176, 24, 76, 208)).SetScissor(new(176, 24, 76, 208))
            .SetRasterizerState(new(GpuCullMode.Back, GpuFrontFace.Clockwise)).Draw(3));

    [Story("Examples/3D/PipelineState/Depth", Height = 320, Order = 102,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget DepthStates(StoryContext ctx) => Build(ctx, DemoKind.Depth,
        [new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm, GpuFormat.D32Float))],
        static (demo, command, surface) =>
    {
        GpuDepthStencilState testWrite = GpuDepthStencilState.Default with { DepthTest = true, DepthWrite = true };
        GpuDepthStencilState testNoWrite = testWrite with { DepthWrite = false };
        command.BeginRendering(surface.ColorTarget, demo.DepthTarget, .05f, .06f, .08f, 1, clearDepth: 1).SetGraphicsPipeline(demo.Pipeline(0))
            .SetViewport(new(4, 24, 76, 208)).SetScissor(new(4, 24, 76, 208))
            .SetDepthStencilState(GpuDepthStencilState.Default).SetRootArguments(demo.Arguments(0)).Draw(3).SetRootArguments(demo.Arguments(1)).Draw(3)
            .SetViewport(new(90, 24, 76, 208)).SetScissor(new(90, 24, 76, 208))
            .SetDepthStencilState(testWrite).SetRootArguments(demo.Arguments(0)).Draw(3).SetRootArguments(demo.Arguments(1)).Draw(3)
            .SetViewport(new(176, 24, 76, 208)).SetScissor(new(176, 24, 76, 208))
            .SetDepthStencilState(testNoWrite).SetRootArguments(demo.Arguments(0)).Draw(3).SetRootArguments(demo.Arguments(1)).Draw(3);
    });

    [Story("Examples/3D/PipelineState/Blend", Height = 320, Order = 103,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget BlendState(StoryContext ctx) => Build(ctx, DemoKind.Blend,
        [new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm))],
        static (demo, command, surface) =>
        command.BeginRendering(surface.ColorTarget, null, .05f, .06f, .08f, 1).SetGraphicsPipeline(demo.Pipeline(0))
            .SetViewport(new(8, 24, 112, 208)).SetScissor(new(8, 24, 112, 208))
            .SetBlendState(GpuBlendState.None).SetRootArguments(demo.Arguments(0)).Draw(3).SetRootArguments(demo.Arguments(1)).Draw(3)
            .SetViewport(new(136, 24, 112, 208)).SetScissor(new(136, 24, 112, 208))
            .SetBlendState(GpuBlendState.AlphaBlend).SetRootArguments(demo.Arguments(0)).Draw(3).SetRootArguments(demo.Arguments(1)).Draw(3));

    [Story("Examples/3D/PipelineState/Stencil", Height = 320, Order = 104,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget Stencil(StoryContext ctx) => Build(ctx, DemoKind.Stencil,
        [new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm, GpuFormat.Depth24PlusStencil8))],
        static (demo, command, surface) =>
    {
        var replace = new GpuStencilFaceState(GpuCompareOp.Always, GpuStencilOp.Keep, GpuStencilOp.Keep, GpuStencilOp.Replace);
        var equal = new GpuStencilFaceState(GpuCompareOp.Equal, GpuStencilOp.Keep, GpuStencilOp.Keep, GpuStencilOp.Keep);
        GpuDepthStencilState writeMask = GpuDepthStencilState.Default with
        {
            StencilTest = true, StencilFront = replace, StencilBack = replace,
            StencilReadMask = 0x0f, StencilWriteMask = 0x0f,
        };
        GpuDepthStencilState testMask = writeMask with { StencilFront = equal, StencilBack = equal, StencilWriteMask = 0 };
        command.BeginRendering(surface.ColorTarget, demo.DepthTarget, .05f, .06f, .08f, 1, clearDepth: 1, clearStencil: 0)
            .SetGraphicsPipeline(demo.Pipeline(0))
            .SetViewport(new(8, 24, 112, 208)).SetScissor(new(8, 24, 112, 208))
            .SetDepthStencilState(writeMask).SetStencilReference(1).SetRootArguments(demo.Arguments(0)).Draw(3)
            .SetDepthStencilState(testMask).SetStencilReference(1).SetRootArguments(demo.Arguments(1)).Draw(6)
            .SetViewport(new(136, 24, 112, 208)).SetScissor(new(136, 24, 112, 208))
            .SetDepthStencilState(writeMask).SetStencilReference(2).SetRootArguments(demo.Arguments(0)).Draw(3)
            .SetDepthStencilState(testMask).SetStencilReference(2).SetRootArguments(demo.Arguments(1)).Draw(6);
    });

    [Story("Examples/3D/PipelineState/ViewportScissor", Height = 320, Order = 105,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget ViewportScissor(StoryContext ctx) => Build(ctx, DemoKind.ViewportScissor,
        [new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm))],
        static (demo, command, surface) =>
        command.BeginRendering(surface.ColorTarget, null, .05f, .06f, .08f, 1).SetGraphicsPipeline(demo.Pipeline(0))
            .SetViewport(new(16, 24, 96, 208)).SetScissor(new(0, 0, 256, 256)).SetRootArguments(demo.Arguments(0)).Draw(6)
            .SetViewport(new(144, 24, 96, 208)).SetScissor(new(144, 80, 96, 96)).SetRootArguments(demo.Arguments(1)).Draw(6));

    [Story("Examples/3D/PipelineState/Separation", Height = 320, Order = 106,
        CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget Separation(StoryContext ctx) => Build(ctx, DemoKind.Separation,
        [new GpuGraphicsPipelineDesc(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm, GpuFormat.D32Float))],
        static (demo, command, surface) =>
    {
        GpuDepthStencilState depth = GpuDepthStencilState.Default with { DepthTest = true, DepthWrite = true };
        command.BeginRendering(surface.ColorTarget, demo.DepthTarget, .05f, .06f, .08f, 1, clearDepth: 1).SetGraphicsPipeline(demo.Pipeline(0))
            .SetViewport(new(8, 8, 112, 112)).SetScissor(new(8, 8, 112, 112))
            .SetRasterizerState(GpuRasterizerState.Default).SetDepthStencilState(GpuDepthStencilState.Default).SetBlendState(GpuBlendState.None)
            .SetRootArguments(demo.Arguments(0)).Draw(3)
            .SetViewport(new(136, 8, 112, 112)).SetScissor(new(136, 8, 112, 112))
            .SetBlendState(GpuBlendState.AlphaBlend).SetRootArguments(demo.Arguments(0)).Draw(3).SetRootArguments(demo.Arguments(1)).Draw(3)
            .SetViewport(new(8, 136, 112, 112)).SetScissor(new(8, 136, 112, 112))
            .SetBlendState(GpuBlendState.None).SetRasterizerState(new(GpuCullMode.Back, GpuFrontFace.Clockwise)).SetRootArguments(demo.Arguments(0)).Draw(3)
            .SetViewport(new(136, 136, 112, 112)).SetScissor(new(136, 136, 112, 112))
            .SetRasterizerState(GpuRasterizerState.Default).SetDepthStencilState(depth).SetRootArguments(demo.Arguments(0)).Draw(3).SetRootArguments(demo.Arguments(1)).Draw(3);
    });

    [Story("Examples/3D/Depth", Height = 320, Order = 107,
        CapabilityNote = "Compatibility route backed by the browser-safe pipeline-state demo.")]
    public static Widget Depth(StoryContext ctx) => DepthStates(ctx);

    [Story("Examples/3D/Blend", Height = 320, Order = 108,
        CapabilityNote = "Compatibility route backed by the browser-safe pipeline-state demo.")]
    public static Widget Blend(StoryContext ctx) => BlendState(ctx);

    private static Widget Build(StoryContext ctx, DemoKind kind,
        IReadOnlyList<GpuGraphicsPipelineDesc> pipelineDescriptions, RecordDemo record)
    {
        if (ctx.DeviceOrNull is not { } device)
            return ctx.Snap(Frame(Muted("GPU runtime required")));

        var demo = new PipelineStateDemo(device, kind, pipelineDescriptions, record);
        return ctx.Snap(Frame(GpuView(256, 256, demo.Render, animated: false, dispose: demo.Dispose)));
    }

    private sealed class PipelineStateDemo : IDisposable
    {
        private const uint Size = 256;
        private readonly GpuDevice _device;
        private readonly DemoKind _kind;
        private readonly RecordDemo _record;
        private readonly List<GpuBuffer> _geometry = [];
        private readonly GpuPipeline[] _pipelines;
        private readonly GpuTexture? _depth;
        private GpuViewSurface? _renderedSurface;

        internal PipelineStateDemo(GpuDevice device, DemoKind kind,
            IReadOnlyList<GpuGraphicsPipelineDesc> pipelineDescriptions, RecordDemo record)
        {
            _device = device;
            _kind = kind;
            _record = record;
            GpuFormat? depthFormat = pipelineDescriptions[0].Attachments.DepthStencilFormat;
            if (depthFormat is { } format) _depth = device.CreateDepthTarget(Size, Size, format);

            GpuShaderCode shader = TriangleShader();
            _pipelines = pipelineDescriptions.Select(description =>
                device.CreateGraphicsPipeline(shader, description)).ToArray();
            CreateGeometry();
        }

        internal GpuViewRenderResult Render(GpuDevice device, GpuViewSurface surface, float time)
        {
            if (ReferenceEquals(_renderedSurface, surface)) return GpuViewRenderResult.Ready;

            using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
            _record(this, command, surface);
            command.EndRendering();
            surface.CopyColorToFramebuffer(command);
            command.Finish();
            device.MainQueue.Submit(command);
            _renderedSurface = surface;
            return GpuViewRenderResult.Ready;
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

        internal GpuPipeline Pipeline(int index) => _pipelines[index];
        internal GpuTexture? DepthTarget => _depth;
        internal DrawArgs Arguments(int index) => new() { VertexBufferIndex = _geometry[index].BindlessIndex };
        private static Vertex V(float x, float y, float z, float r, float g, float b, float a = 1)
            => new() { Px = x, Py = y, Pz = z, Pw = 1, R = r, G = g, B = b, A = a };

        public void Dispose()
        {
            _device.MainQueue.WaitIdle();
            foreach (GpuPipeline pipeline in _pipelines) pipeline.Dispose();
            foreach (GpuBuffer buffer in _geometry) buffer.Dispose();
            _depth?.Dispose();
        }
    }

    private static GpuShaderCode TriangleShader() => new()
    {
        SpirV = ShaderResource("triangle.spv"),
        DxilVertex = ShaderResource("triangle.vs.dxil"),
        DxilPixel = ShaderResource("triangle.ps.dxil"),
        Wgsl = ShaderResource("triangle.wgsl"),
    };

    private static byte[] ShaderResource(string fileName)
    {
        System.Reflection.Assembly assembly = typeof(PipelineStateStories).Assembly;
        string resourceName = assembly.GetManifestResourceNames().Single(name =>
            name.EndsWith("Shaders." + fileName, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded triangle shader is missing: {fileName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
