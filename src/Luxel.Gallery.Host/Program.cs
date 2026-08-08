using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Luxel;
using Luxel.DevTools;
using Luxel.Diagnostics;
using Luxel.Gallery;
using Luxel.Platform;
using Luxel.Framework.UI;
using Luxel.Graphics.TwoD;
using Luxel.Graphics.TwoD.Skia;
using Luxel.Typography;
using Luxel.UI;

// 使い方:
//   dotnet run --project src/Luxel.Gallery.Host -- [auto|vk|dx] [port] [seconds]   ネイティブ app (環境自動検出, 既定 port=5180, 常駐)
//   dotnet run --project src/Luxel.Gallery.Host -- <backend> e2e [--update]     play + golden 回帰 (offscreen)
//   backend: auto (既定) | vk | dx | webgpu
// リモート検証 (AI): DevTools — GET /windows /winframe?id=1 /trees, POST /cmd
//   (UI 入力は {op, ui:"gallery"|"story", x, y}、ウィンドウ操作は window.*)
HostApplicationBuilder storyHostBuilder = Host.CreateApplicationBuilder(args);
storyHostBuilder.Services.AddGalleryStory();
using IHost storyHost = storyHostBuilder.Build();
StoryCatalog catalog = storyHost.Services.GetRequiredService<StoryCatalog>();
string backend = (args.Length > 0 ? args[0] : "auto").ToLowerInvariant();

string rasterizerBackend = args.SkipWhile(a => a != "--rasterizer").Skip(1).FirstOrDefault()?.ToLowerInvariant() ?? "gpu";
if (rasterizerBackend is not ("gpu" or "skia"))
    throw new ArgumentException($"未知の2D rasterizer: {rasterizerBackend} (gpu / skia)");

GpuDevice CreateDevice() => backend switch
{
    "auto" when OperatingSystem.IsWindows() => new GpuDevice(Luxel.Graphics.DirectX12.D3D12Backend.Create()),
    "auto" => new GpuDevice(Luxel.Graphics.Vulkan.VulkanBackend.Create()),
    "vk" or "vulkan" => new GpuDevice(Luxel.Graphics.Vulkan.VulkanBackend.Create()),
    "dx" or "d3d12" => new GpuDevice(Luxel.Graphics.DirectX12.D3D12Backend.Create()),
    "webgpu" or "wgpu" => new GpuDevice(Luxel.Graphics.WebGPU.WebGpuBackend.Create()),
    _ => throw new ArgumentException($"未知のバックエンド: {backend} (vk / dx / webgpu)"),
};

if (args.Length > 1 && args[1] is "e2e" or "snap")
{
    if (args[1] == "snap")
        Console.WriteLine("snap は廃止されました — e2e (ctx.Play + d.Snap) として実行します。");
    using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);   // 同梱フォント (日本語 + マシン非依存)
    string? filter = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--") && a is not "gpu" and not "skia");
    if (rasterizerBackend == "skia")
    {
        Console.WriteLine("=== Luxel.Gallery e2e with SkiaSharp CPU rasterizer ===");
        using var rasterizer = new SkiaRasterizer2D();
        using var host = new GalleryHost(rasterizer, font, catalog);
        return E2e.Run(host, catalog.All, "skia", args.Contains("--update"), filter, args.Contains("--times"));
    }
    using GpuDevice device = CreateDevice();
    Console.WriteLine($"=== Luxel.Gallery e2e on '{backend}' (device: {device.Name}) ===");
    using var gpuHost = new GalleryHost(device, font, catalog);
    return E2e.Run(gpuHost, catalog.All, backend, args.Contains("--update"), filter, args.Contains("--times"));
}

