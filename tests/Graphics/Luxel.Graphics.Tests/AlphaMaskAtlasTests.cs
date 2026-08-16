using Luxel.Graphics.TwoD;

namespace Luxel.Tests;

public class AlphaMaskAtlasTests
{
    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 8)]
    [InlineData(17, 20)]
    public void RequiredRowStride_AlignsR8RowsToFourBytes(int width, int expected)
        => Assert.Equal(expected, AlphaMaskAtlas.RequiredRowStride(width));

    [Fact]
    public void Bind_RejectsUnalignedOrShortRowStride()
    {
        var atlas = new AlphaMaskAtlas();
        Assert.Throws<ArgumentOutOfRangeException>(() => atlas.Bind(1, 9, 8, rowStrideBytes: 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => atlas.Bind(1, 9, 8, rowStrideBytes: 10));
    }

    [Fact]
    public void DrawMask_RequiresBoundAtlasAndContainedSource()
    {
        var scene = new Scene2D();
        var atlas = new AlphaMaskAtlas();
        var destination = new RectF(10, 20, 6, 8);

        Assert.Throws<InvalidOperationException>(() =>
            scene.DrawMask(atlas, new MaskRect(0, 0, 6, 8), destination, Color2D.Black));

        atlas.Bind(srcIndex: 7, width: 32, height: 16);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scene.DrawMask(atlas, new MaskRect(30, 0, 6, 8), destination, Color2D.Black));
    }

    [Fact]
    public void DrawMask_EncodesR8SourceColorSamplingAndDestination()
    {
        var atlas = new AlphaMaskAtlas();
        atlas.Bind(srcIndex: 7, width: 31, height: 16); // default stride = 32 bytes
        uint color = Color2D.Rgba(20, 40, 60, 192);

        var scene = new Scene2D();
        scene.DrawMask(
            atlas,
            new MaskRect(11, 3, 6, 8),
            new RectF(10.5f, 20.25f, 12, 16),
            color,
            MaskSampling.Linear,
            absoluteColor: true);

        (_, GpuPath[] paths, GpuStyle[] styles) = PathEncoder.Encode(scene);
        GpuPath path = Assert.Single(paths);
        Assert.Equal(3u, path.Kind);
        Assert.Equal((uint)MaskSampling.Linear, path.FillRule);
        Assert.Equal(7u, path.SrcIndex);
        Assert.Equal(32u, path.SrcStride);
        Assert.Equal(11u, path.SrcX);
        Assert.Equal(3u, path.SrcY);
        Assert.Equal(6u, path.SrcW);
        Assert.Equal(8u, path.SrcH);
        Assert.Equal(10.5f, path.BMinX);
        Assert.Equal(20.25f, path.BMinY);
        Assert.Equal(22.5f, path.BMaxX);
        Assert.Equal(36.25f, path.BMaxY);
        Assert.Equal(color, Assert.Single(styles).ColorRgba);
        Assert.True(Assert.Single(scene.Shapes).AbsoluteColor);
    }

    [Fact]
    public void MaskSubRect_EncodesNearestSampling()
    {
        var scene = new Scene2D();
        scene.MaskSubRect(2, 16, 1, 2, 5, 7, 0, 0, 5, 7, Color2D.White, MaskSampling.Nearest);

        (_, GpuPath[] paths, _) = PathEncoder.Encode(scene);
        Assert.Equal((uint)MaskSampling.Nearest, Assert.Single(paths).FillRule);
    }

    [Fact]
    public void MaskSubRect_RejectsSourceOutsideRowStride()
    {
        var scene = new Scene2D();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            scene.MaskSubRect(2, 16, 14, 0, 4, 7, 0, 0, 4, 7, Color2D.White));
    }
}
