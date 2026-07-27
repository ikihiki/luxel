using Luxel.Graphics.Abstraction;
using Luxel.Graphics.Vulkan.Interop;
using Silk.NET.Vulkan;

namespace Luxel.Graphics.Vulkan;

internal sealed unsafe class VulkanQueue : IGpuBackendQueue
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly uint _familyIndex;
    private readonly PipelineLayout _layout;
    private readonly DescriptorSet _descriptorSet;
    private readonly object _queueLock;   // OneShotSubmit と共有 (同一 VkQueue の外部同期)

    public VulkanQueue(Vk vk, Device device, Queue queue, uint familyIndex,
                       PipelineLayout layout, DescriptorSet descriptorSet, object queueLock)
    {
        _vk = vk;
        _device = device;
        _queue = queue;
        _familyIndex = familyIndex;
        _layout = layout;
        _descriptorSet = descriptorSet;
        _queueLock = queueLock;
    }

    public IGpuBackendCommandBuffer StartCommandRecording()
        => new VulkanCommandBuffer(_vk, _device, _familyIndex, _layout, _descriptorSet);

    public void Submit(IGpuBackendCommandBuffer commandBuffer)
    {
        var vcb = (VulkanCommandBuffer)commandBuffer;
        CommandBuffer handle = vcb.Handle;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &handle,
        };
        lock (_queueLock)
            VkCheck.Ok(_vk.QueueSubmit(_queue, 1, in submit, default), "vkQueueSubmit");
    }

    public void WaitIdle()
    {
        lock (_queueLock) VkCheck.Ok(_vk.QueueWaitIdle(_queue), "vkQueueWaitIdle");
    }
}
