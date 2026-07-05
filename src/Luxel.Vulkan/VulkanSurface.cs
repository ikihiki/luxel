using System.Runtime.InteropServices;
using Luxel.Abstraction;
using Luxel.Vulkan.Interop;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Luxel.Vulkan;

/// <summary>
/// Win32 サーフェス + スワップチェーン。RGBA8 バッファを取得画像へ vkCmdCopyBufferToImage して present。
/// </summary>
internal sealed unsafe class VulkanSurface : IGpuBackendSurface
{
    [DllImport("kernel32", SetLastError = true)] private static extern nint GetModuleHandleW(nint lpModuleName);

    private readonly Vk _vk;
    private readonly Instance _instance;
    private readonly PhysicalDevice _phys;
    private readonly Device _device;
    private readonly Queue _queue;
    private readonly KhrSurface _khrSurface;
    private readonly KhrSwapchain _khrSwap;
    private SurfaceKHR _surface;
    private SwapchainKHR _swapchain;
    private Image[] _images = [];
    private readonly Format _format = Format.R8G8B8A8Unorm;   // framebuffer(RGBA8) と一致 (swizzle 不要)
    private Extent2D _extent;

    private CommandPool _pool;
    private CommandBuffer _cmd;
    private Silk.NET.Vulkan.Semaphore _acquire, _render;
    private Fence _fence;
    private bool _disposed;

