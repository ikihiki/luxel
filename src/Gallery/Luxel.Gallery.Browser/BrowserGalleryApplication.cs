using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.AssetsGpu;
using Luxel.Audio.Browser;
using Luxel.Audio.Gallery;
using Luxel.Controls;
using Luxel.Gallery;
using Luxel.Graphics;
using Luxel.Graphics.TwoD;
using Luxel.Graphics.WebGPU.Browser;
using Luxel.Platform;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Web;
using Luxel.Resources;
using Luxel.Resources.Browser;
using Luxel.Shaders;
using Luxel.Shaders.Slang.Browser;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Gallery.Browser;

/// <summary>Configures and runs the browser Gallery runtime.</summary>
[SupportedOSPlatform("browser")]
public static partial class BrowserGalleryApplication
{
    private static IServiceProvider? _storyServices;
    private static StoryCatalog Catalog => (_storyServices
        ?? throw new InvalidOperationException("Browser Gallery story services are not configured."))
        .GetRequiredService<StoryCatalog>();
    private static StoryContext? _activeContext;
    private static string? _activeStory;

    public static async Task RunAsync(IServiceProvider storyServices, string story, string argsJson)
    {
        _storyServices = storyServices;
        try
        {
            await RunCatalogStory(story, argsJson);
        }
        catch (Exception ex)
        {
            PublishWebGpuDiagnostics(BrowserWebGpuBackend.CaptureLatestDiagnostics(ex, "BrowserGalleryApplication.RunAsync"));
            SetStatus("fail", $"browser-webgpu: status=fail, story={story}, error={ex}");
            Console.Error.WriteLine(ex);
            throw;
        }
        finally
        {
            _storyServices = null;
        }
    }

