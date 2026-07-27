using Luxel.Typography;
using Luxel.UI;

namespace Luxel.UI.App;

/// <summary>Configuration for the minimal Linux/X11 Luxel UI application host.</summary>
public sealed record LuxelAppOptions
{
    public string Title { get; set; } = "Luxel";
    public int Width { get; set; } = 960;
    public int Height { get; set; } = 640;
    public bool EnableValidation { get; set; } = true;
    public Func<VectorFont>? FontFactory { get; set; }
    public Theme? Theme { get; set; }
    public int? RunFrames { get; set; }
    public Action<string>? Diagnostic { get; set; }
}
