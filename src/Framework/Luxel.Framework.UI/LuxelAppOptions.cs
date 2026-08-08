using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Framework.UI;

/// <summary>Window-system choices supported by the application host.</summary>
public enum LuxelWindowBackend
{
    /// <summary>Select Win32 on Windows and Silk.NET X11 on Linux.</summary>
    Auto,
    Win32,
    SilkX11,
}

/// <summary>GPU backend choices supported by the application host.</summary>
public enum LuxelGraphicsBackend
{
    /// <summary>Select Direct3D 12 on Windows and Vulkan on Linux.</summary>
    Auto,
    Vulkan,
    Direct3D12,
    /// <summary>Explicit opt-in to the native WebGPU backend. Auto never selects this.</summary>
    WebGpu,
}

/// <summary>Configuration for the environment-aware Luxel UI application host.</summary>
public sealed record LuxelAppOptions
{
    public string Title { get; set; } = "Luxel";
    public string UiName { get; set; } = "app";
    public int Width { get; set; } = 960;
    public int Height { get; set; } = 640;
    public LuxelWindowBackend WindowBackend { get; set; } = LuxelWindowBackend.Auto;
    public LuxelGraphicsBackend GraphicsBackend { get; set; } = LuxelGraphicsBackend.Auto;
    public bool EnableValidation { get; set; } = true;
    public Func<VectorFont>? FontFactory { get; set; }
    public Theme? Theme { get; set; }
    public int? RunFrames { get; set; }
    public TimeSpan? RunDuration { get; set; }
    public Action<string>? Diagnostic { get; set; }
}
