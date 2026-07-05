using Luxel.Abstraction;
using Silk.NET.Vulkan;

namespace Luxel.Vulkan;

internal sealed unsafe class VulkanTexture : IGpuBackendTexture
{
    private readonly Vk _vk;
    private readonly Device _device;
    private Image _image;
    private DeviceMemory _memory;
    private ImageView _view;
    private bool _disposed;

    public VulkanTexture(Vk vk, Device device, Image image, DeviceMemory memory, ImageView view,
                         uint width, uint height, GpuFormat format, Format vkFormat,
                         uint bindlessIndex = 0, ImageLayout initialLayout = ImageLayout.Undefined,
                         ImageAspectFlags aspect = ImageAspectFlags.ColorBit)
    {
        _vk = vk;
        _device = device;
        _image = image;
        _memory = memory;
        _view = view;
        Width = width;
        Height = height;
        Format = format;
        VkFormat = vkFormat;
        BindlessIndex = bindlessIndex;
        CurrentLayout = initialLayout;
        Aspect = aspect;
    }

    public uint Width { get; }
    public uint Height { get; }
    public GpuFormat Format { get; }
    public uint BindlessIndex { get; }

    internal Format VkFormat { get; }
    internal Image Image => _image;
    internal ImageView View => _view;
    internal ImageLayout CurrentLayout { get; set; }
    internal ImageAspectFlags Aspect { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vk.DestroyImageView(_device, _view, null);
        _vk.DestroyImage(_device, _image, null);
        _vk.FreeMemory(_device, _memory, null);
    }
}
