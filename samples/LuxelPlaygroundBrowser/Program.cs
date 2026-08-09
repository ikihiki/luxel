using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Luxel.AssetsGpu;
using Luxel.Controls;
using Luxel.Graphics;
using Luxel.Graphics.TwoD;
using Luxel.Graphics.WebGPU.Browser;
using Luxel.Imaging;
using Luxel.Platform;
using Luxel.Platform.Abstraction;
using Luxel.Platform.Web;
using Luxel.Resources;
using Luxel.Resources.Browser;
using Luxel.Scripting.Roslyn.Web;
using Luxel.Shaders;
using Luxel.Shaders.Slang.Browser;
using Luxel.Typography;
using Luxel.UI;

namespace LuxelPlaygroundBrowser;

[SupportedOSPlatform("browser")]
public static partial class Program
{
    private static readonly WebScriptExecutor Executor = new();
    private static WebScriptCompiler? _compiler;
    private static WebScriptLanguageService? _languageService;
    private static Func<BrowserWorkspaceSnapshot, BrowserRunResources>? _runResourceFactory;
    private static BrowserRunResources? _publishedRunResources;
    private static UiHost? _ui;
    private static readonly SemaphoreSlim RunGate = new(1, 1);
    private static CancellationTokenSource? _runCancellation;
    private static int _latestRevision;
    private static int _runGeneration;
    private static PendingRender? _pendingRender;

