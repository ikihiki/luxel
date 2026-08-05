using System.Runtime.InteropServices;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.StoryKit;

namespace Luxel.Gallery.Stories;

/// <summary>
/// Deterministic examples of Luxel's separated graphics pipeline description and command-time state blocks.
/// Each scene uses the same vertex-pulling shader and changes only the state named by the story.
/// </summary>
public static class PipelineStateStories
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex { public float Px, Py, Pz, Pw, R, G, B, A; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs { public uint VertexBufferIndex; }

    [Story("Examples/3D/PipelineState/Topology", Height = 320, Order = 100)]
    public static Widget Topology() => View(new TopologyScene());

    [Story("Examples/3D/PipelineState/Rasterizer", Height = 320, Order = 101)]
    public static Widget Rasterizer() => View(new RasterizerScene());

    [Story("Examples/3D/PipelineState/Depth", Height = 320, Order = 102)]
    public static Widget DepthStates() => View(new DepthScene());

    [Story("Examples/3D/PipelineState/Blend", Height = 320, Order = 103)]
    public static Widget BlendState() => View(new BlendScene());

    [Story("Examples/3D/PipelineState/Stencil", Height = 320, Order = 104)]
    public static Widget Stencil() => View(new StencilScene());

    [Story("Examples/3D/PipelineState/ViewportScissor", Height = 320, Order = 105)]
    public static Widget ViewportScissor() => View(new ViewportScissorScene());

    [Story("Examples/3D/PipelineState/Separation", Height = 320, Order = 106)]
    public static Widget Separation() => View(new SeparationScene());

    // Keep the established public routes for bookmarks and existing documentation embeds.
    [Story("Examples/3D/Depth", Height = 320, Order = 107)]
    public static Widget Depth() => View(new DepthScene());

    [Story("Examples/3D/Blend", Height = 320, Order = 108)]
    public static Widget Blend() => View(new BlendScene());

    private static Widget View(GpuSceneBase scene)
        => Frame(GpuSceneBase.View(256, 256, scene, animated: false));

    private static Vertex V(float x, float y, float z, float r, float g, float b, float a = 1)
        => new() { Px = x, Py = y, Pz = z, Pw = 1, R = r, G = g, B = b, A = a };

    private static GpuBuffer MakeGeometry(GpuDevice device, Vertex[] vertices)
    {
        var buffer = device.Malloc((ulong)(vertices.Length * Marshal.SizeOf<Vertex>()), GpuMemoryKind.HostMapped);
        vertices.AsSpan().CopyTo(buffer.Span<Vertex>(vertices.Length));
        return buffer;
    }

    private static GpuGraphicsPipelineDesc PipelineDesc(
        GpuPrimitiveTopology topology = GpuPrimitiveTopology.TriangleList,
        GpuFormat? depthStencilFormat = null)
        => new(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm, depthStencilFormat), topology);

    private static void Finish(PipelineStateScene scene, GpuCommandBuffer command)
    {
        command.EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(scene.TargetForCopy, scene.BufferForCopy);
        command.Finish();
        scene.DeviceForSubmit.MainQueue.SubmitAndWait(command);
    }

    private abstract class PipelineStateScene : GpuSceneBase
    {
        internal GpuDevice DeviceForSubmit => Device;
        internal GpuTexture TargetForCopy => Target;
        internal GpuBuffer BufferForCopy => OutBuffer;

        protected GpuPipeline MakePipeline(GpuPrimitiveTopology topology = GpuPrimitiveTopology.TriangleList,
            GpuFormat? depthStencilFormat = null)
            => Track(Device.CreateGraphicsPipeline(GpuShaderCode.Load("triangle"), PipelineDesc(topology, depthStencilFormat)));

        protected GpuBuffer Geometry(params Vertex[] vertices) => Track(MakeGeometry(Device, vertices));
        protected static DrawArgs Args(GpuBuffer geometry) => new() { VertexBufferIndex = geometry.BindlessIndex };
    }

    private sealed class TopologyScene : PipelineStateScene
    {
        private GpuPipeline _listPipeline = null!, _stripPipeline = null!;
        private GpuBuffer _list = null!, _strip = null!;

        protected override void OnInit()
        {
            _listPipeline = MakePipeline(GpuPrimitiveTopology.TriangleList);
            _stripPipeline = MakePipeline(GpuPrimitiveTopology.TriangleStrip);
            _list = Geometry(
                V(-.8f, -.7f, 0, .95f, .25f, .18f), V(.8f, -.7f, 0, .95f, .25f, .18f), V(-.8f, .7f, 0, .95f, .25f, .18f),
                V(.8f, -.7f, 0, .95f, .25f, .18f), V(.8f, .7f, 0, .95f, .25f, .18f), V(-.8f, .7f, 0, .95f, .25f, .18f));
            _strip = Geometry(
                V(-.8f, -.7f, 0, .2f, .7f, 1), V(.8f, -.7f, 0, .2f, .7f, 1),
                V(-.8f, .7f, 0, .2f, .7f, 1), V(.8f, .7f, 0, .2f, .7f, 1));
        }

        protected override void OnRender(float time)
        {
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            cmd.BeginRendering(Target, null, .05f, .06f, .08f, 1)
                .SetViewport(new(8, 24, 112, 208)).SetScissor(new(8, 24, 112, 208))
                .SetGraphicsPipeline(_listPipeline).SetRootArguments(Args(_list)).Draw(6)
                .SetViewport(new(136, 24, 112, 208)).SetScissor(new(136, 24, 112, 208))
                .SetGraphicsPipeline(_stripPipeline).SetRootArguments(Args(_strip)).Draw(4);
            Finish(this, cmd);
        }
    }

    private sealed class RasterizerScene : PipelineStateScene
    {
        private GpuPipeline _pipeline = null!;
        private GpuBuffer _triangle = null!;

        protected override void OnInit()
        {
            _pipeline = MakePipeline();
            _triangle = Geometry(V(-.75f, .7f, 0, .95f, .65f, .12f), V(-.75f, -.7f, 0, .95f, .65f, .12f), V(.75f, -.7f, 0, .95f, .65f, .12f));
        }

        protected override void OnRender(float time)
        {
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            cmd.BeginRendering(Target, null, .05f, .06f, .08f, 1).SetGraphicsPipeline(_pipeline)
                .SetViewport(new(4, 24, 76, 208)).SetScissor(new(4, 24, 76, 208))
                .SetRasterizerState(GpuRasterizerState.Default).SetRootArguments(Args(_triangle)).Draw(3)
                .SetViewport(new(90, 24, 76, 208)).SetScissor(new(90, 24, 76, 208))
                .SetRasterizerState(new(GpuCullMode.Back, GpuFrontFace.CounterClockwise)).Draw(3)
                .SetViewport(new(176, 24, 76, 208)).SetScissor(new(176, 24, 76, 208))
                .SetRasterizerState(new(GpuCullMode.Back, GpuFrontFace.Clockwise)).Draw(3);
            Finish(this, cmd);
        }
    }

    private sealed class DepthScene : PipelineStateScene
    {
        private GpuTexture _depth = null!;
        private GpuPipeline _pipeline = null!;
        private GpuBuffer _near = null!, _far = null!;

        protected override void OnInit()
        {
            _depth = Track(Device.CreateDepthTarget(W, H, GpuFormat.D32Float));
            _pipeline = MakePipeline(depthStencilFormat: GpuFormat.D32Float);
            _near = Geometry(V(-.8f, -.7f, .3f, .15f, .9f, .35f), V(.8f, -.7f, .3f, .15f, .9f, .35f), V(0, .75f, .3f, .15f, .9f, .35f));
            _far = Geometry(V(-.8f, .7f, .7f, .95f, .18f, .16f), V(.8f, .7f, .7f, .95f, .18f, .16f), V(0, -.75f, .7f, .95f, .18f, .16f));
        }

        protected override void OnRender(float time)
        {
            GpuDepthStencilState testWrite = GpuDepthStencilState.Default with { DepthTest = true, DepthWrite = true };
            GpuDepthStencilState testNoWrite = testWrite with { DepthWrite = false };
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            cmd.BeginRendering(Target, _depth, .05f, .06f, .08f, 1, clearDepth: 1)
                .SetGraphicsPipeline(_pipeline)
                .SetViewport(new(4, 24, 76, 208)).SetScissor(new(4, 24, 76, 208))
                .SetDepthStencilState(GpuDepthStencilState.Default).SetRootArguments(Args(_near)).Draw(3).SetRootArguments(Args(_far)).Draw(3)
                .SetViewport(new(90, 24, 76, 208)).SetScissor(new(90, 24, 76, 208))
                .SetDepthStencilState(testWrite).SetRootArguments(Args(_near)).Draw(3).SetRootArguments(Args(_far)).Draw(3)
                .SetViewport(new(176, 24, 76, 208)).SetScissor(new(176, 24, 76, 208))
                .SetDepthStencilState(testNoWrite).SetRootArguments(Args(_near)).Draw(3).SetRootArguments(Args(_far)).Draw(3);
            Finish(this, cmd);
        }
    }

    private sealed class BlendScene : PipelineStateScene
    {
        private GpuPipeline _pipeline = null!;
        private GpuBuffer _red = null!, _blue = null!;

        protected override void OnInit()
        {
            _pipeline = MakePipeline();
            _red = Geometry(V(-.8f, -.7f, 0, 1, .12f, .12f), V(.8f, -.7f, 0, 1, .12f, .12f), V(0, .75f, 0, 1, .12f, .12f));
            _blue = Geometry(V(-.8f, .7f, 0, .08f, .25f, 1, .5f), V(.8f, .7f, 0, .08f, .25f, 1, .5f), V(0, -.75f, 0, .08f, .25f, 1, .5f));
        }

        protected override void OnRender(float time)
        {
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            cmd.BeginRendering(Target, null, .05f, .06f, .08f, 1).SetGraphicsPipeline(_pipeline)
                .SetViewport(new(8, 24, 112, 208)).SetScissor(new(8, 24, 112, 208))
                .SetBlendState(GpuBlendState.None).SetRootArguments(Args(_red)).Draw(3).SetRootArguments(Args(_blue)).Draw(3)
                .SetViewport(new(136, 24, 112, 208)).SetScissor(new(136, 24, 112, 208))
                .SetBlendState(GpuBlendState.AlphaBlend).SetRootArguments(Args(_red)).Draw(3).SetRootArguments(Args(_blue)).Draw(3);
            Finish(this, cmd);
        }
    }

    private sealed class StencilScene : PipelineStateScene
    {
        private GpuTexture _depthStencil = null!;
        private GpuPipeline _pipeline = null!;
        private GpuBuffer _mask = null!, _fill = null!;

        protected override void OnInit()
        {
            _depthStencil = Track(Device.CreateDepthTarget(W, H, GpuFormat.Depth24PlusStencil8));
            _pipeline = MakePipeline(depthStencilFormat: GpuFormat.Depth24PlusStencil8);
            _mask = Geometry(V(-.8f, -.75f, .5f, .18f, .2f, .26f), V(.8f, -.75f, .5f, .18f, .2f, .26f), V(0, .8f, .5f, .18f, .2f, .26f));
            _fill = Geometry(
                V(-1, -1, .4f, 1, .75f, .12f), V(1, -1, .4f, 1, .75f, .12f), V(-1, 1, .4f, 1, .75f, .12f),
                V(1, -1, .4f, 1, .75f, .12f), V(1, 1, .4f, 1, .75f, .12f), V(-1, 1, .4f, 1, .75f, .12f));
        }

        protected override void OnRender(float time)
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

            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            cmd.BeginRendering(Target, _depthStencil, .05f, .06f, .08f, 1, clearDepth: 1, clearStencil: 0)
                .SetGraphicsPipeline(_pipeline)
                .SetViewport(new(8, 24, 112, 208)).SetScissor(new(8, 24, 112, 208))
                .SetDepthStencilState(writeMask).SetStencilReference(1).SetRootArguments(Args(_mask)).Draw(3)
                .SetDepthStencilState(testMask).SetStencilReference(1).SetRootArguments(Args(_fill)).Draw(6)
                .SetViewport(new(136, 24, 112, 208)).SetScissor(new(136, 24, 112, 208))
                .SetDepthStencilState(writeMask).SetStencilReference(2).SetRootArguments(Args(_mask)).Draw(3)
                .SetDepthStencilState(testMask).SetStencilReference(2).SetRootArguments(Args(_fill)).Draw(6);
            Finish(this, cmd);
        }
    }

    private sealed class ViewportScissorScene : PipelineStateScene
    {
        private GpuPipeline _pipeline = null!;
        private GpuBuffer _red = null!, _blue = null!;

        protected override void OnInit()
        {
            _pipeline = MakePipeline();
            _red = Geometry(V(-1, -1, 0, .95f, .22f, .16f), V(1, -1, 0, .95f, .22f, .16f), V(-1, 1, 0, .95f, .22f, .16f), V(1, -1, 0, .95f, .22f, .16f), V(1, 1, 0, .95f, .22f, .16f), V(-1, 1, 0, .95f, .22f, .16f));
            _blue = Geometry(V(-1, -1, 0, .15f, .55f, 1), V(1, -1, 0, .15f, .55f, 1), V(-1, 1, 0, .15f, .55f, 1), V(1, -1, 0, .15f, .55f, 1), V(1, 1, 0, .15f, .55f, 1), V(-1, 1, 0, .15f, .55f, 1));
        }

        protected override void OnRender(float time)
        {
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            cmd.BeginRendering(Target, null, .05f, .06f, .08f, 1).SetGraphicsPipeline(_pipeline)
                .SetViewport(new(16, 24, 96, 208)).SetScissor(new(0, 0, 256, 256)).SetRootArguments(Args(_red)).Draw(6)
                .SetViewport(new(144, 24, 96, 208)).SetScissor(new(144, 80, 96, 96)).SetRootArguments(Args(_blue)).Draw(6);
            Finish(this, cmd);
        }
    }

    private sealed class SeparationScene : PipelineStateScene
    {
        private GpuTexture _depth = null!;
        private GpuPipeline _pipeline = null!;
        private GpuBuffer _opaque = null!, _alpha = null!;

        protected override void OnInit()
        {
            _depth = Track(Device.CreateDepthTarget(W, H, GpuFormat.D32Float));
            _pipeline = MakePipeline(depthStencilFormat: GpuFormat.D32Float);
            _opaque = Geometry(V(-.8f, .75f, .35f, .18f, .85f, .35f), V(-.8f, -.75f, .35f, .18f, .85f, .35f), V(.8f, -.75f, .35f, .18f, .85f, .35f));
            _alpha = Geometry(V(-.8f, -.7f, .65f, .2f, .35f, 1, .55f), V(.8f, -.7f, .65f, .2f, .35f, 1, .55f), V(0, .8f, .65f, .2f, .35f, 1, .55f));
        }

        protected override void OnRender(float time)
        {
            GpuDepthStencilState depth = GpuDepthStencilState.Default with { DepthTest = true, DepthWrite = true };
            using GpuCommandBuffer cmd = Device.MainQueue.StartCommandRecording();
            cmd.BeginRendering(Target, _depth, .05f, .06f, .08f, 1, clearDepth: 1).SetGraphicsPipeline(_pipeline)
                .SetViewport(new(8, 8, 112, 112)).SetScissor(new(8, 8, 112, 112))
                .SetRasterizerState(GpuRasterizerState.Default).SetDepthStencilState(GpuDepthStencilState.Default).SetBlendState(GpuBlendState.None)
                .SetRootArguments(Args(_opaque)).Draw(3)
                .SetViewport(new(136, 8, 112, 112)).SetScissor(new(136, 8, 112, 112))
                .SetBlendState(GpuBlendState.AlphaBlend).SetRootArguments(Args(_opaque)).Draw(3).SetRootArguments(Args(_alpha)).Draw(3)
                .SetViewport(new(8, 136, 112, 112)).SetScissor(new(8, 136, 112, 112))
                .SetBlendState(GpuBlendState.None).SetRasterizerState(new(GpuCullMode.Back, GpuFrontFace.Clockwise)).SetRootArguments(Args(_opaque)).Draw(3)
                .SetViewport(new(136, 136, 112, 112)).SetScissor(new(136, 136, 112, 112))
                .SetRasterizerState(GpuRasterizerState.Default).SetDepthStencilState(depth).SetRootArguments(Args(_opaque)).Draw(3).SetRootArguments(Args(_alpha)).Draw(3);
            Finish(this, cmd);
        }
    }
}
