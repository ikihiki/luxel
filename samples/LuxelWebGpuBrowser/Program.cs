using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Luxel.Controls;
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

    public static async Task Main()
    {
        string app = GetApp();
        try
        {
            switch (app)
            {
                case "triangle": await RunTriangle(); break;
                case "counter": await RunCounter(); break;
                default: throw new InvalidOperationException($"Unknown browser app '{app}'. Expected triangle or counter.");
            }
        }
        catch (Exception ex)
        {
            SetStatus("fail", $"browser-webgpu: status=fail, app={app}, error={ex.Message}");
            Console.Error.WriteLine(ex);
            throw;
        }
    }

    private static async Task RunCounter()
    {
        using WebWindowBackend web = await CreateWindowBackend();
        using var windows = new WindowSystem(web);
        Window window = windows.CreateWindow(new WindowDesc("Luxel Counter", 640, 360));
        windows.Pump();
        using BrowserWebGpuBackend backend = await BrowserWebGpuBackend.CreateAsync();
        using var device = new GpuDevice(backend);
        using GpuSurface surface = device.CreateCanvasSurface("#luxel-canvas", (uint)window.Width, (uint)window.Height);
        using var raster = new GpuDeviceRasterizer2D(device, RasterShader);
        using var font = new VectorFont(Resource("BIZUDGothic-Regular.ttf"));
        using var canvas = new RetainedCanvas();
        using IRasterScene2D scene = raster.CreateScene(canvas);
        CanonicalCounterRecipe.Result recipe = CanonicalCounterRecipe.Build();
        using var ui = new UiHost(canvas, font, window.Width, window.Height, gpuRasterizer: raster);
        ui.SetRoot(recipe.Root);

        GpuBuffer framebuffer = device.Malloc(checked((ulong)window.Width * (uint)window.Height * 4), GpuMemoryKind.DeviceLocal);
        bool resizePending = false;
        int resizeWidth = window.Width, resizeHeight = window.Height;
        int pointerDownCount = 0, pointerUpCount = 0, renderRevision = 0, presentedCount = 0;
        window.Resized += (w, h) => { resizePending = true; resizeWidth = Math.Max(1, w); resizeHeight = Math.Max(1, h); };
        window.PointerMoved += e => ui.PointerMove(e.X, e.Y);
        window.PointerDown += e => { pointerDownCount++; ui.PointerDown(e.X, e.Y, MapButton(e.Button)); PublishCounterState(); };
        window.PointerUp += e => { pointerUpCount++; ui.PointerUp(e.X, e.Y, MapButton(e.Button)); PublishCounterState(); };

        void PublishCounterState() => SetCounterState(recipe.Count.Value, renderRevision, presentedCount,
            pointerDownCount, pointerUpCount,
            recipe.Minus.WorldPos.X, recipe.Minus.WorldPos.Y, recipe.Minus.Size.Width, recipe.Minus.Size.Height,
            recipe.Plus.WorldPos.X, recipe.Plus.WorldPos.Y, recipe.Plus.Size.Width, recipe.Plus.Size.Height);

        async Task RenderAsync()
        {
            using GpuCommandBuffer command = device.MainQueue.StartCommandRecording();
            scene.Render(Camera2D.Pixels, new GpuRasterTarget2D(command, framebuffer, (uint)window.Width, (uint)window.Height));
            command.Finish();
            await device.MainQueue.SubmitAsync(command);
            surface.Present(framebuffer, (uint)window.Width, (uint)window.Width, (uint)window.Height);
            renderRevision++;
            presentedCount = recipe.Count.Value;
            PublishCounterState();
        }

        await RenderAsync();
        SetStatus("pass", $"browser-webgpu: status=pass\nstory={CanonicalCounterRecipe.Story}\napp=counter\ncount=0\nrecipe={CanonicalCounterRecipe.Recipe}\ndevice={device.Name}");
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
            if (resizePending || canvas.HasPendingChanges) await RenderAsync();
            await NextFrame();
        }
        framebuffer.Dispose();
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
        SetStatus("pass", $"browser-webgpu: status=pass\nstory={CanonicalTriangleRecipe.Story}\napp=triangle\nshader={CanonicalTriangleRecipe.Shader}\nvertexSize={CanonicalTriangleRecipe.VertexSize}; rootSize={CanonicalTriangleRecipe.DrawArgsSize}\ncanvas={width}x{height}\nrecipe={CanonicalTriangleRecipe.Recipe}\nhash={CanonicalTriangleRecipe.ShaderSha256}\ndevice={gpu.Name}\ncompute=0x{computeValue:x8}; center=rgba({red},{green},{blue},{alpha})\nframes=1+; resize={resizeEvents}; pointer={pointerEvents}; key={keyEvents}");
        while (windows.Pump()) { if (resizePending) { surface.Resize((uint)window.Width, (uint)window.Height); surface.Present(pixels, width, width, height); resizePending = false; } await NextFrame(); }
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
    private readonly record struct ComputeRoot(uint BufferIndex, uint Value, uint Pad0 = 0, uint Pad1 = 0);

    [JSImport("getApp", "luxel-browser-host")] private static partial string GetApp();
    [JSImport("nextFrame", "luxel-browser-host")] private static partial Task<double> NextFrame();
    [JSImport("setStatus", "luxel-browser-host")] private static partial void SetStatus(string state, string summary);
    [JSImport("setCounterState", "luxel-browser-host")] private static partial void SetCounterState(int count, int renderRevision, int presentedCount, int pointerDownCount, int pointerUpCount, float minusX, float minusY, float minusW, float minusH, float plusX, float plusY, float plusW, float plusH);
}
