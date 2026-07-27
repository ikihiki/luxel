using Luxel.Graphics.Abstraction;
using Luxel.Graphics.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace Luxel.Graphics.Vulkan;

internal sealed unsafe class VulkanCommandBuffer : IGpuBackendCommandBuffer
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PipelineLayout _layout;
    private readonly DescriptorSet _descriptorSet;
    private CommandPool _pool;
    private CommandBuffer _cb;
    private bool _disposed;

    public VulkanCommandBuffer(Vk vk, Device device, uint queueFamilyIndex, PipelineLayout layout,
                               DescriptorSet descriptorSet)
    {
        _vk = vk;
        _device = device;
        _layout = layout;
        _descriptorSet = descriptorSet;

        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = queueFamilyIndex,
            Flags = CommandPoolCreateFlags.TransientBit,
        };
        VkCheck.Ok(_vk.CreateCommandPool(_device, in poolInfo, null, out _pool), "vkCreateCommandPool");

        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        VkCheck.Ok(_vk.AllocateCommandBuffers(_device, in allocInfo, out _cb), "vkAllocateCommandBuffers");

        var begin = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VkCheck.Ok(_vk.BeginCommandBuffer(_cb, in begin), "vkBeginCommandBuffer");

        // 固定 bindless ディスクリプタセットを compute/graphics 両方の bind point に束縛。
        var set = _descriptorSet;
        _vk.CmdBindDescriptorSets(_cb, PipelineBindPoint.Compute, _layout, 0, 1, in set, 0, null);
        _vk.CmdBindDescriptorSets(_cb, PipelineBindPoint.Graphics, _layout, 0, 1, in set, 0, null);
    }

    internal CommandBuffer Handle => _cb;

    public void SetComputePipeline(IGpuBackendPipeline pipeline)
    {
        var vp = (VulkanPipeline)pipeline;
        _vk.CmdBindPipeline(_cb, PipelineBindPoint.Compute, vp.Handle);
    }

    public void SetRootConstants(ReadOnlySpan<byte> data)
    {
        fixed (byte* p = data)
            _vk.CmdPushConstants(_cb, _layout, ShaderStageFlags.All, 0, (uint)data.Length, p);
    }

    public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        _vk.CmdDispatch(_cb, groupCountX, groupCountY, groupCountZ);
    }

    public void SetGraphicsPipeline(IGpuBackendPipeline pipeline)
    {
        var vp = (VulkanPipeline)pipeline;
        _vk.CmdBindPipeline(_cb, PipelineBindPoint.Graphics, vp.Handle);
    }

    public void BeginRendering(IGpuBackendTexture color, IGpuBackendTexture? depth,
        float r, float g, float b, float a, float clearDepth)
    {
        var tex = (VulkanTexture)color;
        TransitionImage(tex, ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlags2.TopOfPipeBit, 0,
            PipelineStageFlags2.ColorAttachmentOutputBit, AccessFlags2.ColorAttachmentWriteBit);

        var clear = new ClearValue { Color = new ClearColorValue(r, g, b, a) };
        var attachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = tex.View,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            ClearValue = clear,
        };

        var depthAttachment = new RenderingAttachmentInfo { SType = StructureType.RenderingAttachmentInfo };
        bool hasDepth = depth is not null;
        if (hasDepth)
        {
            var dtex = (VulkanTexture)depth!;
            TransitionImage(dtex, ImageLayout.DepthAttachmentOptimal,
                PipelineStageFlags2.TopOfPipeBit, 0,
                PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit,
                AccessFlags2.DepthStencilAttachmentWriteBit);
            depthAttachment.ImageView = dtex.View;
            depthAttachment.ImageLayout = ImageLayout.DepthAttachmentOptimal;
            depthAttachment.LoadOp = AttachmentLoadOp.Clear;
            depthAttachment.StoreOp = AttachmentStoreOp.Store;
            depthAttachment.ClearValue = new ClearValue { DepthStencil = new ClearDepthStencilValue(clearDepth, 0) };
        }

        var renderingInfo = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(tex.Width, tex.Height)),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &attachment,
            PDepthAttachment = hasDepth ? &depthAttachment : null,
        };
        _vk.CmdBeginRendering(_cb, in renderingInfo);

        // ネガティブ高さビューポートで Y を反転し、D3D12 と同じ座標系に揃える。
        var viewport = new Viewport(0, tex.Height, tex.Width, -(float)tex.Height, 0, 1);
        _vk.CmdSetViewport(_cb, 0, 1, in viewport);
        var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(tex.Width, tex.Height));
        _vk.CmdSetScissor(_cb, 0, 1, in scissor);
    }

    public void EndRendering() => _vk.CmdEndRendering(_cb);

    public void Draw(uint vertexCount, uint instanceCount)
        => _vk.CmdDraw(_cb, vertexCount, instanceCount, 0, 0);

    public void CopyTextureToBuffer(IGpuBackendTexture source, IGpuBackendBuffer destination, uint rowLengthPixels)
    {
        var tex = (VulkanTexture)source;
        var buf = (VulkanBuffer)destination;
        TransitionImage(tex, ImageLayout.TransferSrcOptimal,
            PipelineStageFlags2.ColorAttachmentOutputBit, AccessFlags2.ColorAttachmentWriteBit,
            PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferReadBit);

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = rowLengthPixels,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(tex.Width, tex.Height, 1),
        };
        _vk.CmdCopyImageToBuffer(_cb, tex.Image, ImageLayout.TransferSrcOptimal, buf.Handle, 1, in region);
    }

    public void CopyBufferToBuffer(IGpuBackendBuffer source, IGpuBackendBuffer destination, ulong bytes)
    {
        var src = (VulkanBuffer)source;
        var dst = (VulkanBuffer)destination;
        var region = new BufferCopy(0, 0, bytes);
        _vk.CmdCopyBuffer(_cb, src.Handle, dst.Handle, 1, in region);
    }

    private void TransitionImage(VulkanTexture tex, ImageLayout newLayout,
        PipelineStageFlags2 srcStage, AccessFlags2 srcAccess,
        PipelineStageFlags2 dstStage, AccessFlags2 dstAccess)
    {
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = srcStage,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStage,
            DstAccessMask = dstAccess,
            OldLayout = tex.CurrentLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = tex.Image,
            SubresourceRange = new ImageSubresourceRange(tex.Aspect, 0, 1, 0, 1),
        };
        var dep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier,
        };
        _vk.CmdPipelineBarrier2(_cb, in dep);
        tex.CurrentLayout = newLayout;
    }

    public void Barrier(GpuStage source, GpuStage destination, GpuHazard hazard)
    {
        AccessFlags2 srcAccess = AccessFlags2.MemoryReadBit | AccessFlags2.MemoryWriteBit;
        AccessFlags2 dstAccess = AccessFlags2.MemoryReadBit | AccessFlags2.MemoryWriteBit;
        if ((hazard & GpuHazard.IndirectArguments) != 0)
            dstAccess |= AccessFlags2.IndirectCommandReadBit;

        var memBarrier = new MemoryBarrier2
        {
            SType = StructureType.MemoryBarrier2,
            SrcStageMask = MapStage(source),
            SrcAccessMask = srcAccess,
            DstStageMask = MapStage(destination),
            DstAccessMask = dstAccess,
        };
        var dep = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            MemoryBarrierCount = 1,
            PMemoryBarriers = &memBarrier,
        };
        _vk.CmdPipelineBarrier2(_cb, in dep);
    }

    public void Finish()
    {
        VkCheck.Ok(_vk.EndCommandBuffer(_cb), "vkEndCommandBuffer");
    }

    private static PipelineStageFlags2 MapStage(GpuStage stage)
    {
        if (stage == GpuStage.None) return PipelineStageFlags2.None;
        PipelineStageFlags2 f = 0;
        if ((stage & GpuStage.DrawIndirect) != 0) f |= PipelineStageFlags2.DrawIndirectBit;
        if ((stage & GpuStage.VertexShader) != 0) f |= PipelineStageFlags2.VertexShaderBit;
        if ((stage & GpuStage.PixelShader) != 0) f |= PipelineStageFlags2.FragmentShaderBit;
        if ((stage & GpuStage.ComputeShader) != 0) f |= PipelineStageFlags2.ComputeShaderBit;
        if ((stage & GpuStage.ColorOutput) != 0) f |= PipelineStageFlags2.ColorAttachmentOutputBit;
        if ((stage & GpuStage.DepthStencil) != 0)
            f |= PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit;
        if ((stage & GpuStage.Copy) != 0) f |= PipelineStageFlags2.AllTransferBit;
        if ((stage & GpuStage.AllGraphics) != 0) f |= PipelineStageFlags2.AllGraphicsBit;
        if ((stage & GpuStage.All) != 0) f |= PipelineStageFlags2.AllCommandsBit;
        return f;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vk.DestroyCommandPool(_device, _pool, null);
    }
}
