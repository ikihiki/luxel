using Luxel.Typography;
using Luxel.UI;

namespace Luxel.UI.App;

/// <summary>Configuration for the minimal Linux/X11 Luxel UI application host.</summary>
public sealed record LuxelAppOptions
{
    public string Title { get; init; } = "Luxel";
    public int Width { get; init; } = 960;
    public int Height { get; init; } = 640;
    public bool EnableValidation { get; init; } = true;
    public Func<VectorFont>? FontFactory { get; init; }
    public Theme? Theme { get; init; }
    public int? RunFrames { get; init; }
    public Action<string>? Diagnostic { get; init; }
}
