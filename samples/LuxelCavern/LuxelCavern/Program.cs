using System.Diagnostics;
using Luxel;
using Luxel.DevTools;
using Luxel.Framework;
using Luxel.Framework.DevTools;
using Luxel.Input;
using Luxel.Platform;
using Luxel.Settings;
using Luxel.Typography;
using LuxelCavern;
using LuxelCavern.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// LuxelCavern — スタンドアロン実行 (capstone ①, Stage B: 実時間プレイアブル)。
//   LuxelCavern[.exe] [vk|dx] [--frames N]
//     vk (既定) / dx : GPU バックエンド
//     --frames N      : N フレーム回して自動終了 (publish スモーク・CI 用)
//
// LuxelHostBuilder + GameScene (CavernRealtimeScene) でゲームループを駆動し、Win32Window + GpuSurface へ
// 提示する (Framework は窓/提示を持たないので、フレーム待ちを pacer で同期し scene のフレームバッファを Present)。
// 実窓 (Win32) は STA スレッド必須。操作: A/D or ←→ 移動、Space/W/↑ ジャンプ、Esc ポーズ、Enter リトライ。

string backend = "vk";
int frames = 0;
for (int i = 0; i < args.Length; i++)
{
    string a = args[i].ToLowerInvariant();
    if (a is "vk" or "vulkan") backend = "vk";
    else if (a is "dx" or "d3d12") backend = "dx";
    else if (a == "--frames" && i + 1 < args.Length && int.TryParse(args[i + 1], out int n)) { frames = Math.Max(1, n); i++; }
    // DevTools 系フラグ (--devtools[ port] / --devtools-native) は DevToolsOptions.Parse が解釈する。
}

int exit = 1;
var thread = new Thread(() => exit = Run(backend, frames, args)) { Name = "LuxelCavern-Main" };
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return exit;

// フォントは Core.dll 埋め込みを ResourceSystem 経由でロード (RS は読み込み後に破棄、VectorFont はバイトを保持)。
static VectorFont LoadBodyFont()
{
    using Luxel.Resources.ResourceSystem res = CavernResources.CreateEmbedded();
    return CavernAssets.LoadBodyFont(res);
}

static GpuDevice CreateDevice(string backend) => backend switch
{
    "dx" => new GpuDevice(Luxel.D3D12.D3D12Backend.Create()),
    _ => new GpuDevice(Luxel.Vulkan.VulkanBackend.Create()),
};

static int Run(string backend, int frames, string[] args)
{
    try
    {
        using GpuDevice device = CreateDevice(backend);
        Console.WriteLine($"=== Luxel Cavern (backend: {backend}, device: {device.Name}) ===");
        // フォントは Core.dll 埋め込みを ResourceSystem 経由で読む (single-file publish でも loose 依存なし)。
        using VectorFont font = LoadBodyFont();

        int w = CavernRealtimeScene.Width, h = CavernRealtimeScene.Height;
        using var windows = new WindowSystem(Win32WindowBackend.Create());
        NativeWindow win = windows.CreateWindow(new Luxel.Abstraction.WindowDesc(TitleScreen.GameTitle, w, h));
        using GpuSurface surface = win.CreateSwapchain(device);

        var keyboard = new KeyboardSource();
        win.KeyDown += vk => keyboard.Down(vk);
        win.KeyUp += vk => keyboard.Up(vk);

        // セーブは %APPDATA%/LuxelCavern/ に置く (checklist 6: 実ユーザ書込パス、リポジトリ非依存)。
        string saveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuxelCavern");
        var fileStore = new PhysicalFileStore(saveDir);

        // DevTools 結線 (任意、--devtools[ port] / --devtools-native)。内蔵版は第二 GpuDevice を島スレッドで作る。
        var devOptions = DevToolsOptions.Parse(args, () => CreateDevice(backend));

        var pacer = new FramePacer();
        using IHost host = LuxelHostBuilder.Create()
            .UseGpuDevice(device)
            .UseAudio()   // XAudio2 + AudioMixer を DI 登録 (BGM/SE)。オーディオデバイス不在時は各 voice が no-op。
            .UseFrameWaiter(pacer.WaitAsync)
            .WithDevTools(devOptions)   // listener + DebugServer/DevToolsApp + IFramePublisher を host に載せる
            .ConfigureServices(s =>
            {
                s.AddSingleton(font);
                s.AddSingleton<IInputSource>(keyboard);
                s.AddSingleton<IKeyCapture>(keyboard);
                s.AddSingleton<IFileStore>(fileStore);
                s.AddSingleton<CavernRealtimeScene>();
            })
            .AddScene<CavernRealtimeScene>()
            .Build();

        host.Start();   // GameLoop + DevToolsRuntime 開始 (最初のフレーム待ちで停止)
        var scene = host.Services.GetRequiredService<CavernRealtimeScene>();
        var framePublisher = host.Services.GetService<IFramePublisher>();   // 提示ループが呼ぶフレーム配信口
        if (host.Services.GetService<DevToolsRuntime>()?.BrowserUrl is { } url)
            Console.WriteLine($"DevTools: {url} (ブラウザで開くとゲームの統計/画面を確認できます)");

        var sw = Stopwatch.StartNew();
        int drawn = 0;
        while (windows.Pump())
        {
            long t0 = sw.ElapsedMilliseconds;
            pacer.Tick();   // 1 フレーム分のゲームループを同期実行 (入力→固定更新→描画)
            if (scene.Framebuffer is { } fb)
            {
                surface.Present(fb, scene.StridePixels, (uint)w, (uint)h);
                framePublisher?.Publish(fb, (int)scene.StridePixels, w, h);   // DevTools へ配信 (購読者が居るときだけ)
            }

            if (scene.QuitRequested) { win.Close(); windows.Pump(); break; }        // タイトルの「おわる」
            if (frames > 0 && ++drawn >= frames) { win.Close(); windows.Pump(); break; }

            int elapsed = (int)(sw.ElapsedMilliseconds - t0);
            if (elapsed < 16) Thread.Sleep(16 - elapsed);   // ~60fps 上限
        }

        host.StopAsync().GetAwaiter().GetResult();
        Console.WriteLine(frames > 0 ? $"cavern: {frames} フレーム描画して終了 (smoke ok)" : "cavern: 終了");
        return 0;
    }
    catch (Exception ex)
    {
        string log = Path.Combine(AppContext.BaseDirectory, "cavern-crash.log");
        try { File.WriteAllText(log, ex.ToString()); } catch { /* ログ失敗は無視 */ }
        Console.Error.WriteLine(ex);
        return 1;
    }
}

