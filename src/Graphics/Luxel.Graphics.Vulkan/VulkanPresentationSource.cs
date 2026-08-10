namespace Luxel.Graphics.Vulkan;

/// <summary>
/// Vulkan-specific presentation bootstrap supplied by low-level composition code.
/// The caller is responsible for obtaining the required extensions and surface callback from the
/// selected window implementation.
/// </summary>
public sealed class VulkanPresentationSource
{
    private readonly Func<nint, ulong> _createSurface;

    public VulkanPresentationSource(
        IEnumerable<string> requiredInstanceExtensions,
        Func<nint, ulong> createSurface)
    {
        ArgumentNullException.ThrowIfNull(requiredInstanceExtensions);
        _createSurface = createSurface ?? throw new ArgumentNullException(nameof(createSurface));
        RequiredInstanceExtensions = requiredInstanceExtensions
            .Select(static extension =>
                string.IsNullOrWhiteSpace(extension)
                    ? throw new ArgumentException("Vulkan instance extension names cannot be empty.", nameof(requiredInstanceExtensions))
                    : extension)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (RequiredInstanceExtensions.Count == 0)
            throw new ArgumentException("At least one Vulkan instance extension is required.", nameof(requiredInstanceExtensions));
    }

    public IReadOnlyList<string> RequiredInstanceExtensions { get; }

    internal ulong CreateSurface(nint instanceHandle)
    {
        if (instanceHandle == 0) throw new ArgumentException("A non-zero VkInstance handle is required.", nameof(instanceHandle));
        ulong surface = _createSurface(instanceHandle);
        if (surface == 0) throw new InvalidOperationException("The Vulkan presentation source returned a null VkSurfaceKHR.");
        return surface;
    }
}
