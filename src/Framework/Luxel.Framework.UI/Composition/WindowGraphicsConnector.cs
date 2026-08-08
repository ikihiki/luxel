using Luxel.Graphics.Abstraction;
using Luxel.Graphics.DirectX12;
using Luxel.Graphics.Vulkan;
using Luxel.Graphics.WebGPU;
using Luxel.Platform.Silk;
using Luxel.Platform.Windows;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;

namespace Luxel.Framework.UI;

internal static unsafe class WindowGraphicsConnector
{
    public static VulkanPresentationSource CreateVulkanPresentationSource(Window window)
    {
        SilkWindow silkWindow = window.RequireBackendWindow<SilkWindow>();
        IVkSurface vkSurface = silkWindow.NativeWindow.VkSurface
            ?? throw new PlatformNotSupportedException("Silk.NET did not expose Vulkan surface integration for this window.");
        byte** extensionPointers = vkSurface.GetRequiredExtensions(out uint extensionCount);
        if (extensionPointers is null || extensionCount == 0)
            throw new PlatformNotSupportedException("The window implementation did not report required Vulkan instance extensions.");
        var extensions = new string[extensionCount];
        for (uint i = 0; i < extensionCount; i++)
            extensions[i] = SilkMarshal.PtrToString((nint)extensionPointers[i])
                ?? throw new PlatformNotSupportedException("The window implementation returned an invalid Vulkan extension name.");

        return new VulkanPresentationSource(extensions, instanceHandle =>
        {
            ulong surface = vkSurface.Create<byte>(new VkHandle(instanceHandle), null).Handle;
            if (surface == 0) throw new InvalidOperationException("Silk.NET returned a null VkSurfaceKHR.");
            return surface;
        });
    }

    public static Func<Window, GpuSurface> CreateSurfaceFactory(IGpuBackend backend)
        => window =>
        {
            uint width = (uint)Math.Max(1, window.Width);
            uint height = (uint)Math.Max(1, window.Height);
            return backend switch
            {
                D3D12Backend d3d12 => d3d12.CreateSurface(
                    window.RequireBackendWindow<Win32Window>().Handle, width, height),
                VulkanBackend vulkan when window.BackendWindow is SilkWindow => vulkan.CreateSurface(width, height),
                VulkanBackend vulkan => vulkan.CreateWin32Surface(
                    window.RequireBackendWindow<Win32Window>().Handle, width, height),
                WebGpuBackend webGpu when window.BackendWindow is SilkWindow silk =>
                    webGpu.CreateXlibSurface(silk.X11Display, silk.X11Window, width, height),
                WebGpuBackend webGpu => CreateWin32WebGpuSurface(webGpu, window, width, height),
                _ => throw new PlatformNotSupportedException(
                    $"No built-in presentation connection exists for {window.BackendWindow.GetType().FullName} and {backend.GetType().FullName}.")
            };
        };

    private static GpuSurface CreateWin32WebGpuSurface(WebGpuBackend backend, Window window, uint width, uint height)
    {
        Win32Window win32 = window.RequireBackendWindow<Win32Window>();
        return backend.CreateWin32Surface(win32.HInstance, win32.Handle, width, height);
    }
}
