using System.Runtime.InteropServices;
using Luxel.Abstraction;
using Luxel.Vulkan.Interop;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Luxel.Vulkan;

/// <summary>Vulkan swapchain that copies a mapped RGBA8 buffer into the acquired image and presents it.</summary>
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
    private readonly uint _family;
    private SurfaceKHR _surface;
    private SwapchainKHR _swapchain;
    private Image[] _images = [];
    private Format _format;
    private readonly ColorSpaceKHR _colorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr;
    private Extent2D _extent;
    private uint _requestedWidth;
    private uint _requestedHeight;

    private CommandPool _pool;
    private CommandBuffer _cmd;
    private Silk.NET.Vulkan.Semaphore _acquire;
    private Silk.NET.Vulkan.Semaphore _render;
    private Fence _fence;
    private bool _disposed;

    private VulkanSurface(
        Vk vk, Instance instance, PhysicalDevice phys, Device device, Queue queue, uint family,
        SurfaceKHR surface, uint width, uint height)
    {
        _vk = vk;
        _instance = instance;
        _phys = phys;
        _device = device;
        _queue = queue;
        _family = family;
        _surface = surface;
        _requestedWidth = width;
        _requestedHeight = height;

        if (!_vk.TryGetInstanceExtension(_instance, out _khrSurface))
            throw new VulkanException("Failed to load VK_KHR_surface.");
        if (!_vk.TryGetDeviceExtension(_instance, _device, out _khrSwap))
        {
            _khrSurface.DestroySurface(_instance, _surface, null);
            _surface = default;
            _khrSurface.Dispose();
            throw new VulkanException("Failed to load VK_KHR_swapchain.");
        }

        try
        {
            CreateSynchronizationResources();
            CreateSwapchain();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public static VulkanSurface FromExisting(
        Vk vk, Instance instance, PhysicalDevice phys, Device device, Queue queue, uint family,
        SurfaceKHR surface, uint width, uint height)
    {
        if (surface.Handle == 0) throw new ArgumentException("A non-zero VkSurfaceKHR is required.", nameof(surface));
        return new VulkanSurface(vk, instance, phys, device, queue, family, surface, width, height);
    }

    public static VulkanSurface FromWin32(
        Vk vk, Instance instance, PhysicalDevice phys, Device device, Queue queue, uint family,
        nint hwnd, uint width, uint height)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Win32 Vulkan surfaces are only available on Windows.");
        if (hwnd == 0) throw new ArgumentException("A non-zero HWND is required.", nameof(hwnd));
        if (!vk.TryGetInstanceExtension(instance, out KhrWin32Surface khrWin32))
            throw new VulkanException("Failed to load VK_KHR_win32_surface.");
        if (!vk.TryGetInstanceExtension(instance, out KhrSurface khrSurface))
        {
            khrWin32.Dispose();
            throw new VulkanException("Failed to load VK_KHR_surface.");
        }

        SurfaceKHR surface = default;
        try
        {
            var createInfo = new Win32SurfaceCreateInfoKHR
            {
                SType = StructureType.Win32SurfaceCreateInfoKhr,
                Hwnd = hwnd,
                Hinstance = GetModuleHandleW(0),
            };
            VkCheck.Ok(khrWin32.CreateWin32Surface(instance, in createInfo, null, out surface), "vkCreateWin32SurfaceKHR");
            VulkanSurface result = new(vk, instance, phys, device, queue, family, surface, width, height);
            surface = default;
            return result;
        }
        finally
        {
            if (surface.Handle != 0) khrSurface.DestroySurface(instance, surface, null);
            khrSurface.Dispose();
            khrWin32.Dispose();
        }
    }

    private void CreateSynchronizationResources()
    {
        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _family,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };
        VkCheck.Ok(_vk.CreateCommandPool(_device, in poolInfo, null, out _pool), "vkCreateCommandPool(surface)");
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _pool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        VkCheck.Ok(_vk.AllocateCommandBuffers(_device, in allocateInfo, out _cmd), "vkAllocateCommandBuffers(surface)");

        var semaphoreInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
        VkCheck.Ok(_vk.CreateSemaphore(_device, in semaphoreInfo, null, out _acquire), "vkCreateSemaphore(acquire)");
        VkCheck.Ok(_vk.CreateSemaphore(_device, in semaphoreInfo, null, out _render), "vkCreateSemaphore(render)");
        var fenceInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo, Flags = FenceCreateFlags.SignaledBit };
        VkCheck.Ok(_vk.CreateFence(_device, in fenceInfo, null, out _fence), "vkCreateFence(surface)");
    }

    private void CreateSwapchain()
    {
        _extent = default;
        _images = [];
        if (_requestedWidth == 0 || _requestedHeight == 0) return;

        VkCheck.Ok(_khrSurface.GetPhysicalDeviceSurfaceCapabilities(_phys, _surface, out SurfaceCapabilitiesKHR caps),
            "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
        EnsureSurfaceConfigurationSupported(caps);

        _extent = caps.CurrentExtent.Width != uint.MaxValue
            ? caps.CurrentExtent
            : new Extent2D(
                Math.Clamp(_requestedWidth, caps.MinImageExtent.Width, caps.MaxImageExtent.Width),
                Math.Clamp(_requestedHeight, caps.MinImageExtent.Height, caps.MaxImageExtent.Height));
        if (_extent.Width == 0 || _extent.Height == 0) return;

        uint imageCount = caps.MinImageCount + 1;
        if (caps.MaxImageCount > 0) imageCount = Math.Min(imageCount, caps.MaxImageCount);
        CompositeAlphaFlagsKHR compositeAlpha = ChooseCompositeAlpha(caps.SupportedCompositeAlpha);

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = _surface,
            MinImageCount = imageCount,
            ImageFormat = _format,
            ImageColorSpace = _colorSpace,
            ImageExtent = _extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.TransferDstBit,
            ImageSharingMode = SharingMode.Exclusive,
            PreTransform = caps.CurrentTransform,
            CompositeAlpha = compositeAlpha,
            PresentMode = PresentModeKHR.FifoKhr,
            Clipped = true,
        };
        VkCheck.Ok(_khrSwap.CreateSwapchain(_device, in createInfo, null, out _swapchain), "vkCreateSwapchainKHR");

        uint count = 0;
        VkCheck.Ok(_khrSwap.GetSwapchainImages(_device, _swapchain, ref count, null), "vkGetSwapchainImagesKHR(count)");
        if (count == 0) throw new VulkanException("vkGetSwapchainImagesKHR returned no images.");
        _images = new Image[count];
        fixed (Image* images = _images)
            VkCheck.Ok(_khrSwap.GetSwapchainImages(_device, _swapchain, ref count, images), "vkGetSwapchainImagesKHR(images)");
        if (count != _images.Length) Array.Resize(ref _images, checked((int)count));
    }

    private void EnsureSurfaceConfigurationSupported(SurfaceCapabilitiesKHR caps)
    {
        uint formatCount = 0;
        VkCheck.Ok(_khrSurface.GetPhysicalDeviceSurfaceFormats(_phys, _surface, ref formatCount, null),
            "vkGetPhysicalDeviceSurfaceFormatsKHR(count)");
        var formats = new SurfaceFormatKHR[formatCount];
        fixed (SurfaceFormatKHR* values = formats)
            VkCheck.Ok(_khrSurface.GetPhysicalDeviceSurfaceFormats(_phys, _surface, ref formatCount, values),
                "vkGetPhysicalDeviceSurfaceFormatsKHR(formats)");
        SurfaceFormatKHR? selected = formats
            .Where(format => format.ColorSpace == _colorSpace &&
                             format.Format is Format.R8G8B8A8Unorm or Format.B8G8R8A8Unorm)
            .OrderBy(format => format.Format == Format.R8G8B8A8Unorm ? 0 : 1)
            .Select(format => (SurfaceFormatKHR?)format)
            .FirstOrDefault();
        if (selected is null)
            throw new VulkanException(
                "The window surface does not support an RGBA8 UNORM swapchain format with SRGB_NONLINEAR color space.");
        _format = selected.Value.Format;

        uint presentModeCount = 0;
        VkCheck.Ok(_khrSurface.GetPhysicalDeviceSurfacePresentModes(_phys, _surface, ref presentModeCount, null),
            "vkGetPhysicalDeviceSurfacePresentModesKHR(count)");
        var presentModes = new PresentModeKHR[presentModeCount];
        fixed (PresentModeKHR* values = presentModes)
            VkCheck.Ok(_khrSurface.GetPhysicalDeviceSurfacePresentModes(_phys, _surface, ref presentModeCount, values),
                "vkGetPhysicalDeviceSurfacePresentModesKHR(modes)");
        if (!presentModes.Contains(PresentModeKHR.FifoKhr))
            throw new VulkanException("The window surface does not support the required FIFO present mode.");

        if ((caps.SupportedUsageFlags & ImageUsageFlags.TransferDstBit) == 0)
            throw new VulkanException("The window surface swapchain images do not support TransferDst usage.");
        if ((caps.SupportedTransforms & caps.CurrentTransform) == 0)
            throw new VulkanException($"The window surface current transform {caps.CurrentTransform} is not supported.");
        _ = ChooseCompositeAlpha(caps.SupportedCompositeAlpha);
    }

    private static CompositeAlphaFlagsKHR ChooseCompositeAlpha(CompositeAlphaFlagsKHR supported)
    {
        CompositeAlphaFlagsKHR[] preferences =
        [
            CompositeAlphaFlagsKHR.OpaqueBitKhr,
            CompositeAlphaFlagsKHR.PreMultipliedBitKhr,
            CompositeAlphaFlagsKHR.PostMultipliedBitKhr,
            CompositeAlphaFlagsKHR.InheritBitKhr,
        ];
        foreach (CompositeAlphaFlagsKHR value in preferences)
        {
            if ((supported & value) != 0) return value;
        }
        throw new VulkanException("The window surface reports no supported composite alpha mode.");
    }

    public void Present(IGpuBackendBuffer source, uint srcStridePixels, uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_swapchain.Handle == 0 || width == 0 || height == 0) return;
        if (source is not VulkanBuffer vulkanBuffer)
            throw new ArgumentException("The presentation source must be a Vulkan buffer from this backend.", nameof(source));
        if (srcStridePixels < width) throw new ArgumentOutOfRangeException(nameof(srcStridePixels));
        ulong requiredBytes = checked((ulong)srcStridePixels * height * 4UL);
        if (source.Size < requiredBytes)
            throw new ArgumentException("The presentation source buffer is smaller than the requested RGBA image.", nameof(source));

        VkCheck.Ok(_vk.WaitForFences(_device, 1, in _fence, true, ulong.MaxValue), "vkWaitForFences(surface)");
        uint imageIndex = 0;
        Result acquire = _khrSwap.AcquireNextImage(_device, _swapchain, ulong.MaxValue, _acquire, default, ref imageIndex);
        if (acquire == Result.ErrorOutOfDateKhr)
        {
            RecreateSwapchain();
            return;
        }
        bool recreateAfterPresent = acquire == Result.SuboptimalKhr;
        if (acquire is not (Result.Success or Result.SuboptimalKhr))
            VkCheck.Ok(acquire, "vkAcquireNextImageKHR");
        if (imageIndex >= _images.Length) throw new VulkanException("vkAcquireNextImageKHR returned an invalid image index.");

        VkCheck.Ok(_vk.ResetFences(_device, 1, in _fence), "vkResetFences(surface)");
        VkCheck.Ok(_vk.ResetCommandBuffer(_cmd, 0), "vkResetCommandBuffer(surface)");
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        VkCheck.Ok(_vk.BeginCommandBuffer(_cmd, in beginInfo), "vkBeginCommandBuffer(surface)");

        Barrier(_images[imageIndex], ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
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
        _vk.CmdCopyBufferToImage(_cmd, vulkanBuffer.Handle, _images[imageIndex], ImageLayout.TransferDstOptimal, 1, in region);
        Barrier(_images[imageIndex], ImageLayout.TransferDstOptimal, ImageLayout.PresentSrcKhr,
            PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit, PipelineStageFlags2.BottomOfPipeBit, 0);
        VkCheck.Ok(_vk.EndCommandBuffer(_cmd), "vkEndCommandBuffer(surface)");

        CommandBuffer commandBuffer = _cmd;
        Silk.NET.Vulkan.Semaphore waitSemaphore = _acquire;
        Silk.NET.Vulkan.Semaphore signalSemaphore = _render;
        PipelineStageFlags waitStage = PipelineStageFlags.TransferBit;
        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &waitSemaphore,
            PWaitDstStageMask = &waitStage,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
            SignalSemaphoreCount = 1,
            PSignalSemaphores = &signalSemaphore,
        };
        VkCheck.Ok(_vk.QueueSubmit(_queue, 1, in submitInfo, _fence), "vkQueueSubmit(surface)");

        SwapchainKHR swapchain = _swapchain;
        uint presentedImage = imageIndex;
        var presentInfo = new PresentInfoKHR
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &signalSemaphore,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &presentedImage,
        };
        Result present = _khrSwap.QueuePresent(_queue, in presentInfo);
        if (present is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr)
            recreateAfterPresent = true;
        else
            VkCheck.Ok(present, "vkQueuePresentKHR");
        if (recreateAfterPresent) RecreateSwapchain();
    }

    public void Resize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _requestedWidth = width;
        _requestedHeight = height;
        RecreateSwapchain();
    }

    private void RecreateSwapchain()
    {
        VkCheck.Ok(_vk.DeviceWaitIdle(_device), "vkDeviceWaitIdle(surface resize)");
        if (_swapchain.Handle != 0)
        {
            _khrSwap.DestroySwapchain(_device, _swapchain, null);
            _swapchain = default;
        }
        CreateSwapchain();
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
        var dependency = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier,
        };
        _vk.CmdPipelineBarrier2(_cmd, in dependency);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_device.Handle != 0) _vk.DeviceWaitIdle(_device);
        if (_swapchain.Handle != 0) _khrSwap.DestroySwapchain(_device, _swapchain, null);
        if (_fence.Handle != 0) _vk.DestroyFence(_device, _fence, null);
        if (_acquire.Handle != 0) _vk.DestroySemaphore(_device, _acquire, null);
        if (_render.Handle != 0) _vk.DestroySemaphore(_device, _render, null);
        if (_pool.Handle != 0) _vk.DestroyCommandPool(_device, _pool, null);
        if (_surface.Handle != 0) _khrSurface.DestroySurface(_instance, _surface, null);
        _surface = default;
        _khrSwap.Dispose();
        _khrSurface.Dispose();
    }
}