    public static async Task Main()
    {
        try
        {
            MetadataReferenceImage[] references = await LoadMetadataReferences();
            if (GetMode() == "language")
            {
                _languageService = new WebScriptLanguageService(references);
                SetLanguageReady();
                return;
            }
            _compiler = new WebScriptCompiler(references);
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
        if (!TryBeginRun(revision, out int generation, out CancellationTokenSource cancellation))
            return Serialize(new RunResponse("runtime-error", [], new FailureResponse("protocol", "The source revision is not newer than the active revision.", null, null)));
        CancellationToken token = cancellation.Token;

        try
        {
            WebScriptOutput.SetSink(message => { if (IsCurrentRun(generation, token)) PublishLog(revision, "information", message); });
            WebScriptCompilation compilation = _compiler.Compile(source, $"Luxel.Playground.Script.{revision}");
            ThrowIfStale(generation, token);
            DiagnosticResponse[] diagnostics = compilation.Diagnostics.Select(MapDiagnostic).ToArray();
            if (!compilation.Success || compilation.PeImage is null)
                return Serialize(new RunResponse("diagnostics", diagnostics, null));

            using IDisposable resourceScope = WebScriptResources.Push(null);
            WebScriptExecution execution = Executor.Execute(compilation.PeImage, compilation.PdbImage ?? []);
            ThrowIfStale(generation, token);
            if (!execution.Success || execution.Widget is null)
            {
                WebScriptFailure failure = execution.Failure ?? new WebScriptFailure("runtime", "Script execution failed.");
                return Serialize(new RunResponse(
                    "runtime-error",
                    diagnostics,
                    new FailureResponse(failure.Kind, failure.Message, failure.ExceptionType, failure.Line)));
            }

            ThrowIfStale(generation, token);
            Interlocked.Exchange(ref _pendingRender, new PendingRender(revision, generation, execution.Widget));
            return Serialize(new RunResponse("render-pending", diagnostics, null));
        }
        catch (OperationCanceledException)
        {
            return Serialize(new RunResponse("canceled", [], null));
        }
        catch (Exception exception)
        {
            return Serialize(new RunResponse(
                "runtime-error",
                [],
                new FailureResponse("infrastructure", exception.Message, exception.GetType().FullName, null)));
        }
        finally
        {
            CompleteRun(cancellation);
        }
    }

    [JSExport]
    public static async Task<string> RunProject(string projectJson, int revision)
    {
        if (_compiler is null || _ui is null || _runResourceFactory is null)
            return Serialize(new RunResponse("runtime-error", [], new FailureResponse("infrastructure", "The playground runtime is not ready.", null, null)));
        if (!TryBeginRun(revision, out int generation, out CancellationTokenSource cancellation))
            return Serialize(new RunResponse("runtime-error", [], new FailureResponse("protocol", "The workspace revision is not newer than the active revision.", null, null)));
        CancellationToken token = cancellation.Token;

        BrowserRunResources? runResources = null;
        bool enteredGate = false;
        try
        {
            await RunGate.WaitAsync(token);
            enteredGate = true;
            ThrowIfStale(generation, token);

            BrowserWorkspaceSnapshot workspace = BrowserWorkspaceSnapshot.Parse(projectJson);
            runResources = _runResourceFactory(workspace);
            ThrowIfStale(generation, token);
            DiagnosticResponse[] shaderDiagnostics = await CompileWorkspaceShaders(workspace, runResources, generation, token);
            ThrowIfStale(generation, token);
            if (shaderDiagnostics.Any(diagnostic => diagnostic.Severity == "error"))
                return Serialize(new RunResponse("diagnostics", shaderDiagnostics, null));

            WebScriptProject project = workspace.ToWebScriptProject();
            WebScriptOutput.SetSink(message => { if (IsCurrentRun(generation, token)) PublishLog(revision, "information", message); });
            WebScriptCompilation compilation = _compiler.Compile(project, $"Luxel.Playground.Script.{revision}");
            ThrowIfStale(generation, token);
            DiagnosticResponse[] diagnostics = [.. shaderDiagnostics, .. compilation.Diagnostics.Select(MapDiagnostic)];
            if (!compilation.Success || compilation.PeImage is null)
                return Serialize(new RunResponse("diagnostics", diagnostics, null));

            using IDisposable resourceScope = WebScriptResources.Push(runResources.ScriptResources);
            WebScriptExecution execution = Executor.Execute(compilation.PeImage, compilation.PdbImage ?? []);
            ThrowIfStale(generation, token);
            if (!execution.Success || execution.Widget is null)
            {
                WebScriptFailure failure = execution.Failure ?? new WebScriptFailure("runtime", "Script execution failed.");
                return Serialize(new RunResponse(
                    "runtime-error",
                    diagnostics,
                    new FailureResponse(failure.Kind, failure.Message, failure.ExceptionType, failure.Line, failure.FileName)));
            }

            ThrowIfStale(generation, token);
            BrowserRunResources? previous = _publishedRunResources;
            _publishedRunResources = runResources;
            runResources = null;
            previous?.Dispose();
            Interlocked.Exchange(ref _pendingRender, new PendingRender(revision, generation, execution.Widget));
            return Serialize(new RunResponse("render-pending", diagnostics, null));
        }
        catch (OperationCanceledException)
        {
            return Serialize(new RunResponse("canceled", [], null));
        }
        catch (Exception exception)
        {
            return Serialize(new RunResponse(
                "runtime-error",
                [],
                new FailureResponse("infrastructure", exception.Message, exception.GetType().FullName, null, null)));
        }
        finally
        {
            runResources?.Dispose();
            if (enteredGate) RunGate.Release();
            CompleteRun(cancellation);
        }
    }

    [JSExport]
    public static void Cancel(int revision)
    {
        if (revision != _latestRevision) return;
        Interlocked.Increment(ref _runGeneration);
        Interlocked.Exchange(ref _pendingRender, null);
        CancellationTokenSource? cancellation = Interlocked.Exchange(ref _runCancellation, null);
        cancellation?.Cancel();
    }

    [JSExport]
    public static async Task<string> Complete(string source, int position, int revision)
        => _languageService is null
            ? SerializeError("The Playground language service is not ready.")
            : JsonSerializer.Serialize(await _languageService.CompleteAsync(source, position, revision), JsonOptions);

    [JSExport]
    public static async Task<string> Hover(string source, int position, int revision)
        => _languageService is null
            ? SerializeError("The Playground language service is not ready.")
            : JsonSerializer.Serialize(await _languageService.HoverAsync(source, position, revision), JsonOptions);

    [JSExport]
    public static async Task<string> Format(string source, int revision)
        => _languageService is null
            ? SerializeError("The Playground language service is not ready.")
            : JsonSerializer.Serialize(await _languageService.FormatAsync(source, revision), JsonOptions);

    [JSExport]
    public static async Task<string> Analyze(string source, int revision)
        => _languageService is null
            ? SerializeError("The Playground language service is not ready.")
            : JsonSerializer.Serialize(await _languageService.AnalyzeAsync(source, revision), JsonOptions);

    [JSExport]
    public static async Task<string> CompleteProject(string projectJson, string fileId, int position, int revision)
    {
        if (_languageService is null) return SerializeError("The Playground language service is not ready.");
        BrowserWorkspaceSnapshot workspace = BrowserWorkspaceSnapshot.Parse(projectJson, revision);
        BrowserWorkspaceFile file = workspace.File(fileId);
        return JsonSerializer.Serialize(await _languageService.CompleteAsync(
            workspace.ToWebScriptProject(), file.Path, position, revision), JsonOptions);
    }

    [JSExport]
    public static async Task<string> HoverProject(string projectJson, string fileId, int position, int revision)
    {
        if (_languageService is null) return SerializeError("The Playground language service is not ready.");
        BrowserWorkspaceSnapshot workspace = BrowserWorkspaceSnapshot.Parse(projectJson, revision);
        BrowserWorkspaceFile file = workspace.File(fileId);
        return JsonSerializer.Serialize(await _languageService.HoverAsync(
            workspace.ToWebScriptProject(), file.Path, position, revision), JsonOptions);
    }

    [JSExport]
    public static async Task<string> FormatProject(string projectJson, string fileId, int revision)
    {
        if (_languageService is null) return SerializeError("The Playground language service is not ready.");
        BrowserWorkspaceSnapshot workspace = BrowserWorkspaceSnapshot.Parse(projectJson, revision);
        BrowserWorkspaceFile file = workspace.File(fileId);
        return JsonSerializer.Serialize(await _languageService.FormatAsync(
            workspace.ToWebScriptProject(), file.Path, revision), JsonOptions);
    }

    [JSExport]
    public static async Task<string> AnalyzeProject(string projectJson, string fileId, int revision)
    {
        if (_languageService is null) return SerializeError("The Playground language service is not ready.");
        BrowserWorkspaceSnapshot workspace = BrowserWorkspaceSnapshot.Parse(projectJson, revision);
        _ = workspace.File(fileId);
        return JsonSerializer.Serialize(await _languageService.AnalyzeAsync(
            workspace.ToWebScriptProject(), revision), JsonOptions);
    }

    private static string SerializeError(string message)
        => JsonSerializer.Serialize(new { error = message }, JsonOptions);

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
            BrowserWebGpuBackend backend = await BrowserWebGpuBackend.CreateAsync();
            using var device = new GpuDevice(backend);
            using var resourceHttp = new HttpClient { BaseAddress = new Uri(GetBaseUrl(), UriKind.Absolute) };
            await using var slangCompiler = new BrowserSlangCompiler();
            _runResourceFactory = workspace => new BrowserRunResources(workspace, resourceHttp, slangCompiler, device);
            var browserBackend = (Luxel.Graphics.WebGPU.Browser.BrowserWebGpuBackend)device.Backend;
            using GpuSurface surface = browserBackend.CreateCanvasSurface("#luxel-canvas", (uint)window.Width, (uint)window.Height);
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

            async Task RenderAsync(PendingRender? pending = null)
            {
                using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
                scene.Render(Camera2D.Pixels, new GpuRasterTarget2D(command, framebuffer, (uint)window.Width, (uint)window.Height));
                command.Finish();
                await device.MainQueue.SubmitAsync(command);
                surface.Present(framebuffer, (uint)window.Width, (uint)window.Width, (uint)window.Height);
                if (pending is not null)
                {
                    await NextFrame();
                    if (pending.Generation == Volatile.Read(ref _runGeneration))
                        PublishFirstFrame(pending.Revision);
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
                PendingRender? pending = Interlocked.Exchange(ref _pendingRender, null);
                if (pending is not null && pending.Generation == Volatile.Read(ref _runGeneration))
                    ui.SetRoot(pending.Widget);
                else
                    pending = null;
                _publishedRunResources?.Resources.Pump();
                ui.Tick(1f / 60f);
                if (canvas.HasPendingChanges) await RenderAsync(pending);
                await NextFrame();
            }
            framebuffer.Dispose();
        }
        finally
        {
            Interlocked.Exchange(ref _runCancellation, null)?.Cancel();
            _publishedRunResources?.Dispose();
            _publishedRunResources = null;
            _runResourceFactory = null;
            _ui = null;
            PlatformClipboard.Current = null;
        }
    }