/// <summary>Win32 のキーイベントを <see cref="InputBus"/> へ流す入力源 (GameLoop が毎フレーム Poll)。
/// <see cref="IKeyCapture"/> も実装し、設定画面のリバインドで「直近に押された生キー」を取り出せる。</summary>
sealed class KeyboardSource : IInputSource, IKeyCapture
{
    private readonly List<(KeyCode Key, bool Down)> _pending = new();
    private KeyCode? _lastPressed;
    public string Name => "cavern-keyboard";

    public void Down(ushort vk) { if (Map(vk) is { } k) lock (_pending) { _pending.Add((k, true)); _lastPressed = k; } }
    public void Up(ushort vk) { if (Map(vk) is { } k) lock (_pending) _pending.Add((k, false)); }

    /// <summary>リバインド用: 直近に押されたキーを取り出してクリア。</summary>
    public KeyCode? TakePressed() { lock (_pending) { KeyCode? k = _lastPressed; _lastPressed = null; return k; } }

    public void Poll(InputBus bus)
    {
        lock (_pending)
        {
            foreach ((KeyCode k, bool d) in _pending) bus.EnqueueKey(k, d);
            _pending.Clear();
        }
    }

    private static KeyCode? Map(ushort vk) => vk switch
    {
        >= 0x41 and <= 0x5A => (KeyCode)((int)KeyCode.A + (vk - 0x41)),   // A-Z
        0x70 => KeyCode.F1,   // DevTools オーバーレイ切替
        0x20 => KeyCode.Space,
        0x0D => KeyCode.Enter,
        0x1B => KeyCode.Escape,
        0x25 => KeyCode.Left,
        0x27 => KeyCode.Right,
        0x26 => KeyCode.Up,
        0x28 => KeyCode.Down,
        _ => null,
    };
}

/// <summary>メインスレッドの <see cref="Tick"/> で GameLoop の 1 フレームを同期的に進めるペーサ
/// (StoryAppView と同型 — TCS を inline 完了させ、フレーム実行を呼び出しスレッドで走らせて GPU キュー安全)。</summary>
sealed class FramePacer
{
    private TaskCompletionSource? _tcs;
    private bool _cancelHooked;

    public Task WaitAsync(CancellationToken token)
    {
        if (!_cancelHooked)
        {
            _cancelHooked = true;
            token.Register(() => Interlocked.Exchange(ref _tcs, null)?.TrySetCanceled(token));
        }
        var tcs = new TaskCompletionSource();
        Volatile.Write(ref _tcs, tcs);
        if (token.IsCancellationRequested) tcs.TrySetCanceled(token);
        return tcs.Task;
    }

    public void Tick() => Interlocked.Exchange(ref _tcs, null)?.TrySetResult();
}
