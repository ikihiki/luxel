using System.Runtime.InteropServices;
using Luxel.AssetsGpu;
using Luxel.Controls;
using Luxel.Graphics;
using Luxel.Graphics.RenderGraph;
using Luxel.Resources;
using Luxel.Shaders;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;
using Rg = Luxel.Graphics.RenderGraph.RenderGraph;

namespace Luxel.Gallery.Stories;

/// <summary>Browser-safe RenderGraph examples shared by native Gallery and the WebAssembly runtime.</summary>
[StoryMeta("Examples/RenderGraph")]
public static class BrowserRenderGraphStories
{
    private const uint Width = 256;
    private const uint Height = 256;

    [StructLayout(LayoutKind.Sequential)]
    private struct BlurArgs
    {
        public uint SrcIndex, DstIndex, Width, Height;
        public int DirX, DirY;
        public uint Pad0, Pad1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositeArgs
    {
        public uint UiIndex, BlurIndex, DstIndex, Width, Height, SplitX, Pad0, Pad1;
    }

    /// <summary>UI pattern → separable blur → split composite. Runs in native Gallery and browser WebAssembly.</summary>
    [Story(CapabilityNote = "Runs through the shared Gallery WebAssembly story runner.")]
    public static Widget Blur(StoryContext ctx)
    {
        if (ctx.DeviceOrNull is not { } device || ctx.ScopedResourcesOrNull is not { } resources)
            return ctx.Snap(Frame(GpuView(256, 256,
                static (_, _, _) => GpuViewRenderResult.Failed,
                animated: false)));

        ResourceHandle<GpuShaderCode> blurShader = resources.Create<SlangSource, GpuShaderCode>(
            "render-graph.blur.slang", new SlangSource("render-graph.blur.slang", BlurSlang), "compute");
        ResourceHandle<GpuShaderCode> compositeShader = resources.Create<SlangSource, GpuShaderCode>(
            "render-graph.composite.slang", new SlangSource("render-graph.composite.slang", CompositeSlang), "compute");
        ResourceHandle<GpuPipeline> blurPipeline = resources.CreateComputePipeline(
            "render-graph.blur.pipeline", blurShader);
        ResourceHandle<GpuPipeline> compositePipeline = resources.CreateComputePipeline(
            "render-graph.composite.pipeline", compositeShader);
        Signal<ResourceState> blurState = ctx.Observe(blurPipeline);
        Signal<ResourceState> compositeState = ctx.Observe(compositePipeline);

        GpuBuffer input = device.Malloc((ulong)(Width * Height * 4), GpuMemoryKind.HostMapped);
        FillInput(input.Span<byte>());
        Rg? graph = null;
        GpuViewSurface? generation = null;
        bool rendered = false;

        return ctx.Snap(Frame(GpuView(
            Width,
            Height,
            (gpu, surface, _) =>
            {
                ResourceState blur = blurState.Value;
                ResourceState composite = compositeState.Value;
                if (!blur.HasValue || !composite.HasValue)
                    return blur.Status == ResourceStatus.Failed || composite.Status == ResourceStatus.Failed
                        ? GpuViewRenderResult.Failed
                        : GpuViewRenderResult.Loading;

                if (!ReferenceEquals(generation, surface))
                {
                    graph?.Dispose();
                    graph = BuildGraph(gpu, surface, input, blurPipeline.Value, compositePipeline.Value);
                    generation = surface;
                    rendered = false;
                }
                if (rendered) return GpuViewRenderResult.Ready;

                using GpuCommandBuffer command = gpu.MainQueue.StartCommandRecording();
                graph!.Execute(command);
                command.Finish();
                gpu.MainQueue.Submit(command);
                rendered = true;
                return GpuViewRenderResult.Ready;
            },
            animated: false,
            dispose: () =>
            {
                graph?.Dispose();
                input.Dispose();
            })));

        static Rg BuildGraph(GpuDevice device, GpuViewSurface surface, GpuBuffer input,
            GpuPipeline blurPipeline, GpuPipeline compositePipeline)
        {
            ulong bytes = (ulong)(Width * Height * 4);
            var graph = new Rg(device);
            BufferHandle source = graph.ImportBuffer(input, "ui");
            BufferHandle horizontal = graph.CreateBuffer(
                new BufferDesc(bytes, GpuMemoryKind.DeviceLocal), "blur-horizontal");
            BufferHandle vertical = graph.CreateBuffer(
                new BufferDesc(bytes, GpuMemoryKind.DeviceLocal), "blur-vertical");
            BufferHandle output = graph.ImportBuffer(surface.Framebuffer, "output");

            graph.AddPass("BlurH", PassQueue.Compute)
                .Read(source)
                .Write(horizontal)
                .Execute(pass => Dispatch(pass, blurPipeline, new BlurArgs
                {
                    SrcIndex = pass.BindlessIndex(source),
                    DstIndex = pass.BindlessIndex(horizontal),
                    Width = Width,
                    Height = Height,
                    DirX = 1,
                }));
            graph.AddPass("BlurV", PassQueue.Compute)
                .Read(horizontal)
                .Write(vertical)
                .Execute(pass => Dispatch(pass, blurPipeline, new BlurArgs
                {
                    SrcIndex = pass.BindlessIndex(horizontal),
                    DstIndex = pass.BindlessIndex(vertical),
                    Width = Width,
                    Height = Height,
                    DirY = 1,
                }));
            graph.AddPass("Composite", PassQueue.Compute)
                .Read(source)
                .Read(vertical)
                .Write(output)
                .Execute(pass => Dispatch(pass, compositePipeline, new CompositeArgs
                {
                    UiIndex = pass.BindlessIndex(source),
                    BlurIndex = pass.BindlessIndex(vertical),
                    DstIndex = pass.BindlessIndex(output),
                    Width = Width,
                    Height = Height,
                    SplitX = Width / 2,
                }));
            return graph;
        }
    }

