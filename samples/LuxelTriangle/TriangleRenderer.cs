using System.Runtime.InteropServices;
using Luxel;

internal sealed class TriangleRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public float Px, Py, Pz, Pw;
        public float R, G, B, A;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrawArgs
    {
        public uint VertexBufferIndex;
    }

    private readonly GpuDevice _device;
    private readonly GpuBuffer _vertices;
    private readonly GpuPipeline _pipeline;
    private GpuTexture? _target;
    private GpuBuffer? _framebuffer;
    private uint _height;

    public TriangleRenderer(GpuDevice device)
    {
        _device = device;
        _vertices = device.Malloc(3 * 32, GpuMemoryKind.HostMapped);
        Span<Vertex> vertices = _vertices.Span<Vertex>(3);
        vertices[0] = new Vertex { Px = 0, Py = -0.72f, Pz = 0, Pw = 1, R = 1, G = 0.18f, B = 0.18f, A = 1 };
        vertices[1] = new Vertex { Px = 0.72f, Py = 0.62f, Pz = 0, Pw = 1, R = 0.18f, G = 1, B = 0.28f, A = 1 };
        vertices[2] = new Vertex { Px = -0.72f, Py = 0.62f, Pz = 0, Pw = 1, R = 0.2f, G = 0.42f, B = 1, A = 1 };
        _pipeline = device.CreateGraphicsPipeline(
            GpuShaderCode.Load("tutorial_triangle"),
            GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
    }

    public GpuBuffer Framebuffer
        => _framebuffer ?? throw new InvalidOperationException("Renderer has no framebuffer. Call Resize with a positive size first.");

    public uint StridePixels { get; private set; }

    public void Resize(int width, int height)
    {
        _framebuffer?.Dispose();
        _framebuffer = null;
        _target?.Dispose();
        _target = null;
        StridePixels = 0;
        _height = 0;

        if (width <= 0 || height <= 0) return;
        StridePixels = (uint)Align(width, 64);   // D3D12 texture readback rows require 256-byte alignment.
        _height = (uint)height;
        _target = _device.CreateRenderTarget(StridePixels, _height, GpuFormat.Rgba8Unorm);
        _framebuffer = _device.Malloc(checked((ulong)StridePixels * _height * 4), GpuMemoryKind.HostMapped);
    }

    public void Render()
    {
        if (_target is null || _framebuffer is null) return;
        var args = new DrawArgs { VertexBufferIndex = _vertices.BindlessIndex };
        using GpuCommandBuffer command = _device.MainQueue.StartCommandRecording();
        command.BeginRendering(_target, null, 0.055f, 0.07f, 0.11f, 1)
            .SetGraphicsPipeline(_pipeline)
            .SetRootArguments(args)
            .Draw(3)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToBuffer(_target, _framebuffer);
        command.Finish();
        _device.MainQueue.SubmitAndWait(command);
    }

    public void Dispose()
    {
        _device.MainQueue.WaitIdle();
        _framebuffer?.Dispose();
        _target?.Dispose();
        _pipeline.Dispose();
        _vertices.Dispose();
    }

    private static int Align(int value, int alignment)
        => checked((value + alignment - 1) / alignment * alignment);
}
