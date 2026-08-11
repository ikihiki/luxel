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

    [Fact]
    public void Logical_pipeline_binding_preserves_independent_command_state()
    {
        var backendPipeline = new RecordingPipeline();
        using var pipeline = new GpuPipeline(backendPipeline);
        var backendCommand = new RecordingCommandBuffer();
        using var command = new GpuCommandBuffer(backendCommand);

        command.SetGraphicsPipeline(pipeline);

        Assert.Equal(1, backendCommand.PipelineBindings);
        Assert.Equal(0, backendCommand.RasterizerStateChanges);
        Assert.Equal(0, backendCommand.DepthStencilStateChanges);
        Assert.Equal(0, backendCommand.BlendStateChanges);
    }

    [Fact]
    public void Legacy_pipeline_binding_applies_legacy_state_bundle()
    {
        var rasterizer = new GpuRasterizerState(GpuCullMode.Back, GpuFrontFace.Clockwise);
        var depthStencil = GpuDepthStencilState.Default with { DepthTest = true, DepthWrite = true };
        var blend = GpuBlendState.AlphaBlend;
        using var pipeline = new GpuPipeline(new RecordingPipeline())
        {
            LegacyGraphicsState = new GpuLegacyGraphicsState(rasterizer, depthStencil, blend),
        };
        var backendCommand = new RecordingCommandBuffer();
        using var command = new GpuCommandBuffer(backendCommand);

        command.SetGraphicsPipeline(pipeline);

        Assert.Equal(rasterizer, backendCommand.Rasterizer);
        Assert.Equal(depthStencil, backendCommand.DepthStencil);
        Assert.Equal(blend, backendCommand.Blend);
    }

    [Fact]
    public void Depth_stencil_state_requirements_are_validated_against_attachment_layout()
    {
        var colorOnly = new GpuAttachmentLayout(GpuFormat.Rgba8Unorm);
        var depthOnly = new GpuAttachmentLayout(GpuFormat.Rgba8Unorm, GpuFormat.D32Float);
        var depthState = GpuDepthStencilState.Default with { DepthTest = true };
        var writeState = GpuDepthStencilState.Default with { DepthWrite = true };
        var stencilState = GpuDepthStencilState.Default with { StencilTest = true };

        Assert.Throws<InvalidOperationException>(() =>
            GpuGraphicsStateValidation.ValidateDepthStencilAttachmentRequirements(colorOnly, depthState));
        Assert.Throws<InvalidOperationException>(() =>
            GpuGraphicsStateValidation.ValidateDepthStencilAttachmentRequirements(colorOnly, writeState));
        Assert.Throws<InvalidOperationException>(() =>
            GpuGraphicsStateValidation.ValidateDepthStencilAttachmentRequirements(depthOnly, stencilState));

        GpuGraphicsStateValidation.ValidateDepthStencilAttachmentRequirements(depthOnly, depthState);
        GpuGraphicsStateValidation.ValidateDepthStencilAttachmentRequirements(
            new GpuAttachmentLayout(GpuFormat.Rgba8Unorm, GpuFormat.Depth24PlusStencil8), stencilState);
    }

    private sealed class RecordingPipeline : Luxel.Graphics.Abstraction.IGpuBackendPipeline
    {
        public bool IsCompute => false;
        public GpuGraphicsPipelineDesc? GraphicsDescription => new(new GpuAttachmentLayout(GpuFormat.Rgba8Unorm));
        public GpuPipelineDiagnostics Diagnostics => default;
        public Luxel.Graphics.Abstraction.IGpuBackendPipeline ResolveGraphicsVariant(
            GpuRasterizerState rasterizer, GpuDepthStencilState depthStencil, GpuBlendState blend) => this;
        public void Dispose() { }
    }

    private sealed class RecordingCommandBuffer : Luxel.Graphics.Abstraction.IGpuBackendCommandBuffer
    {
        public int PipelineBindings { get; private set; }
        public int RasterizerStateChanges { get; private set; }
        public int DepthStencilStateChanges { get; private set; }
        public int BlendStateChanges { get; private set; }
        public GpuRasterizerState Rasterizer { get; private set; }
        public GpuDepthStencilState DepthStencil { get; private set; }
        public GpuBlendState Blend { get; private set; }

        public void SetGraphicsPipeline(Luxel.Graphics.Abstraction.IGpuBackendPipeline pipeline) => PipelineBindings++;
        public void SetRasterizerState(GpuRasterizerState state) { Rasterizer = state; RasterizerStateChanges++; }
        public void SetDepthStencilState(GpuDepthStencilState state) { DepthStencil = state; DepthStencilStateChanges++; }
        public void SetBlendState(GpuBlendState state) { Blend = state; BlendStateChanges++; }
        public void SetComputePipeline(Luxel.Graphics.Abstraction.IGpuBackendPipeline pipeline) { }
        public void SetRootConstants(ReadOnlySpan<byte> data) { }
        public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ) { }
        public void SetStencilReference(uint reference) { }
        public void SetViewport(GpuViewport viewport) { }
        public void SetScissor(GpuScissorRect scissor) { }
        public void BeginRendering(Luxel.Graphics.Abstraction.IGpuBackendTexture color,
            Luxel.Graphics.Abstraction.IGpuBackendTexture? depth, float r, float g, float b, float a,
            float clearDepth, uint clearStencil) { }
        public void EndRendering() { }
        public void Draw(uint vertexCount, uint instanceCount) { }
        public void CopyTextureToBuffer(Luxel.Graphics.Abstraction.IGpuBackendTexture source,
            Luxel.Graphics.Abstraction.IGpuBackendBuffer destination, uint rowLengthPixels) { }
        public void CopyBufferToBuffer(Luxel.Graphics.Abstraction.IGpuBackendBuffer source,
            Luxel.Graphics.Abstraction.IGpuBackendBuffer destination, ulong bytes) { }
        public void Barrier(GpuStage source, GpuStage destination, GpuHazard hazard) { }
        public void Finish() { }
        public void Dispose() { }
    }
}
