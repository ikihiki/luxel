using System.Runtime.InteropServices;

namespace Luxel.Tests;

public sealed class TutorialAbiTests
{
    [Fact]
    public void Triangle_vertex_layout_matches_slang()
    {
        Assert.Equal(TutorialAbi.VertexSize, Marshal.SizeOf<TutorialAbi.Vertex>());
        Assert.Equal(0, Marshal.OffsetOf<TutorialAbi.Vertex>(nameof(TutorialAbi.Vertex.Px)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<TutorialAbi.Vertex>(nameof(TutorialAbi.Vertex.Pw)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<TutorialAbi.Vertex>(nameof(TutorialAbi.Vertex.R)).ToInt32());
        Assert.Equal(28, Marshal.OffsetOf<TutorialAbi.Vertex>(nameof(TutorialAbi.Vertex.A)).ToInt32());
    }

    [Fact]
    public void Triangle_draw_args_layout_matches_slang_and_dword_root_constants()
    {
        Assert.Equal(192, GpuCommandBuffer.MaxRootArgumentBytes);
        Assert.Equal(0, TutorialAbi.DrawArgsSize % 4);
        Assert.Equal(TutorialAbi.DrawArgsSize, Marshal.SizeOf<TutorialAbi.DrawArgs>());
        Assert.Equal(0, Marshal.OffsetOf<TutorialAbi.DrawArgs>(nameof(TutorialAbi.DrawArgs.VertexBufferIndex)).ToInt32());
    }
}
