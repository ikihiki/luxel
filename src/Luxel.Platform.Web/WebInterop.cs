using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace Luxel.Platform.Web;

[SupportedOSPlatform("browser")]
internal static partial class WebInterop
{
    internal const string ModuleName = "luxel-platform-web";

    internal static async ValueTask ImportAsync(string moduleUrl, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleUrl);
        _ = await JSHost.ImportAsync(ModuleName, moduleUrl, cancellationToken).ConfigureAwait(false);
    }

    [JSImport("createWindow", ModuleName)]
    internal static partial int CreateWindow(string selector, string title, int width, int height, bool visible);

    [JSImport("destroyWindow", ModuleName)]
    internal static partial void DestroyWindow(int windowId);

    [JSImport("setTitle", ModuleName)]
    internal static partial void SetTitle(int windowId, string title);

    [JSImport("setBounds", ModuleName)]
    internal static partial void SetBounds(int windowId, int width, int height, bool setWidth, bool setHeight);

    [JSImport("showWindow", ModuleName)]
    internal static partial void ShowWindow(int windowId);

    [JSImport("hideWindow", ModuleName)]
    internal static partial void HideWindow(int windowId);

    [JSImport("focusWindow", ModuleName)]
    internal static partial void FocusWindow(int windowId);

    [JSImport("closeWindow", ModuleName)]
    internal static partial void CloseWindow(int windowId);

    [JSImport("setCursor", ModuleName)]
    internal static partial void SetCursor(int windowId, int cursorKind);

    [JSImport("dequeueEventKind", ModuleName)]
    internal static partial int DequeueEventKind();

    [JSImport("eventWindowId", ModuleName)]
    internal static partial int EventWindowId();

    [JSImport("eventNumber", ModuleName)]
    internal static partial double EventNumber(int index);

    [JSImport("eventInteger", ModuleName)]
    internal static partial int EventInteger(int index);

    [JSImport("eventText", ModuleName)]
    internal static partial string? EventText();

    [JSImport("setClipboardText", ModuleName)]
    internal static partial void SetClipboardText(string text);

    [JSImport("requestClipboardRead", ModuleName)]
    internal static partial void RequestClipboardRead();

    [JSImport("clipboardText", ModuleName)]
    internal static partial string ClipboardText();
}