    private static async Task<DiagnosticResponse[]> CompileWorkspaceShaders(
        BrowserWorkspaceSnapshot workspace,
        BrowserRunResources runResources,
        int generation,
        CancellationToken token)
    {
        var diagnostics = new List<DiagnosticResponse>();
        foreach (BrowserWorkspaceFile file in workspace.Files.Where(file =>
                     file.Language == "slang" && Path.GetExtension(file.Path).Equals(".slang", StringComparison.OrdinalIgnoreCase)))
        {
            string selector = IsGraphicsShader(file.Source) ? "graphics" : "compute";
            ThrowIfStale(generation, token);
            ResourceHandle<GpuShaderCode> handle = runResources.Resources.Load<GpuShaderCode>($"workspace://{file.Path}#{selector}");
            runResources.Handles.Add(handle);
            try
            {
                await handle.Ready.WaitAsync(token);
                ThrowIfStale(generation, token);
                runResources.ScriptResources.Add(
                    file.Path,
                    handle.Value,
                    new WebScriptResourceMetadata(
                        handle.Uri.ToString(),
                        file.Path,
                        selector,
                        typeof(GpuShaderCode).FullName ?? nameof(GpuShaderCode),
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["workspaceRevision"] = workspace.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["fileVersion"] = file.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["programKind"] = selector,
                            ["target"] = "wgsl",
                        }));
            }
            catch (ShaderCompilationException exception)
            {
                if (exception.Diagnostics.Count == 0)
                {
                    diagnostics.Add(new DiagnosticResponse("SLANG", exception.Message, "error", null, null, 1, file.Path));
                }
                else
                {
                    diagnostics.AddRange(exception.Diagnostics.Select(diagnostic => new DiagnosticResponse(
                        diagnostic.Code ?? "SLANG",
                        diagnostic.Message,
                        diagnostic.Severity.ToString().ToLowerInvariant(),
                        diagnostic.Line,
                        diagnostic.Column,
                        1,
                        diagnostic.Path ?? file.Path)));
                }
            }
        }
        runResources.Resources.Pump();
        ThrowIfStale(generation, token);
        return diagnostics.ToArray();
    }

    private static bool TryBeginRun(int revision, out int generation, out CancellationTokenSource cancellation)
    {
        if (revision <= Volatile.Read(ref _latestRevision))
        {
            generation = 0;
            cancellation = null!;
            return false;
        }

        Volatile.Write(ref _latestRevision, revision);
        generation = Interlocked.Increment(ref _runGeneration);
        Interlocked.Exchange(ref _pendingRender, null);
        cancellation = new CancellationTokenSource();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _runCancellation, cancellation);
        previous?.Cancel();
        return true;
    }

    private static void CompleteRun(CancellationTokenSource cancellation)
    {
        Interlocked.CompareExchange(ref _runCancellation, null, cancellation);
        cancellation.Dispose();
    }

    private static bool IsCurrentRun(int generation, CancellationToken token)
        => !token.IsCancellationRequested && generation == Volatile.Read(ref _runGeneration);

    private static void ThrowIfStale(int generation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (generation != Volatile.Read(ref _runGeneration)) throw new OperationCanceledException(token);
    }

    private static bool IsGraphicsShader(string source)
        => (source.Contains("[shader(\"vertex\")]", StringComparison.Ordinal)
                && source.Contains("[shader(\"fragment\")]", StringComparison.Ordinal))
            || (source.Contains("vsMain", StringComparison.Ordinal) && source.Contains("psMain", StringComparison.Ordinal));

    private static DiagnosticResponse MapDiagnostic(WebScriptDiagnostic diagnostic) => new(
        diagnostic.Id,
        diagnostic.Message,
        diagnostic.Severity.ToString().ToLowerInvariant(),
        diagnostic.Line,
        diagnostic.Column,
        diagnostic.Length,
        diagnostic.FileName);

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

    private sealed class BrowserRunResources : IDisposable
    {
        public BrowserRunResources(
            BrowserWorkspaceSnapshot snapshot,
            HttpClient http,
            ISlangCompiler slangCompiler,
            GpuDevice device)
        {
            Workspace = new WorkspaceFileSystem();
            Workspace.ApplyBatch(snapshot.Files.Select(file => (WorkspaceFileOperation)new WorkspaceSetOperation(
                file.Path, Encoding.UTF8.GetBytes(file.Source))).ToArray());
            var builder = new ResourceSystemBuilder();
            ResourceSystemDefaultHandles defaults = builder.AddBrowserCore();
            builder.Sources.Add(new WorkspaceSource(Workspace)).RunOn(defaults.IoDomain).ManagedBy(defaults.IoManager).Register();
            builder.Sources.Add(new HttpSource(http)).RunOn(defaults.IoDomain).ManagedBy(defaults.IoManager).Register();
            builder.Steps.Add<byte[], CpuImage>(new TexDecoder()).RunOn(defaults.CpuDomain).ManagedBy(defaults.CpuManager).Register();
            builder.Steps.Add<byte[], CpuImage>(new ImageSharpDecoder()).RunOn(defaults.CpuDomain).ManagedBy(defaults.CpuManager).Register();
            builder.Steps.Add<byte[], SlangSource>(new WorkspaceSlangSourceStep(Workspace)).RunOn(defaults.CpuDomain).ManagedBy(defaults.CpuManager).Register();
            builder.Steps.Add<SlangSource, GpuShaderCode>(new SlangCompileStep(slangCompiler, GpuBackendKind.WebGpu))
                .RunOn(defaults.CpuDomain).ManagedBy(defaults.CpuManager).Register();
            builder.AddAssetGpu(device, options => options.ConfigureDomain = domain => domain.UseBrowserOwnerContext());
            Resources = builder.Build();
            Resources.Watch();
            Resources.Pump();
        }

        public WorkspaceFileSystem Workspace { get; }
        public ResourceSystem Resources { get; }
        public BrowserScriptResourceProvider ScriptResources { get; } = new();
        public List<IDisposable> Handles { get; } = [];

        public void Dispose()
        {
            foreach (IDisposable handle in Handles) handle.Dispose();
            Handles.Clear();
            ScriptResources.Clear();
            Resources.Dispose();
        }
    }

    private sealed record PendingRender(int Revision, int Generation, Widget Widget);

    private sealed class BrowserScriptResourceProvider : IWebScriptResourceProvider
    {
        private readonly Dictionary<(Type Type, string Name), object> _resources = new();

        public void Add<T>(string name, T value, WebScriptResourceMetadata metadata) where T : notnull
        {
            var resource = new WebScriptResource<T>(value, metadata);
            _resources[(typeof(T), name)] = resource;
            _resources[(typeof(T), metadata.Uri)] = resource;
        }

        public bool TryGet<T>(string name, out WebScriptResource<T>? resource)
        {
            if (_resources.TryGetValue((typeof(T), name), out object? value))
            {
                resource = (WebScriptResource<T>)value;
                return true;
            }
            resource = null;
            return false;
        }

        public void Clear() => _resources.Clear();
    }

    private sealed record ReferenceManifest(int Version, string[] Assemblies);
    private sealed record DiagnosticResponse(string Id, string Message, string Severity, int? Line, int? Column, int Length, string? FileName = null);
    private sealed record FailureResponse(string Kind, string Message, string? ExceptionType, int? Line, string? FileName = null);
    private sealed record RunResponse(string Outcome, DiagnosticResponse[] Diagnostics, FailureResponse? Failure);

    [JSImport("getMode", "luxel-playground-host")] private static partial string GetMode();
    [JSImport("setLanguageReady", "luxel-playground-host")] private static partial void SetLanguageReady();
    [JSImport("getBaseUrl", "luxel-playground-host")] private static partial string GetBaseUrl();
    [JSImport("nextFrame", "luxel-playground-host")] private static partial Task<double> NextFrame();
    [JSImport("setReady", "luxel-playground-host")] private static partial void SetReady(string deviceName);
    [JSImport("setFatalError", "luxel-playground-host")] private static partial void SetFatalError(string error);
    [JSImport("publishLog", "luxel-playground-host")] private static partial void PublishLog(int revision, string level, string message);
    [JSImport("publishFirstFrame", "luxel-playground-host")] private static partial void PublishFirstFrame(int revision);
}