    private static void Dispatch<T>(PassContext pass, GpuPipeline pipeline, T args) where T : unmanaged
        => pass.Cmd.SetComputePipeline(pipeline)
            .SetRootArguments(args)
            .Dispatch((Width + 7) / 8, (Height + 7) / 8);

    private static void FillInput(Span<byte> pixels)
    {
        pixels.Clear();
        FillRect(pixels, 20, 20, 100, 60, 60, 130, 240);
        FillRect(pixels, 140, 30, 90, 90, 230, 80, 100);
        FillRect(pixels, 30, 110, 120, 40, 40, 200, 120);
        FillRect(pixels, 170, 140, 60, 90, 255, 220, 60);
        FillRect(pixels, 20, 170, 130, 60, 180, 90, 220);
    }

    private static void FillRect(Span<byte> pixels, int x, int y, int width, int height,
        byte red, byte green, byte blue)
    {
        for (int py = y; py < y + height; py++)
        for (int px = x; px < x + width; px++)
        {
            int offset = (py * (int)Width + px) * 4;
            pixels[offset] = red;
            pixels[offset + 1] = green;
            pixels[offset + 2] = blue;
            pixels[offset + 3] = 255;
        }
    }

    private const string BlurSlang = """
        [[vk::binding(0, 0)]] RWByteAddressBuffer g_buffers[];
        struct Args { uint srcIndex; uint dstIndex; uint width; uint height; int dirX; int dirY; uint pad0; uint pad1; };
        [[vk::push_constant]] Args args;

        float4 loadPixel(int x, int y)
        {
            x = clamp(x, 0, int(args.width) - 1);
            y = clamp(y, 0, int(args.height) - 1);
            uint packed = g_buffers[args.srcIndex].Load((y * args.width + x) * 4);
            return float4((packed & 255u) / 255.0, ((packed >> 8) & 255u) / 255.0,
                ((packed >> 16) & 255u) / 255.0, ((packed >> 24) & 255u) / 255.0);
        }

        void storePixel(int x, int y, float4 color)
        {
            uint4 c = uint4(saturate(color) * 255.0 + 0.5);
            uint packed = c.x | (c.y << 8) | (c.z << 16) | (c.w << 24);
            g_buffers[args.dstIndex].Store((y * args.width + x) * 4, packed);
        }

        [shader("compute")]
        [numthreads(8, 8, 1)]
        void main(uint3 tid : SV_DispatchThreadID)
        {
            int x = int(tid.x), y = int(tid.y);
            if (x >= int(args.width) || y >= int(args.height)) return;
            const float weights[5] = { 0.240, 0.180, 0.120, 0.060, 0.020 };
            float4 sum = 0;
            for (int i = -4; i <= 4; i++)
                sum += loadPixel(x + args.dirX * i, y + args.dirY * i) * weights[abs(i)];
            storePixel(x, y, sum);
        }
        """;

    private const string CompositeSlang = """
        [[vk::binding(0, 0)]] RWByteAddressBuffer g_buffers[];
        struct Args { uint uiIndex; uint blurIndex; uint dstIndex; uint width; uint height; uint splitX; uint pad0; uint pad1; };
        [[vk::push_constant]] Args args;

        uint loadPixel(uint bufferIndex, uint x, uint y)
        {
            return g_buffers[bufferIndex].Load((y * args.width + x) * 4);
        }

        [shader("compute")]
        [numthreads(8, 8, 1)]
        void main(uint3 tid : SV_DispatchThreadID)
        {
            if (tid.x >= args.width || tid.y >= args.height) return;
            uint pixel = loadPixel(tid.x < args.splitX ? args.uiIndex : args.blurIndex, tid.x, tid.y);
            if (tid.x == args.splitX || tid.x == args.splitX + 1) pixel = 0xffd9d9d9u;
            g_buffers[args.dstIndex].Store((tid.y * args.width + tid.x) * 4, pixel);
        }
        """;
}
