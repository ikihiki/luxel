using Luxel;
using Luxel.DevTools;
using Luxel.Diagnostics;
using Luxel.Gallery;
using Luxel.Platform;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;

// 使い方:
//   dotnet run --project src/Luxel.Gallery -- <backend> [port] [seconds]   ネイティブ app (実ウィンドウ, 既定 port=5180, 常駐)
//   dotnet run --project src/Luxel.Gallery -- <backend> snap [--update]    全ストーリーのスナップショット回帰 (offscreen)
//   backend: vk | dx
// リモート検証 (AI): DevTools — GET /windows /winframe?id=1 /trees, POST /cmd
//   (UI 入力は {op, ui:"gallery"|"story", x, y}、ウィンドウ操作は window.*)
string backend = (args.Length > 0 ? args[0] : "vk").ToLowerInvariant();

GpuDevice CreateDevice() => backend switch
{
    "vk" or "vulkan" => new GpuDevice(Luxel.Vulkan.VulkanBackend.Create()),
    "dx" or "d3d12" => new GpuDevice(Luxel.D3D12.D3D12Backend.Create()),
    _ => throw new ArgumentException($"未知のバックエンド: {backend} (vk / dx)"),
};

if (args.Length > 1 && args[1] is "e2e" or "snap")
{
    if (args[1] == "snap")
        Console.WriteLine("snap は廃止されました — e2e (ctx.Play + d.Snap) として実行します。");
    using GpuDevice device = CreateDevice();
    Console.WriteLine($"=== Luxel.Gallery e2e on '{backend}' (device: {device.Name}) ===");
    using VectorFont font = VectorFont.LoadSystem();
    using var host = new GalleryHost(device, font);
    // フラグ以外の残り引数 = テスト名フィルタ (部分一致、"パス#play名")
    string? filter = args.Skip(2).FirstOrDefault(a => !a.StartsWith("--"));
    return E2e.Run(host, StoryRegistry.All, backend, args.Contains("--update"), filter, args.Contains("--times"));
}

// canvas 更新コストのマイクロベンチ: -- vk bench <story> [frames] [--type] [--click x y] [--wheel d]
if (args.Length > 2 && args[1] == "bench")
{
    using GpuDevice device = CreateDevice();
    Console.WriteLine($"=== Luxel.Gallery bench on '{backend}' (device: {device.Name}) ===");
    using VectorFont font = VectorFont.LoadSystem();
    using var host = new GalleryHost(device, font);
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

int exit = 1;
var t = new Thread(() => exit = RunApp(CreateDevice, port, seconds));
t.SetApartmentState(ApartmentState.STA);   // 実ウィンドウ/TSF は STA
t.Start();
t.Join();
return exit;

static int RunApp(Func<GpuDevice> createDevice, int port, int seconds)
{
    using GpuDevice device = createDevice();
    Console.WriteLine($"=== Luxel.Gallery app (device: {device.Name}) ===");
    UiTheme.Current.Value = Theme.Light.Compact();   // ギャラリーは高密度テーマ (snap は既定テーマのまま)
    using VectorFont font = VectorFont.LoadSystemJapanese();
    using var windows = new WindowSystem(Win32WindowBackend.Create());

    var cmds = new EngineCommands();
    using var listener = new DevToolsListener(cmds);
    using var manager = new WindowManager(device, font, windows, cmds);
    using var app = new GalleryApp { HostGpu = (device, font) };   // 実窓専用ストーリーの GPU 窓口

    WindowHost galleryWin = manager.CreateUiWindow(new Luxel.Abstraction.WindowDesc("Luxel Gallery", 1280, 840), "gallery", app.BuildRoot);
    // アプリ全域ショートカット (AP-M5): フォーカス中コントロールが消費しないキーだけ届く
    if (galleryWin.Content is UiContent gc)
        gc.Host.RegisterShortcut(new KeyGesture(Key.D, Ctrl: true), app.ToggleTheme);
    if (StoryRegistry.All.Count > 0) app.Select(StoryRegistry.All[0]);   // 初期選択
    cmds.Register("story.select", a =>
    {
        if (a is System.Text.Json.JsonElement el && el.TryGetProperty("id", out var id))
            app.SelectByPath(id.GetString() ?? "");
    });

    using var server = new DebugServer(listener, port, windows: manager);
    server.Start();
    Console.WriteLine($"Gallery URL: {server.Url} (stories: {StoryRegistry.All.Count})");

    var sw = System.Diagnostics.Stopwatch.StartNew();
    long last = 0;
    bool storyRegistered = false;
    while (true)
    {
        long now = sw.ElapsedMilliseconds;
        float dt = Math.Min(0.1f, (now - last) / 1000f);
        last = now;
        if (!manager.RunFrame(dt)) break;
        app.Update();   // Log パネル同期
        // ウィンドウの論理サイズを chrome へ同期 — リサイズで表示範囲が追従する (変化時のみ dirty)
        if (manager.Hosts.Count > 0 && manager.Hosts[0].Content is UiContent gsz)
            app.SetWindowSize(gsz.Host.Width, gsz.Host.Height);
        // ストーリー選択/リサイズで chrome を再構築 (SurfaceView/story は生存)
        if (app.ConsumeDirty() && manager.Hosts.Count > 0 && manager.Hosts[0].Content is UiContent uc)
            uc.Host.SetRoot(app.BuildRoot());
        // プレビューの子 UiHost はウィンドウ実体化後に生まれるので、できたら "story" として登録
        if (!storyRegistered && app.StoryHost is { } sh)
        {
            manager.UiRegistry.Register("story", sh);
            storyRegistered = true;
        }
        if (seconds > 0 && sw.Elapsed.TotalSeconds > seconds) break;
        // 描画した周回は present (vsync) が既にループを律速している — スリープを足すと
        // 入力レイテンシに直列加算されるので休まない。完全アイドルの周回だけ CPU を返す。
        if (!manager.AnyRendered) Thread.Sleep(8);
    }
    Console.WriteLine("gallery: shutting down");
    return 0;
}