// canvas 更新コストのマイクロベンチ: -- vk bench <story> [frames] [--type] [--click x y] [--wheel d]
if (args.Length > 2 && args[1] == "bench")
{
    if (rasterizerBackend != "gpu")
        throw new NotSupportedException("既存benchはGPU upload/readbackを計測するため --rasterizer gpu 専用です。");
    using GpuDevice device = CreateDevice();
    Console.WriteLine($"=== Luxel.Gallery bench on '{backend}' (device: {device.Name}) ===");
    using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);   // 同梱フォント
    using var host = new GalleryHost(device, font, catalog);
    int frames = args.Length > 3 && int.TryParse(args[3], out int f) ? f : 300;
    (float x, float y)? click = null;
    int ci = Array.IndexOf(args, "--click");
    if (ci >= 0 && ci + 2 < args.Length
        && float.TryParse(args[ci + 1], out float cx) && float.TryParse(args[ci + 2], out float cy))
        click = (cx, cy);
    float wheel = 0;
    int wi = Array.IndexOf(args, "--wheel");
    if (wi >= 0 && wi + 1 < args.Length && float.TryParse(args[wi + 1], out float wd)) wheel = wd;
    return Bench.Run(host, args[2], frames, args.Contains("--type"), click, wheel);
}

int port = args.Length > 1 && int.TryParse(args[1], out int p) ? p : 5180;
int seconds = args.Length > 2 && int.TryParse(args[2], out int s) ? s : 0;   // 0 = 常駐

var gallery = new GalleryApp(catalog);
bool storyRegistered = false;
LuxelAppBuilder builder = LuxelApp.CreateBuilder(args);
builder.Options.Title = "Luxel Gallery";
builder.Options.UiName = "gallery";
builder.Options.Width = 1280;
builder.Options.Height = 840;
builder.Options.Theme = Theme.Light.Compact();
builder.Options.FontFactory = () => GalleryFonts.Load(GalleryFonts.Regular);
builder.Options.RunDuration = seconds > 0 ? TimeSpan.FromSeconds(seconds) : null;
builder.Options.GraphicsBackend = backend switch
{
    "auto" => LuxelGraphicsBackend.Auto,
    "vk" or "vulkan" => LuxelGraphicsBackend.Vulkan,
    "dx" or "d3d12" => LuxelGraphicsBackend.Direct3D12,
    "webgpu" or "wgpu" => LuxelGraphicsBackend.WebGpu,
    _ => throw new ArgumentException($"未知のバックエンド: {backend} (auto / vk / dx / webgpu)"),
};
builder.ConfigureRuntime(runtime =>
{
    runtime.Own(gallery);
    gallery.HostGpu = (runtime.Device, runtime.Font);
});
builder.OnStarted(runtime =>
{
    Console.WriteLine($"=== Luxel.Gallery app (device: {runtime.Device.Name}) ===");
    if (runtime.MainWindow.Content is UiContent content)
        content.Host.RegisterShortcut(new KeyGesture(Key.D, Ctrl: true), gallery.ToggleTheme);
    gallery.SelectByPath("Start/Welcome");
    runtime.Commands.Register("story.select", value =>
    {
        if (value is System.Text.Json.JsonElement element && element.TryGetProperty("id", out var id))
            gallery.SelectByPath(id.GetString() ?? "");
    });

    DevToolsListener listener = runtime.Own(new DevToolsListener(runtime.Commands));
    var server = runtime.Own(new DebugServer(listener, port, windows: runtime.WindowManager));
    server.Start();
    Console.WriteLine($"Gallery URL: {server.Url} (stories: {catalog.All.Count})");
});
builder.OnFrame((runtime, _) =>
{
    gallery.Update();
    if (runtime.MainWindow.Content is not UiContent content) return;
    gallery.SetWindowSize(content.Host.Width, content.Host.Height);
    if (gallery.ConsumeDirty()) content.Host.SetRoot(gallery.BuildRoot());
    if (!storyRegistered && gallery.StoryHost is { } storyHost)
    {
        runtime.WindowManager.UiRegistry.Register("story", storyHost);
        storyRegistered = true;
    }
});

LuxelUiApplication app = builder.Build();
app.MapScreen("/", gallery.BuildRoot);
app.Run();
Console.WriteLine("gallery: shutting down");
return 0;
