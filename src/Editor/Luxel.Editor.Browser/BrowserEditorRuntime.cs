using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using Luxel.Controls;
using Luxel.Graphics;
using Luxel.Graphics.TwoD;
using Luxel.Graphics.WebGPU.Browser;
using Luxel.Platform;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Web;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Editor.Browser;

[System.Runtime.Versioning.SupportedOSPlatform("browser")]
internal static partial class BrowserEditorRuntime
{
    public static async Task RunAsync(Widget root, BrowserProjectCoordinator coordinator, BrowserJsServices js)
    {
        using WebWindowBackend web = await WebWindowBackend.CreateAsync(new WebWindowBackendOptions
        {
            ModuleUrl = "../luxel-platform-web.js",
            Canvases = [new WebCanvasOptions("#luxel-canvas") { SurfaceToken = "#luxel-canvas" }],
        });
        using var clipboard = new Clipboard(web.CreateClipboardBackend());
        PlatformClipboard.Current = clipboard;
        try
        {
            using var windows = new WindowSystem(web);
            Window window = windows.CreateWindow(new WindowDesc("Luxel Editor", 1280, 800));
            windows.Pump();
            BrowserWebGpuBackend backend = await BrowserWebGpuBackend.CreateAsync();
            using var device = new GpuDevice(backend);
            using GpuSurface surface = backend.CreateCanvasSurface("#luxel-canvas", (uint)window.Width, (uint)window.Height);
            using var raster = new GpuDeviceRasterizer2D(device, RasterShader);
            using var font = new VectorFont(Resource("BIZUDGothic-Regular.ttf"));
            using var canvas = new RetainedCanvas();
            using IRasterScene2D scene = raster.CreateScene(canvas);
            using var ui = new UiHost(canvas, font, window.Width, window.Height, gpuRasterizer: raster);
            ui.SetRoot(root);

            GpuBuffer framebuffer = device.Malloc(checked((ulong)window.Width * (uint)window.Height * 4), GpuMemoryKind.DeviceLocal);
            bool resizePending = false;
            int resizeWidth = window.Width, resizeHeight = window.Height;
            window.Resized += (width, height) => { resizePending = true; resizeWidth = Math.Max(1, width); resizeHeight = Math.Max(1, height); };
            window.PointerMoved += input => ui.PointerMove(input.X, input.Y);
            window.PointerDown += input => ui.PointerDown(input.X, input.Y, MapButton(input.Button));
            window.PointerUp += input => ui.PointerUp(input.X, input.Y, MapButton(input.Button));
            window.Wheel += input => ui.Wheel(input.X, input.Y, input.Delta);
            window.KeyDown += input => ui.KeyDown(MapKey(input.Key),
                input.Modifiers.HasFlag(WindowKeyModifiers.Shift), input.Modifiers.HasFlag(WindowKeyModifiers.Control),
                input.Modifiers.HasFlag(WindowKeyModifiers.Alt));
            window.TextInput += ui.Commit;
            window.FocusChanged += focused => { if (focused && !ui.HasFocus) ui.FocusNext(); };

            async Task RenderAsync()
            {
                using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
                scene.Render(Camera2D.Pixels, new GpuRasterTarget2D(command, framebuffer, (uint)window.Width, (uint)window.Height));
                command.Finish();
                await device.MainQueue.SubmitAsync(command);
                surface.Present(framebuffer, (uint)window.Width, (uint)window.Width, (uint)window.Height);
            }

            await RenderAsync();
            SetReady($"Luxel Editor ready — {device.Name}");
            while (windows.Pump())
            {
                if (resizePending)
                {
                    await device.MainQueue.WaitIdleAsync();
                    framebuffer.Dispose();
                    framebuffer = device.Malloc(checked((ulong)resizeWidth * (uint)resizeHeight * 4), GpuMemoryKind.DeviceLocal);
                    surface.Resize((uint)resizeWidth, (uint)resizeHeight);
                    ui.Resize(resizeWidth, resizeHeight);
                    resizePending = false;
                }
                ui.Tick(1f / 60f);
                js.SetDirty(coordinator.RequiresUnloadWarning);
                if (canvas.HasPendingChanges) await RenderAsync();
                await NextFrame();
            }
            framebuffer.Dispose();
        }
        finally { PlatformClipboard.Current = null; }
    }

    private static PointerButton MapButton(WindowPointerButton button) => button switch
    {
        WindowPointerButton.Right => PointerButton.Right,
        WindowPointerButton.Middle => PointerButton.Middle,
        _ => PointerButton.Left,
    };

    private static Key MapKey(WindowKey key) => key switch
    {
        >= WindowKey.A and <= WindowKey.Z when Enum.TryParse(key.ToString(), out Key letter) => letter,
        >= WindowKey.D0 and <= WindowKey.D9 when Enum.TryParse(key.ToString(), out Key digit) => digit,
        >= WindowKey.F1 and <= WindowKey.F12 when Enum.TryParse(key.ToString(), out Key function) => function,
        WindowKey.Tab => Key.Tab, WindowKey.Enter => Key.Enter, WindowKey.Space => Key.Space,
        WindowKey.Escape => Key.Escape, WindowKey.Backspace => Key.Backspace, WindowKey.Delete => Key.Delete,
        WindowKey.Left => Key.Left, WindowKey.Right => Key.Right, WindowKey.Up => Key.Up, WindowKey.Down => Key.Down,
        WindowKey.Home => Key.Home, WindowKey.End => Key.End, WindowKey.PageUp => Key.PageUp, WindowKey.PageDown => Key.PageDown,
        WindowKey.Slash => Key.Slash, _ => Key.None,
    };

    private static GpuShaderCode RasterShader(string name) => new() { Wgsl = Resource(name + ".wgsl") };
    private static byte[] Resource(string name)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string resource = assembly.GetManifestResourceNames().Single(x => x.EndsWith("." + name, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }

    [JSImport("nextFrame", "luxel-editor-host")] private static partial Task<double> NextFrame();
    [JSImport("setReady", "luxel-editor-host")] private static partial void SetReady(string summary);
}
