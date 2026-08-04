using System.Runtime.InteropServices;

namespace Luxel.Tests;

public sealed class TutorialAbiTests
{
    [Fact]
    public void Triangle_vertex_and_draw_args_layouts_stay_compatible()
    {
        Assert.Equal(TutorialAbi.VertexSize, Marshal.SizeOf<TutorialAbi.Vertex>());
        Assert.Equal(0, Marshal.OffsetOf<TutorialAbi.Vertex>(nameof(TutorialAbi.Vertex.Px)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<TutorialAbi.Vertex>(nameof(TutorialAbi.Vertex.R)).ToInt32());
        Assert.Equal(TutorialAbi.DrawArgsSize, Marshal.SizeOf<TutorialAbi.DrawArgs>());
        Assert.Equal(0, Marshal.OffsetOf<TutorialAbi.DrawArgs>(nameof(TutorialAbi.DrawArgs.VertexBufferIndex)).ToInt32());
    }

    [Fact]
    public void Canonical_triangle_recipe_matches_tutorial_abi()
    {
        Assert.Equal(32, Marshal.SizeOf<CanonicalTriangleRecipe.Vertex>());
        Assert.Equal(4, Marshal.SizeOf<CanonicalTriangleRecipe.DrawArgs>());
        Assert.Equal(TutorialAbi.VertexSize, Marshal.SizeOf<CanonicalTriangleRecipe.Vertex>());
        Assert.Equal(TutorialAbi.DrawArgsSize, Marshal.SizeOf<CanonicalTriangleRecipe.DrawArgs>());
        Assert.Equal(3, CanonicalTriangleRecipe.CreateVertices().Length);
    }

    [Fact]
    public void Tutorial_3d_vertex_layout_matches_slang()
    {
        Assert.Equal(TutorialAbi.Vertex3DSize, Marshal.SizeOf<TutorialAbi.Vertex3D>());
        Assert.Equal(0, Marshal.OffsetOf<TutorialAbi.Vertex3D>(nameof(TutorialAbi.Vertex3D.Position)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<TutorialAbi.Vertex3D>(nameof(TutorialAbi.Vertex3D.Normal)).ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<TutorialAbi.Vertex3D>(nameof(TutorialAbi.Vertex3D.Uv)).ToInt32());
    }

    [Fact]
    public void Tutorial_3d_draw_args_layout_matches_slang_and_dword_root_constants()
    {
        Assert.Equal(192, GpuCommandBuffer.MaxRootArgumentBytes);
        Assert.Equal(0, TutorialAbi.DrawArgs3DSize % 4);
        Assert.Equal(TutorialAbi.DrawArgs3DSize, Marshal.SizeOf<TutorialAbi.DrawArgs3D>());
        Assert.InRange(TutorialAbi.DrawArgs3DSize, 1, GpuCommandBuffer.MaxRootArgumentBytes);
        Assert.Equal(0, Marshal.OffsetOf<TutorialAbi.DrawArgs3D>(nameof(TutorialAbi.DrawArgs3D.VertexBufferIndex)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<TutorialAbi.DrawArgs3D>(nameof(TutorialAbi.DrawArgs3D.IndexBufferIndex)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<TutorialAbi.DrawArgs3D>(nameof(TutorialAbi.DrawArgs3D.TextureIndex)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<TutorialAbi.DrawArgs3D>(nameof(TutorialAbi.DrawArgs3D.SamplerIndex)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<TutorialAbi.DrawArgs3D>(nameof(TutorialAbi.DrawArgs3D.Model)).ToInt32());
        Assert.Equal(80, Marshal.OffsetOf<TutorialAbi.DrawArgs3D>(nameof(TutorialAbi.DrawArgs3D.ViewProjection)).ToInt32());
        Assert.Equal(144, Marshal.OffsetOf<TutorialAbi.DrawArgs3D>(nameof(TutorialAbi.DrawArgs3D.LightDirection)).ToInt32());
        Assert.Equal(160, Marshal.OffsetOf<TutorialAbi.DrawArgs3D>(nameof(TutorialAbi.DrawArgs3D.Stage)).ToInt32());
    }

    [Fact]
    public void Tutorial_post_process_args_match_slang()
    {
        Assert.Equal(TutorialAbi.PostProcessArgsSize, Marshal.SizeOf<TutorialAbi.PostProcessArgs>());
        Assert.Equal(0, TutorialAbi.PostProcessArgsSize % 4);
        Assert.Equal(0, Marshal.OffsetOf<TutorialAbi.PostProcessArgs>(nameof(TutorialAbi.PostProcessArgs.SourceBufferIndex)).ToInt32());
        Assert.Equal(4, Marshal.OffsetOf<TutorialAbi.PostProcessArgs>(nameof(TutorialAbi.PostProcessArgs.DestinationBufferIndex)).ToInt32());
        Assert.Equal(8, Marshal.OffsetOf<TutorialAbi.PostProcessArgs>(nameof(TutorialAbi.PostProcessArgs.Width)).ToInt32());
        Assert.Equal(12, Marshal.OffsetOf<TutorialAbi.PostProcessArgs>(nameof(TutorialAbi.PostProcessArgs.Height)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<TutorialAbi.PostProcessArgs>(nameof(TutorialAbi.PostProcessArgs.StridePixels)).ToInt32());
    }

    [Fact]
    public void Visible_aspect_uses_client_width_not_aligned_readback_stride()
    {
        Assert.Equal(801f / 603f, TutorialAbi.VisibleAspect(801, 603));
        Assert.NotEqual(832f / 603f, TutorialAbi.VisibleAspect(801, 603));
        Assert.Throws<ArgumentOutOfRangeException>(() => TutorialAbi.VisibleAspect(0, 603));
    }

    [Fact]
    public void Raster_defaults_preserve_existing_no_cull_behavior()
    {
        GpuRasterDesc raster = GpuRasterDesc.Default(GpuFormat.Rgba8Unorm);
        Assert.Equal(GpuCullMode.None, raster.CullMode);
        Assert.Equal(GpuFrontFace.CounterClockwise, raster.FrontFace);
    }
}
