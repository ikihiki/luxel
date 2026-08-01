using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using Luxel.Controls;
using Luxel.Graphics;
using Luxel.Graphics.TwoD;
using Luxel.Graphics.WebGPU.Browser;
using Luxel.Platform;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Web;
using Luxel.Scripting.Roslyn.Web;
using Luxel.Typography;
using Luxel.UI;

namespace LuxelPlaygroundBrowser;

[SupportedOSPlatform("browser")]
public static partial class Program
{
    private static readonly WebScriptExecutor Executor = new();
    private static WebScriptCompiler? _compiler;
    private static UiHost? _ui;
    private static int _latestRevision;
    private static int _pendingFirstFrameRevision;

    public static async Task Main()
    {
        try
        {
            _compiler = new WebScriptCompiler(await LoadMetadataReferences());
            await RunHost();
        }
        catch (Exception exception)
        {
            SetFatalError(exception.ToString());
            Console.Error.WriteLine(exception);
            throw;
        }
    }

    [JSExport]
    public static string Run(string source, int revision)
    {
        if (_compiler is null || _ui is null)
            return Serialize(new RunResponse("runtime-error", [], new FailureResponse("infrastructure", "The playground runtime is not ready.", null, null)));
        if (revision <= _latestRevision)
            return Serialize(new RunResponse("runtime-error", [], new FailureResponse("protocol", "The source revision is not newer than the active revision.", null, null)));

        _latestRevision = revision;
        try
        {
            WebScriptCompilation compilation = _compiler.Compile(source, $"Luxel.Playground.Script.{revision}");
            DiagnosticResponse[] diagnostics = compilation.Diagnostics.Select(diagnostic => new DiagnosticResponse(
                diagnostic.Id,
                diagnostic.Message,
                diagnostic.Severity.ToString().ToLowerInvariant(),
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.Length)).ToArray();
            if (!compilation.Success || compilation.PeImage is null)
                return Serialize(new RunResponse("diagnostics", diagnostics, null));

            WebScriptExecution execution = Executor.Execute(compilation.PeImage, compilation.PdbImage ?? []);
            if (!execution.Success || execution.Widget is null)
            {
                WebScriptFailure failure = execution.Failure ?? new WebScriptFailure("runtime", "Script execution failed.");
                return Serialize(new RunResponse(
                    "runtime-error",
                    diagnostics,
                    new FailureResponse(failure.Kind, failure.Message, failure.ExceptionType, failure.Line)));
            }

            _ui.SetRoot(execution.Widget);
            _pendingFirstFrameRevision = revision;
            return Serialize(new RunResponse("render-pending", diagnostics, null));
        }
        catch (Exception exception)
        {
            return Serialize(new RunResponse(
                "runtime-error",
                [],
                new FailureResponse("infrastructure", exception.Message, exception.GetType().FullName, null)));
        }
    }

    private static async Task RunHost()
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
            Window window = windows.CreateWindow(new WindowDesc("Luxel Playground", 640, 360));
            windows.Pump();
            using BrowserWebGpuBackend backend = await BrowserWebGpuBackend.CreateAsync();
            using var device = new GpuDevice(backend);
            using GpuSurface surface = device.CreateCanvasSurface("#luxel-canvas", (uint)window.Width, (uint)window.Height);
            using var raster = new GpuDeviceRasterizer2D(device, RasterShader);
            using var font = new VectorFont(Resource("BIZUDGothic-Regular.ttf"));
            using var canvas = new RetainedCanvas();
            using IRasterScene2D scene = raster.CreateScene(canvas);
            using var ui = new UiHost(canvas, font, window.Width, window.Height, gpuRasterizer: raster);
            _ui = ui;
            ui.SetRoot(Kit.Text("Send a revisioned run message to render C# source."));

            GpuBuffer framebuffer = device.Malloc(checked((ulong)window.Width * (uint)window.Height * 4), GpuMemoryKind.DeviceLocal);
            bool resizePending = false;
            int resizeWidth = window.Width, resizeHeight = window.Height;
            window.Resized += (width, height) => { resizePending = true; resizeWidth = Math.Max(1, width); resizeHeight = Math.Max(1, height); };
            window.PointerMoved += input => ui.PointerMove(input.X, input.Y);
            window.PointerDown += input => ui.PointerDown(input.X, input.Y, MapButton(input.Button));
            window.PointerUp += input => ui.PointerUp(input.X, input.Y, MapButton(input.Button));
            window.Wheel += input => ui.Wheel(input.X, input.Y, input.Delta);
            window.KeyDown += input => ui.KeyDown(MapKey(input.Key),
                input.Modifiers.HasFlag(WindowKeyModifiers.Shift),
                input.Modifiers.HasFlag(WindowKeyModifiers.Control),
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
                if (_pendingFirstFrameRevision != 0)
                {
                    int revision = _pendingFirstFrameRevision;
                    _pendingFirstFrameRevision = 0;
                    PublishFirstFrame(revision);
                }
            }

