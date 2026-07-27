namespace Luxel.Graphics.Vulkan;

/// <summary>Vulkan バックエンド初期化時の presentation 構成。</summary>
public enum VulkanPresentationMode
{
    /// <summary>Windows では Win32 presentation、それ以外では headless を選ぶ。</summary>
    Auto,

    /// <summary>WSI / swapchain extensions を読み込まない offscreen 専用モード。</summary>
    Disabled,

    /// <summary>Win32 HWND への presentation を有効にする。</summary>
    Win32,

    /// <summary><see cref="IVulkanWindowSurface"/> supplied by a portable window backend.</summary>
    Window,
}

/// <summary>Vulkan バックエンドの初期化 options。</summary>
public sealed record VulkanBackendOptions
{
    public bool EnableValidation { get; init; } = true;
    public VulkanPresentationMode Presentation { get; init; } = VulkanPresentationMode.Auto;

    /// <summary>Window-system surface provider. Required when <see cref="Presentation"/> is <see cref="VulkanPresentationMode.Window"/>.</summary>
    public IVulkanWindowSurface? WindowSurface { get; init; }
}
