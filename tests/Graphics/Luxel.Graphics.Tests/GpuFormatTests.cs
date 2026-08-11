using Luxel.Graphics;

namespace Luxel.Tests;

public sealed class GpuFormatTests
{
    [Theory]
    [InlineData(GpuFormat.R8Unorm, 1u)]
    [InlineData(GpuFormat.Rg8Unorm, 2u)]
    [InlineData(GpuFormat.Rgba8Unorm, 4u)]
    [InlineData(GpuFormat.Bgra8Unorm, 4u)]
    [InlineData(GpuFormat.Rgba8UnormSrgb, 4u)]
    [InlineData(GpuFormat.Bgra8UnormSrgb, 4u)]
    [InlineData(GpuFormat.R32Float, 4u)]
    [InlineData(GpuFormat.D32Float, 4u)]
    public void BytesPerPixelIsSharedAcrossBackends(GpuFormat format, uint expected)
        => Assert.Equal(expected, GpuFormatInfo.BytesPerPixel(format));

    [Theory]
    [InlineData(GpuFormat.R8Unorm)]
    [InlineData(GpuFormat.Rg8Unorm)]
    [InlineData(GpuFormat.Rgba8Unorm)]
    [InlineData(GpuFormat.Bgra8Unorm)]
    [InlineData(GpuFormat.Rgba8UnormSrgb)]
    [InlineData(GpuFormat.Bgra8UnormSrgb)]
    public void PortableSampledFormatsAreFilterableOnEveryBackend(GpuFormat format)
        => Assert.True(GpuFormatInfo.IsPortableSampled(format));

    [Theory]
    [InlineData(GpuFormat.R32Float)]
    [InlineData(GpuFormat.D32Float)]
    public void NonPortableSampledFormatsRemainExcludedFromTheSharedAbi(GpuFormat format)
        => Assert.False(GpuFormatInfo.IsPortableSampled(format));
}