    public VulkanSurface(Vk vk, Instance instance, PhysicalDevice phys, Device device, Queue queue, uint family,
        nint hwnd, uint w, uint h)
    {
        _vk = vk; _instance = instance; _phys = phys; _device = device; _queue = queue;
        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface)) throw new VulkanException("VK_KHR_surface 取得失敗");
        if (!_vk.TryGetInstanceExtension(_instance, out KhrWin32Surface khrWin32)) throw new VulkanException("VK_KHR_win32_surface 取得失敗");
        if (!_vk.TryGetDeviceExtension(_instance, _device, out _khrSwap)) throw new VulkanException("VK_KHR_swapchain 取得失敗");

        var ci = new Win32SurfaceCreateInfoKHR
        {
            SType = StructureType.Win32SurfaceCreateInfoKhr,
            Hwnd = hwnd,
            Hinstance = GetModuleHandleW(0),
        };
        VkCheck.Ok(khrWin32.CreateWin32Surface(_instance, in ci, null, out _surface), "vkCreateWin32SurfaceKHR");

        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = family,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };
        VkCheck.Ok(_vk.CreateCommandPool(_device, in poolInfo, null, out _pool), "vkCreateCommandPool(surface)");
        var ai = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        VkCheck.Ok(_vk.AllocateCommandBuffers(_device, in ai, out _cmd), "vkAllocateCommandBuffers(surface)");

        var semInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        _vk.CreateSemaphore(_device, in semInfo, null, out _acquire);
        _vk.CreateSemaphore(_device, in semInfo, null, out _render);
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
        _vk.CreateFence(_device, in fenceInfo, null, out _fence);

        CreateSwapchain(w, h);
    }

    private void CreateSwapchain(uint w, uint h)
    {
        _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_phys, _surface, out SurfaceCapabilitiesKHR caps);
        _extent = caps.CurrentExtent.Width != uint.MaxValue
            ? caps.CurrentExtent
            : new Extent2D(Math.Clamp(w, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                           Math.Clamp(h, caps.MinImageExtent.Height, caps.MaxImageExtent.Height));
        if (_extent.Width == 0 || _extent.Height == 0) return;

        uint imageCount = caps.MinImageCount + 1;
        if (caps.MaxImageCount > 0 && imageCount > caps.MaxImageCount) imageCount = caps.MaxImageCount;

        var sci = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = _format,
            ImageColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr,
            ImageExtent = _extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.TransferDstBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = caps.CurrentTransform,
            CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
            PresentMode = PresentModeKHR.FifoKhr,
            Clipped = true,
        };
        VkCheck.Ok(_khrSwap.CreateSwapchain(_device, in sci, null, out _swapchain), "vkCreateSwapchainKHR");

        uint n = 0;
        _khrSwap.GetSwapchainImages(_device, _swapchain, ref n, null);
        _images = new Image[n];
        fixed (Image* p = _images) _khrSwap.GetSwapchainImages(_device, _swapchain, ref n, p);
    }

    public void Present(IGpuBackendBuffer source, uint srcStridePixels, uint width, uint height)
    {
        if (_swapchain.Handle == 0) return;
        Silk.NET.Vulkan.Buffer buf = ((VulkanBuffer)source).Handle;

        _vk.WaitForFences(_device, 1, in _fence, true, ulong.MaxValue);
        uint idx = 0;
        Result acq = _khrSwap.AcquireNextImage(_device, _swapchain, ulong.MaxValue, _acquire, default, ref idx);
        if (acq is Result.ErrorOutOfDateKhr) { RecreateSwapchain(); return; }
        _vk.ResetFences(_device, 1, in _fence);

        _vk.ResetCommandBuffer(_cmd, 0);
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
        _vk.BeginCommandBuffer(_cmd, in begin);

        Barrier(_images[idx], ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
            PipelineStageFlags2.TopOfPipeBit, 0, PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit);
        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = srcStridePixels,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = new Extent3D(Math.Min(width, _extent.Width), Math.Min(height, _extent.Height), 1),
        };
        _vk.CmdCopyBufferToImage(_cmd, buf, _images[idx], ImageLayout.TransferDstOptimal, 1, in region);
        Barrier(_images[idx], ImageLayout.TransferDstOptimal, ImageLayout.PresentSrcKhr,
            PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit, PipelineStageFlags2.BottomOfPipeBit, 0);
        _vk.EndCommandBuffer(_cmd);

        CommandBuffer cmd = _cmd;
        Silk.NET.Vulkan.Semaphore waitSem = _acquire, sigSem = _render;
        PipelineStageFlags waitStage = PipelineStageFlags.TransferBit;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSem,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &cmd,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &sigSem,
        };
        _vk.QueueSubmit(_queue, 1, in submit, _fence);

        SwapchainKHR sc = _swapchain; uint imgIdx = idx;
        var present = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &sigSem,
            SwapchainCount = 1,
            PSwapchains = &sc,
            PImageIndices = &imgIdx,
        };
        Result pr = _khrSwap.QueuePresent(_queue, in present);
        if (pr is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr) RecreateSwapchain();
    }

    public void Resize(uint width, uint height) => RecreateSwapchain();

    private void RecreateSwapchain()
    {
        _vk.DeviceWaitIdle(_device);
        if (_swapchain.Handle != 0) { _khrSwap.DestroySwapchain(_device, _swapchain, null); _swapchain = default; }
        _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_phys, _surface, out SurfaceCapabilitiesKHR caps);
        CreateSwapchain(caps.CurrentExtent.Width, caps.CurrentExtent.Height);
    }

    private void Barrier(Image image, ImageLayout oldLayout, ImageLayout newLayout,
        PipelineStageFlags2 srcStage, AccessFlags2 srcAccess, PipelineStageFlags2 dstStage, AccessFlags2 dstAccess)
    {
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = srcStage,
            SrcAccessMask = srcAccess,
            DstStageMask = dstStage,
            DstAccessMask = dstAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1),
        };
        var dep = new DependencyInfo { SType = StructureType.DependencyInfo, ImageMemoryBarrierCount = 1, PImageMemoryBarriers = &barrier };
        _vk.CmdPipelineBarrier2(_cmd, in dep);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vk.DeviceWaitIdle(_device);
        if (_swapchain.Handle != 0) _khrSwap.DestroySwapchain(_device, _swapchain, null);
        if (_fence.Handle != 0) _vk.DestroyFence(_device, _fence, null);
        if (_acquire.Handle != 0) _vk.DestroySemaphore(_device, _acquire, null);
        if (_render.Handle != 0) _vk.DestroySemaphore(_device, _render, null);
        if (_pool.Handle != 0) _vk.DestroyCommandPool(_device, _pool, null);
        if (_surface.Handle != 0) _khrSurface.DestroySurface(_instance, _surface, null);
        _khrSwap.Dispose();
        _khrSurface.Dispose();
    }
}
