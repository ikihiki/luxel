namespace Luxel.Graphics;

/// <summary>
/// Portable bridge used by a window backend to provide Vulkan instance extensions and create its native surface.
/// Implementations must not expose Vulkan binding types so Luxel core remains independent of a Vulkan package.
/// </summary>
public interface IVulkanWindowSurface
{
    /// <summary>Vulkan instance extension names required by this window system.</summary>
    IReadOnlyList<string> RequiredInstanceExtensions { get; }

    /// <summary>Creates a raw <c>VkSurfaceKHR</c> for the supplied raw <c>VkInstance</c>.</summary>
    /// <returns>The non-zero raw <c>VkSurfaceKHR</c> handle.</returns>
    ulong CreateSurface(nint instanceHandle);
}
