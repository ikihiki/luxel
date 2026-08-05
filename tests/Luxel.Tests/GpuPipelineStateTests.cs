namespace Luxel.Tests;

public sealed class GpuPipelineStateTests
{
    [Fact]
    public void Defaults_are_portable_and_stencil_disabled_state_is_canonical()
    {
        GpuDepthStencilState value = GpuDepthStencilState.Default with
        {
            StencilFront = new(GpuCompareOp.Less, GpuStencilOp.Replace, GpuStencilOp.Invert, GpuStencilOp.Zero),
            StencilReadMask = 3,
            StencilWriteMask = 7,
        };
        GpuDepthStencilState normalized = value.Normalize();
        Assert.Equal(GpuCompareOp.LessEqual, normalized.DepthCompare);
        Assert.Equal(GpuStencilFaceState.Default, normalized.StencilFront);
        Assert.Equal(0xffu, normalized.StencilReadMask);
        Assert.Equal(0xffu, normalized.StencilWriteMask);
    }

    [Fact]
    public void Stencil_values_are_limited_to_eight_bits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => (GpuDepthStencilState.Default with { StencilReadMask = 256 }).Normalize());
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuDepthStencilState.ValidateByte(256, "reference"));
    }

    [Fact]
    public void Combined_depth_stencil_format_is_classified_without_portable_byte_size()
    {
        Assert.True(GpuFormatInfo.HasDepth(GpuFormat.Depth24PlusStencil8));
        Assert.True(GpuFormatInfo.HasStencil(GpuFormat.Depth24PlusStencil8));
        Assert.True(GpuFormatInfo.IsDepthStencilAttachment(GpuFormat.Depth24PlusStencil8));
        Assert.False(GpuFormatInfo.IsSampledUpload(GpuFormat.Depth24PlusStencil8));
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuFormatInfo.BytesPerPixel(GpuFormat.Depth24PlusStencil8));
    }

    [Fact]
    public void Variant_key_excludes_dynamic_stencil_reference()
    {
        var layout = new GpuAttachmentLayout(GpuFormat.Rgba8Unorm, GpuFormat.Depth24PlusStencil8);
        var state = GpuDepthStencilState.Default with { StencilTest = true };
        var first = new GpuGraphicsPipelineVariantKey(layout, GpuPrimitiveTopology.TriangleList,
            GpuRasterizerState.Default, state.Normalize(), GpuBlendState.None);
        var second = new GpuGraphicsPipelineVariantKey(layout, GpuPrimitiveTopology.TriangleList,
            GpuRasterizerState.Default, state.Normalize(), GpuBlendState.None);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Legacy_descriptor_normalizes_into_separate_state_blocks()
    {
#pragma warning disable CS0618
        GpuRasterDesc legacy = GpuRasterDesc.Default(GpuFormat.Bgra8Unorm);
#pragma warning restore CS0618
        legacy.Topology = GpuPrimitiveTopology.TriangleStrip;
        legacy.DepthWrite = true;
        legacy.CullMode = GpuCullMode.Back;
        legacy.Blend = GpuBlendMode.AlphaBlend;
        var value = legacy.Normalize();
        Assert.Equal(GpuPrimitiveTopology.TriangleStrip, value.Pipeline.Topology);
        Assert.Equal(GpuFormat.D32Float, value.Pipeline.Attachments.DepthStencilFormat);
        Assert.False(value.DepthStencil.DepthTest);
        Assert.True(value.DepthStencil.DepthWrite);
        Assert.Equal(GpuCullMode.Back, value.Rasterizer.CullMode);
        Assert.Equal(GpuBlendMode.AlphaBlend, value.Blend.Mode);
    }
}