    private static async Task RunCatalogStory(string path, string argsJson)
    {
        string stage = "catalog";
        try
        {
        StoryInfo story = Catalog.Find(path) ?? throw new InvalidOperationException($"Unknown browser story '{path}'.");
        IReadOnlyList<StoryArgDefinition> schema = story.ArgDefinitions ?? Array.Empty<StoryArgDefinition>();
        StoryArgs args = StoryArgs.Parse(argsJson).WithDefaults(schema);

        stage = "window";
        SetStatus("loading", $"browser-webgpu: status=loading, story={path}, stage={stage}");
        using WebWindowBackend web = await CreateWindowBackend();
        using var clipboard = new Clipboard(web.CreateClipboardBackend());
        PlatformClipboard.Current = clipboard;
        BrowserAudioBackend? audio = null;
        try
        {
            if (path.StartsWith("Examples/Audio/", StringComparison.Ordinal))
            {
                audio = await BrowserAudioBackend.CreateAsync();
                AudioStories.ConfigureRuntime(audio, () => audio.ResumeAsync(), () => audio.SuspendAsync(), () => audio.State.ToString());
            }
            using var windows = new WindowSystem(web);
            int initialWidth = story.Width > 0 ? story.Width : 640;
            int initialHeight = story.Height > 0 ? story.Height : 360;
            Window window = windows.CreateWindow(new WindowDesc("Luxel " + path, initialWidth, initialHeight));
            windows.Pump();

            stage = "device";
            SetStatus("loading", $"browser-webgpu: status=loading, story={path}, stage={stage}");
            BrowserWebGpuBackend backend = await BrowserWebGpuBackend.CreateAsync();
            using var device = new GpuDevice(backend);
            var browserBackend = (BrowserWebGpuBackend)device.Backend;
            PublishWebGpuDiagnostics(browserBackend.CaptureDiagnostics());
            using GpuSurface surface = browserBackend.CreateCanvasSurface(
                "#luxel-canvas", (uint)window.Width, (uint)window.Height);
            PublishWebGpuDiagnostics(browserBackend.CaptureDiagnostics());
            stage = "resources";
            SetStatus("loading", $"browser-webgpu: status=loading, story={path}, stage={stage}");
            await using var slangCompiler = new BrowserSlangCompiler();
            HttpClient http = (_storyServices
                ?? throw new InvalidOperationException("Browser Gallery story services are not configured."))
                .GetRequiredService<HttpClient>();
            var files = new WebPlatformFileSystem(
                (resourcePath, cancellationToken) => http.GetByteArrayAsync(resourcePath, cancellationToken));
            var resourceBuilder = new ResourceSystemBuilder();
            ResourceSystemDefaultHandles defaults = resourceBuilder.AddBrowserCore();
            ResourceSystemDefaults.AddBuiltinSourcesForWeb(resourceBuilder, defaults, files, http);
            ResourceSystemDefaults.AddBuiltinSteps(resourceBuilder, defaults);
            resourceBuilder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep())
                .RunOn(defaults.CpuDomain).ManagedBy(defaults.CpuManager).Register();
            resourceBuilder.Steps.Add<SlangSource, GpuShaderCode>(
                    new SlangCompileStep(slangCompiler, GpuBackendKind.WebGpu))
                .RunOn(defaults.CpuDomain).ManagedBy(defaults.CpuManager).Register();
            resourceBuilder.AddAssetGpu(device, options =>
                options.ConfigureDomain = domain => domain.UseBrowserCooperative());
            await using ResourceSystem resources = await resourceBuilder.BuildAsync();
            stage = "font";
            SetStatus("loading", $"browser-webgpu: status=loading, story={path}, stage={stage}");
            using var font = new VectorFont(Resource("BIZUDGothic-Regular.ttf"));
            using var context = new StoryContext(resources, args);
            context.SetGpuHost(device, font);
            _activeContext = context;
            _activeStory = path;
            context.ArgsChanged += changed => PublishArgsChanged(changed.ToJson());
            context.Logged += entry => PublishEvent(
                JsonSerializer.Serialize(entry, BrowserJsonContext.Default.StoryLogEntry));

            stage = "story";
            SetStatus("loading", $"browser-webgpu: status=loading, story={path}, stage={stage}");
            StoryResult result = story.BuildResult(context);
            Widget storyRoot = BuildStoryWidget(story, context, result, font, window.Width, window.Height);

            stage = "render";
            SetStatus("loading", $"browser-webgpu: status=loading, story={path}, stage={stage}");
            using var raster = new GpuDeviceRasterizer2D(device, RasterShader);
            using var canvas = new RetainedCanvas();
            using IRasterScene2D scene = raster.CreateScene(canvas);
            using var ui = new UiHost(canvas, font, window.Width, window.Height, gpuRasterizer: raster);
            ui.SetRoot(storyRoot);

            GpuBuffer framebuffer = device.Malloc(
                checked((ulong)window.Width * (uint)window.Height * 4), GpuMemoryKind.DeviceLocal);
            bool resizePending = false;
            int resizeWidth = window.Width, resizeHeight = window.Height;
            int renderRevision = 0;
            window.Resized += (width, height) =>
            {
                resizePending = true;
                resizeWidth = Math.Max(1, width);
                resizeHeight = Math.Max(1, height);
            };
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
                scene.Render(Camera2D.Pixels,
                    new GpuRasterTarget2D(command, framebuffer, (uint)window.Width, (uint)window.Height));
                command.Finish();
                await device.MainQueue.SubmitAsync(command);
                surface.Present(framebuffer, (uint)window.Width, (uint)window.Width, (uint)window.Height);
                PublishWebGpuDiagnostics(browserBackend.CaptureDiagnostics());
                PublishFrame(++renderRevision);
                PublishDiagnostics(JsonSerializer.Serialize(
                    SnapshotWidgets(storyRoot), BrowserJsonContext.Default.BrowserWidgetDiagnosticArray));
            }

            try
            {
                await RenderAsync();
                SetReady($"browser-webgpu: status=pass\nstory={path}\ndevice={device.Name}", context.Args.ToJson(),
                    JsonSerializer.Serialize(schema.ToArray(), BrowserJsonContext.Default.StoryArgDefinitionArray));
                while (windows.Pump())
                {
                    if (resizePending)
                    {
                        await device.MainQueue.WaitIdleAsync();
                        framebuffer.Dispose();
                        framebuffer = device.Malloc(
                            checked((ulong)resizeWidth * (uint)resizeHeight * 4), GpuMemoryKind.DeviceLocal);
                        surface.Resize((uint)resizeWidth, (uint)resizeHeight);
                        ui.Resize(resizeWidth, resizeHeight);
                        resizePending = false;
                    }
                    await resources.PumpAsync();
                    await context.PumpObservedResourcesAsync();
                    ui.Tick(1f / 60f);
                    if (canvas.HasPendingChanges) await RenderAsync();
                    await NextFrame();
                }
            }
            finally
            {
                framebuffer.Dispose();
            }
        }
        finally
        {
            AudioStories.ResetRuntime();
            audio?.Dispose();
            PlatformClipboard.Current = null;
            _activeContext = null;
            _activeStory = null;
        }
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Browser Gallery failed during stage '{stage}'.", error);
        }
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

    private static Widget BuildStoryWidget(StoryInfo story, StoryContext context, StoryResult authored,
        VectorFont font, int width, int height)
    {
        if (authored.Kind == StoryResultKind.Widget)
            return authored.Widget ?? throw new InvalidOperationException($"Widget story '{story.Path}' returned no Widget.");

        StoryResult result = story.Toc
            ? authored.WithMarkdown(MarkdownDoc.InsertToc(authored.Markdown))
            : authored;
        var fences = new Dictionary<string, Func<string, Widget>>
        {
            ["mermaid"] = body => Luxel.Diagram.Factories.DiagramBlock(body, Math.Max(320f, width - 32f)),
            ["math"] = body => Luxel.MathText.Factories.MathBlockView(body, maxWidth: Math.Max(320f, width - 32f)),
        };
        return StoryMarkdownDocumentAdapter.FromStoryResult(result, () => UiTheme.T,
            Math.Max(320f, width), Math.Max(240f, height),
            reference => BuildStoryReference(context, reference, font, width, height),
            body: font, highlighter: Luxel.Highlight.TextMateHighlighter.Instance,
            fences: fences, fill: true);
    }

    private static Widget BuildStoryReference(StoryContext context, StoryReference reference,
        VectorFont font, int width, int height)
    {
        StoryInfo? referenced = Catalog.Find(reference.Path);
        if (referenced is null)
            return Kit.Alert($"Story not found: {reference.Path}", Intent.Danger);

        bool suppressed = context.SuppressPlays;
        context.SuppressPlays = true;
        try
        {
            StoryResult result = referenced.BuildResult(context);
            return BuildStoryWidget(referenced, context, result, font, width, height);
        }
        finally
        {
            context.SuppressPlays = suppressed;
        }
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
    private sealed record BrowserWidgetDiagnostic(string Type, string? Detail, float X, float Y, float Width, float Height);
    private sealed record SetArgsResponse(string Story, string InstanceId, int Revision, string RequestId,
        Dictionary<string, JsonElement>? Args, string[] Errors);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(BrowserWidgetDiagnostic[]))]
    [JsonSerializable(typeof(StoryArgDefinition[]))]
    [JsonSerializable(typeof(StoryLogEntry))]
    [JsonSerializable(typeof(SetArgsResponse))]
    private sealed partial class BrowserJsonContext : JsonSerializerContext;

    [JSImport("nextFrame", "luxel-browser-host")] private static partial Task<double> NextFrame();
    [JSImport("setStatus", "luxel-browser-host")] private static partial void SetStatus(string state, string summary);
    [JSImport("setReady", "luxel-browser-host")] private static partial void SetReady(string summary, string argsJson, string schemaJson);
    [JSImport("publishArgsChanged", "luxel-browser-host")] private static partial void PublishArgsChanged(string argsJson);
    [JSImport("publishEvent", "luxel-browser-host")] private static partial void PublishEvent(string entryJson);
    [JSImport("publishDiagnostics", "luxel-browser-host")] private static partial void PublishDiagnostics(string widgetsJson);
    [JSImport("publishWebGpuDiagnostics", "luxel-browser-host")] private static partial void PublishWebGpuDiagnostics(string diagnosticsJson);
    [JSImport("publishFrame", "luxel-browser-host")] private static partial void PublishFrame(int revision);
}