            await RenderAsync();
            SetReady(device.Name);
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
                if (canvas.HasPendingChanges) await RenderAsync();
                await NextFrame();
            }
            framebuffer.Dispose();
        }
        finally
        {
            _ui = null;
            PlatformClipboard.Current = null;
        }
    }

    private static async Task<MetadataReferenceImage[]> LoadMetadataReferences()
    {
        using var http = new HttpClient { BaseAddress = new Uri(GetBaseUrl(), UriKind.Absolute) };
        string json = await http.GetStringAsync("references/manifest.json");
        ReferenceManifest manifest = JsonSerializer.Deserialize<ReferenceManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException("The metadata reference manifest is invalid.");
        if (manifest.Version != 1 || manifest.Assemblies is not { Length: > 0 })
            throw new InvalidOperationException("The metadata reference manifest version or assembly list is invalid.");
        var references = new List<MetadataReferenceImage>(manifest.Assemblies.Length);
        foreach (string fileName in manifest.Assemblies)
            references.Add(new MetadataReferenceImage(fileName, await http.GetByteArrayAsync("references/" + fileName)));
        return references.ToArray();
    }

    private static string Serialize(RunResponse response) => JsonSerializer.Serialize(response, JsonOptions);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static GpuShaderCode RasterShader(string name) => new() { Wgsl = Resource(name + ".wgsl") };
    private static byte[] Resource(string name)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string resource = assembly.GetManifestResourceNames().Single(value => value.EndsWith("." + name, StringComparison.Ordinal));
        using Stream stream = assembly.GetManifestResourceStream(resource)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static PointerButton MapButton(WindowPointerButton button) => button switch
    {
        WindowPointerButton.Middle => PointerButton.Middle,
        WindowPointerButton.Right => PointerButton.Right,
        _ => PointerButton.Left,
    };

    private static Key MapKey(WindowKey key) => key switch
    {
        WindowKey.Tab => Key.Tab, WindowKey.Enter => Key.Enter, WindowKey.Space => Key.Space,
        WindowKey.Escape => Key.Escape, WindowKey.Backspace => Key.Backspace, WindowKey.Delete => Key.Delete,
        WindowKey.Left => Key.Left, WindowKey.Right => Key.Right, WindowKey.Up => Key.Up, WindowKey.Down => Key.Down,
        WindowKey.Home => Key.Home, WindowKey.End => Key.End, WindowKey.PageUp => Key.PageUp, WindowKey.PageDown => Key.PageDown,
        WindowKey.A => Key.A, WindowKey.B => Key.B, WindowKey.C => Key.C, WindowKey.D => Key.D, WindowKey.E => Key.E,
        WindowKey.F => Key.F, WindowKey.G => Key.G, WindowKey.H => Key.H, WindowKey.I => Key.I, WindowKey.J => Key.J,
        WindowKey.K => Key.K, WindowKey.L => Key.L, WindowKey.M => Key.M, WindowKey.N => Key.N, WindowKey.O => Key.O,
        WindowKey.P => Key.P, WindowKey.Q => Key.Q, WindowKey.R => Key.R, WindowKey.S => Key.S, WindowKey.T => Key.T,
        WindowKey.U => Key.U, WindowKey.V => Key.V, WindowKey.W => Key.W, WindowKey.X => Key.X, WindowKey.Y => Key.Y,
        WindowKey.Z => Key.Z, WindowKey.D0 => Key.D0, WindowKey.D1 => Key.D1, WindowKey.D2 => Key.D2,
        WindowKey.D3 => Key.D3, WindowKey.D4 => Key.D4, WindowKey.D5 => Key.D5, WindowKey.D6 => Key.D6,
        WindowKey.D7 => Key.D7, WindowKey.D8 => Key.D8, WindowKey.D9 => Key.D9, WindowKey.Slash => Key.Slash,
        _ => Key.None,
    };

    private sealed record ReferenceManifest(int Version, string[] Assemblies);
    private sealed record DiagnosticResponse(string Id, string Message, string Severity, int? Line, int? Column, int Length);
    private sealed record FailureResponse(string Kind, string Message, string? ExceptionType, int? Line);
    private sealed record RunResponse(string Outcome, DiagnosticResponse[] Diagnostics, FailureResponse? Failure);

    [JSImport("getBaseUrl", "luxel-playground-host")] private static partial string GetBaseUrl();
    [JSImport("nextFrame", "luxel-playground-host")] private static partial Task<double> NextFrame();
    [JSImport("setReady", "luxel-playground-host")] private static partial void SetReady(string deviceName);
    [JSImport("setFatalError", "luxel-playground-host")] private static partial void SetFatalError(string error);
    [JSImport("publishFirstFrame", "luxel-playground-host")] private static partial void PublishFirstFrame(int revision);
}
