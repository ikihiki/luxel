using Luxel.Platform;

namespace Luxel.Platform.Web;

internal enum WebEventKind
{
    Resize = 1,
    Focus = 2,
    PointerMove = 3,
    PointerDown = 4,
    PointerUp = 5,
    Wheel = 6,
    KeyDown = 7,
    KeyUp = 8,
    TextInput = 9,
    Close = 10,
}

internal readonly record struct WebEvent(
    int WindowId,
    WebEventKind Kind,
    double A = 0,
    double B = 0,
    double C = 0,
    double D = 0,
    double E = 0,
    double F = 0,
    double G = 0,
    int I0 = 0,
    int I1 = 0,
    int I2 = 0,
    int I3 = 0,
    string? Text = null);

internal static class WebCoordinateNormalizer
{
    public static (float X, float Y) ToBackingPixels(
        double clientX,
        double clientY,
        double rectLeft,
        double rectTop,
        double cssWidth,
        double cssHeight,
        int backingWidth,
        int backingHeight)
    {
        if (!double.IsFinite(clientX) || !double.IsFinite(clientY) ||
            !double.IsFinite(rectLeft) || !double.IsFinite(rectTop) ||
            !double.IsFinite(cssWidth) || !double.IsFinite(cssHeight) ||
            cssWidth <= 0 || cssHeight <= 0 || backingWidth <= 0 || backingHeight <= 0)
        {
            return (0, 0);
        }

        double x = (clientX - rectLeft) * backingWidth / cssWidth;
        double y = (clientY - rectTop) * backingHeight / cssHeight;
        return ((float)x, (float)y);
    }
}

internal sealed class WebEventQueue
{
    private readonly Queue<WebEvent> _events = new();

    public int Count => _events.Count;
    public void Enqueue(in WebEvent value) => _events.Enqueue(value);
    public bool TryDequeue(out WebEvent value) => _events.TryDequeue(out value);
}
