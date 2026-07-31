using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luxel.Controls;
using Luxel.Gallery;
using Luxel.Graphics;
using Luxel.Graphics.Abstraction;
using Luxel.Graphics.TwoD;
using Luxel.Graphics.WebGPU.Browser;
using Luxel.Platform;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Web;
using Luxel.Typography;
using Luxel.UI;

namespace LuxelWebGpuBrowser;

[SupportedOSPlatform("browser")]
public static partial class Program
{
    private const uint ExpectedCompute = 0xc0ffee42;
    private static StoryCatalog? _catalog;
    private static StoryCatalog Catalog => _catalog ??= CoreUiStoryProject.CreateCatalog();
    private static StoryContext? _activeContext;
    private static string? _activeStory;

    public static async Task Main()
    {
        string story = GetStory();
        try
        {
            if (story == CanonicalTriangleRecipe.Story) await RunTriangle();
            else await RunCatalogStory(story);
        }
        catch (Exception ex)
        {
            SetStatus("fail", $"browser-webgpu: status=fail, story={story}, error={ex}");
            Console.Error.WriteLine(ex);
            throw;
        }
    }

    private static async Task RunCatalogStory(string path)
    {
        StoryInfo story = Catalog.Find(path) ?? throw new InvalidOperationException($"Unknown CoreUi story '{path}'.");
        if (story.RuntimeBundleId != CoreUiStoryProject.RuntimeBundleId)
            throw new InvalidOperationException($"Story '{path}' is not owned by the CoreUi browser runtime.");
        IReadOnlyList<StoryArgDefinition> schema = story.ArgDefinitions ?? Array.Empty<StoryArgDefinition>();
        StoryArgs args = StoryArgs.Parse(GetArgsJson()).WithDefaults(schema);
        var context = new StoryContext(args: args);
        _activeContext = context;
        _activeStory = path;
        context.ArgsChanged += changed => PublishArgsChanged(changed.ToJson());
        context.Logged += entry => PublishEvent(JsonSerializer.Serialize(entry, BrowserJsonContext.Default.StoryLogEntry));
        StoryResult result = story.BuildResult(context);
        if (result.Kind != StoryResultKind.Widget || result.Widget is null)
            throw new InvalidOperationException($"CoreUi runtime story '{path}' did not build a Widget.");

        using WebWindowBackend web = await CreateWindowBackend();
        using var clipboard = new Clipboard(web.CreateClipboardBackend());
        PlatformClipboard.Current = clipboard;
        try
        {
            using var windows = new WindowSystem(web);
            int initialWidth = story.Width > 0 ? story.Width : 640;
            int initialHeight = story.Height > 0 ? story.Height : 360;
            Window window = windows.CreateWindow(new WindowDesc("Luxel " + path, initialWidth, initialHeight));
            windows.Pump();
            using BrowserWebGpuBackend backend = await BrowserWebGpuBackend.CreateAsync();
            using var device = new GpuDevice(backend);
            using GpuSurface surface = device.CreateCanvasSurface("#luxel-canvas", (uint)window.Width, (uint)window.Height);
            using var raster = new GpuDeviceRasterizer2D(device, RasterShader);
            using var font = new VectorFont(Resource("BIZUDGothic-Regular.ttf"));
            using var canvas = new RetainedCanvas();
            using IRasterScene2D scene = raster.CreateScene(canvas);
            using var ui = new UiHost(canvas, font, window.Width, window.Height, gpuRasterizer: raster);
            ui.SetRoot(result.Widget);

            GpuBuffer framebuffer = device.Malloc(checked((ulong)window.Width * (uint)window.Height * 4), GpuMemoryKind.DeviceLocal);
            bool resizePending = false;
            int resizeWidth = window.Width, resizeHeight = window.Height;
            int renderRevision = 0;
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
                PublishFrame(++renderRevision);
                PublishDiagnostics(JsonSerializer.Serialize(
                    SnapshotWidgets(result.Widget), BrowserJsonContext.Default.BrowserWidgetDiagnosticArray));
            }

            await RenderAsync();
            SetReady($"browser-webgpu: status=pass\nstory={path}\ndevice={device.Name}", context.Args.ToJson(),
                JsonSerializer.Serialize(schema.ToArray(), BrowserJsonContext.Default.StoryArgDefinitionArray));
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
            PlatformClipboard.Current = null;
            _activeContext = null;
            _activeStory = null;
        }
    }

    private static async Task RunTriangle()
    {
        uint width = CanonicalTriangleRecipe.Width, height = CanonicalTriangleRecipe.Height;
        using WebWindowBackend web = await CreateWindowBackend();
        using var windows = new WindowSystem(web);
        Window window = windows.CreateWindow(new WindowDesc("Luxel browser WebGPU", (int)width, (int)height));
        int pointerEvents = 0, keyEvents = 0, resizeEvents = 0;
        bool resizePending = false;
        window.PointerMoved += _ => pointerEvents++;
        window.PointerDown += _ => pointerEvents++;
        window.PointerUp += _ => pointerEvents++;
        window.KeyDown += _ => keyEvents++;
        window.KeyUp += _ => keyEvents++;
        window.Resized += (_, _) => { resizeEvents++; resizePending = true; };
        windows.Pump();
        using BrowserWebGpuBackend gpu = await BrowserWebGpuBackend.CreateAsync();
        using BrowserWebGpuSurface surface = gpu.CreateCanvasSurface("#luxel-canvas", (uint)window.Width, (uint)window.Height);
        resizePending = false;
        using IGpuBackendBuffer compute = gpu.CreateBuffer(256, GpuMemoryKind.HostCached);
        using IGpuBackendPipeline computePipeline = gpu.CreateComputePipeline(Resource("compute.wgsl"), "main");
        using (IGpuBackendCommandBuffer command = gpu.MainQueue.StartCommandRecording())
        {
            command.SetComputePipeline(computePipeline); command.SetRootConstants(Bytes(new ComputeRoot(compute.BindlessIndex, ExpectedCompute)));
            command.Dispatch(1, 1, 1); command.Finish(); await gpu.AsyncQueue.SubmitAsync(command);
        }
        uint computeValue = Read<uint>(compute);
        CanonicalTriangleRecipe.Vertex[] triangleVertices = CanonicalTriangleRecipe.CreateVertices();
        using IGpuBackendBuffer vertices = gpu.CreateBuffer((ulong)(triangleVertices.Length * CanonicalTriangleRecipe.VertexSize), GpuMemoryKind.HostMapped);
        Write(vertices, triangleVertices);
        using IGpuBackendTexture target = gpu.CreateRenderTarget(width, height, GpuFormat.Rgba8Unorm);
        using IGpuBackendBuffer pixels = gpu.CreateBuffer(width * height * 4, GpuMemoryKind.HostCached);
        using IGpuBackendPipeline graphics = gpu.CreateGraphicsPipeline(Resource("tutorial_triangle.wgsl"), "vsMain", Resource("tutorial_triangle.wgsl"), "psMain", GpuRasterDesc.Default(GpuFormat.Rgba8Unorm));
        using (IGpuBackendCommandBuffer command = gpu.MainQueue.StartCommandRecording())
        {
            command.SetGraphicsPipeline(graphics); command.SetRootConstants(Bytes(new CanonicalTriangleRecipe.DrawArgs { VertexBufferIndex = vertices.BindlessIndex }));
            command.BeginRendering(target, null, 0.04f, 0.07f, 0.12f, 1f, 1f); command.Draw(3, 1); command.EndRendering();
            command.CopyTextureToBuffer(target, pixels, width); command.Finish(); await gpu.AsyncQueue.SubmitAsync(command);
        }
        int center = checked((int)((height / 2 * width + width / 2) * 4));
        Span<byte> data = Mapped(pixels); byte red = data[center], green = data[center + 1], blue = data[center + 2], alpha = data[center + 3];
        if (alpha < 240 || red + green + blue < 180) throw new InvalidOperationException($"Center pixel was rgba({red},{green},{blue},{alpha}).");
        surface.Present(pixels, width, width, height);
        SetStatus("pass", $"browser-webgpu: status=pass\nstory={CanonicalTriangleRecipe.Story}\nshader={CanonicalTriangleRecipe.Shader}\nvertexSize={CanonicalTriangleRecipe.VertexSize}; rootSize={CanonicalTriangleRecipe.DrawArgsSize}\ncanvas={width}x{height}\nrecipe={CanonicalTriangleRecipe.Recipe}\nhash={CanonicalTriangleRecipe.ShaderSha256}\ndevice={gpu.Name}\ncompute=0x{computeValue:x8}; center=rgba({red},{green},{blue},{alpha})\nframes=1+; resize={resizeEvents}; pointer={pointerEvents}; key={keyEvents}");
        while (windows.Pump()) { if (resizePending) { surface.Resize((uint)window.Width, (uint)window.Height); surface.Present(pixels, width, width, height); resizePending = false; } await NextFrame(); }
    }

    [JSExport]
    public static string SetArgsSnapshot(string story, string instanceId, string argsJson, int revision, string requestId)
    {
        if (_activeContext is null || _activeStory != story)
            return JsonSerializer.Serialize(
                new SetArgsResponse(story, instanceId, revision, requestId, null, ["Story instance is not ready."]),
                BrowserJsonContext.Default.SetArgsResponse);
        IReadOnlyList<string> errors;
        try { errors = _activeContext.ApplyArgs(StoryArgs.Parse(argsJson)); }
        catch (Exception error) when (error is JsonException or FormatException)
        { errors = new[] { error.Message }; }
        return JsonSerializer.Serialize(
            new SetArgsResponse(
                story,
                instanceId,
                revision,
                requestId,
                _activeContext.Args.Values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal),
                errors.ToArray()),
            BrowserJsonContext.Default.SetArgsResponse);
    }

    private static BrowserWidgetDiagnostic[] SnapshotWidgets(Widget root)
    {
        var widgets = new List<BrowserWidgetDiagnostic>();
        Visit(root, widgets);
        return widgets.ToArray();

        static void Visit(Widget widget, List<BrowserWidgetDiagnostic> output)
        {
            output.Add(new BrowserWidgetDiagnostic(
                widget.GetType().FullName ?? widget.GetType().Name,
                widget.DebugDetail,
                widget.WorldPos.X,
                widget.WorldPos.Y,
                widget.Size.Width,
                widget.Size.Height));
            foreach (Widget child in widget.DebugChildren()) Visit(child, output);
        }
    }

    private static async Task<WebWindowBackend> CreateWindowBackend() => await WebWindowBackend.CreateAsync(new WebWindowBackendOptions
    {
        ModuleUrl = "../luxel-platform-web.js",
        Canvases = [new WebCanvasOptions("#luxel-canvas") { SurfaceToken = "#luxel-canvas" }],
    });

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
        WindowKey.D7 => Key.D7, WindowKey.D8 => Key.D8, WindowKey.D9 => Key.D9,
        WindowKey.F1 => Key.F1, WindowKey.F2 => Key.F2, WindowKey.F3 => Key.F3, WindowKey.F4 => Key.F4,
        WindowKey.F5 => Key.F5, WindowKey.F6 => Key.F6, WindowKey.F7 => Key.F7, WindowKey.F8 => Key.F8,
        WindowKey.F9 => Key.F9, WindowKey.F10 => Key.F10, WindowKey.F11 => Key.F11, WindowKey.F12 => Key.F12,
        WindowKey.Slash => Key.Slash,
        _ => Key.None,
    };

    private static GpuShaderCode RasterShader(string name) => new() { Wgsl = Resource(name + ".wgsl") };
    private static byte[] Resource(string name)
    {
        string resource = Assembly.GetExecutingAssembly().GetManifestResourceNames().Single(n => n.EndsWith("." + name, StringComparison.Ordinal));
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }
    private static byte[] Bytes<T>(T value) where T : unmanaged { T[] one = [value]; return MemoryMarshal.AsBytes(one.AsSpan()).ToArray(); }
    private static unsafe Span<byte> Mapped(IGpuBackendBuffer buffer) => new(buffer.MappedPointer, checked((int)buffer.Size));
    private static T Read<T>(IGpuBackendBuffer buffer) where T : unmanaged => MemoryMarshal.Read<T>(Mapped(buffer));
    private static void Write<T>(IGpuBackendBuffer buffer, ReadOnlySpan<T> values) where T : unmanaged => MemoryMarshal.AsBytes(values).CopyTo(Mapped(buffer));
    private sealed record BrowserWidgetDiagnostic(string Type, string? Detail, float X, float Y, float Width, float Height);
    private sealed record SetArgsResponse(string Story, string InstanceId, int Revision, string RequestId,
        Dictionary<string, JsonElement>? Args, string[] Errors);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(BrowserWidgetDiagnostic[]))]
    [JsonSerializable(typeof(StoryArgDefinition[]))]
    [JsonSerializable(typeof(StoryLogEntry))]
    [JsonSerializable(typeof(SetArgsResponse))]
    private sealed partial class BrowserJsonContext : JsonSerializerContext;

    private readonly record struct ComputeRoot(uint BufferIndex, uint Value, uint Pad0 = 0, uint Pad1 = 0);

    [JSImport("getArgsJson", "luxel-browser-host")] private static partial string GetArgsJson();
    [JSImport("getStory", "luxel-browser-host")] private static partial string GetStory();
    [JSImport("nextFrame", "luxel-browser-host")] private static partial Task<double> NextFrame();
    [JSImport("setStatus", "luxel-browser-host")] private static partial void SetStatus(string state, string summary);
    [JSImport("setReady", "luxel-browser-host")] private static partial void SetReady(string summary, string argsJson, string schemaJson);
    [JSImport("publishArgsChanged", "luxel-browser-host")] private static partial void PublishArgsChanged(string argsJson);
    [JSImport("publishEvent", "luxel-browser-host")] private static partial void PublishEvent(string entryJson);
    [JSImport("publishDiagnostics", "luxel-browser-host")] private static partial void PublishDiagnostics(string widgetsJson);
    [JSImport("publishFrame", "luxel-browser-host")] private static partial void PublishFrame(int revision);
}
