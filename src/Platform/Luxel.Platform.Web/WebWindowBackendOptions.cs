namespace Luxel.Platform.Web;

/// <summary>Identifies an existing HTML canvas used by one Luxel window.</summary>
public sealed record WebCanvasOptions(string Selector)
{
    /// <summary>Identifies a canvas by DOM id without requiring CSS selector escaping.</summary>
    public static WebCanvasOptions FromId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new WebCanvasOptions($"id:{id}") { SurfaceToken = id };
    }
    /// <summary>
    /// Stable token exposed to graphics backends. Defaults to <see cref="Selector"/> and may be a canvas id
    /// or another selector understood by the browser graphics integration.
    /// </summary>
    public string? SurfaceToken { get; init; }
}

/// <summary>Configuration for a browser canvas window backend.</summary>
public sealed class WebWindowBackendOptions
{
    /// <summary>
    /// ES module URL passed to JSHost.ImportAsync. The host must publish the project asset at this URL.
    /// </summary>
    public string ModuleUrl { get; init; } = "./luxel-platform-web.js";

    /// <summary>
    /// Existing canvases assigned to CreateWindow calls in order. At least one canvas is required.
    /// </summary>
    public IReadOnlyList<WebCanvasOptions> Canvases { get; init; } = Array.Empty<WebCanvasOptions>();
}

/// <summary>
/// Browser-only surface feature. Browser graphics backends use this token instead of interpreting
/// <see cref="Luxel.Platform.Abstraction.IWindowBackendWindow.Handle"/> as a native pointer.
/// </summary>
public interface IWebCanvasSurfaceProvider
{
    string CanvasToken { get; }
}
