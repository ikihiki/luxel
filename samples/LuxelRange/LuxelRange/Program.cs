using System.Diagnostics;
using Luxel;
using Luxel.Framework;
using Luxel.Input;
using Luxel.Platform;
using Luxel.Settings;
using LuxelRange;
using LuxelRange.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// LuxelRange — スタンドアロン実行 (capstone ②, 3D 射的)。
//   LuxelRange[.exe] [vk|dx] [--frames N]
//     vk (既定) / dx : GPU バックエンド
//     --frames N      : N フレーム回して自動終了 (publish スモーク・CI 用)
//
// LuxelHostBuilder + GameScene (RangeRealtimeScene) でゲームループを駆動し、Win32Window + GpuSurface へ提示する。
// この段は attract 動作 (カメラ自動旋回 + 定期発射) で 3D 描画/物理/publish 経路を通す薄い層。

string backend = "vk";
int frames = 0;
for (int i = 0; i < args.Length; i++)
{
    string a = args[i].ToLowerInvariant();
    if (a is "vk" or "vulkan") backend = "vk";
    else if (a is "dx" or "d3d12") backend = "dx";
    else if (a == "--frames" && i + 1 < args.Length && int.TryParse(args[i + 1], out int n)) { frames = Math.Max(1, n); i++; }
}

int exit = 1;
var thread = new Thread(() => exit = Run(backend, frames)) { Name = "LuxelRange-Main" };
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return exit;

static GpuDevice CreateDevice(string backend) => backend switch
{
    "dx" => new GpuDevice(Luxel.D3D12.D3D12Backend.Create()),
    _ => new GpuDevice(Luxel.Vulkan.VulkanBackend.Create()),
};

static int Run(string backend, int frames)
{
    try
    {
        using GpuDevice device = CreateDevice(backend);
        Console.WriteLine($"=== Luxel Range (backend: {backend}, device: {device.Name}) ===");

        int w = RangeRealtimeScene.Width, h = RangeRealtimeScene.Height;
        using var windows = new WindowSystem(Win32WindowBackend.Create());
        NativeWindow win = windows.CreateWindow(new Luxel.Abstraction.WindowDesc("Luxel Range", w, h));
        using GpuSurface surface = win.CreateSwapchain(device);

        var keyboard = new KeyboardSource();
        win.KeyDown += vk => keyboard.Down(vk);
        win.KeyUp += vk => keyboard.Up(vk);

        // ハイスコアは %APPDATA%/LuxelRange/ へ (リポジトリ非依存の実ユーザ書込パス)。
        string saveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuxelRange");
        var fileStore = new PhysicalFileStore(saveDir);

        var pacer = new FramePacer();
        using IHost host = LuxelHostBuilder.Create()
            .UseGpuDevice(device)
            .UseAudio()   // XAudio2 + AudioMixer (BGM/SE)。オーディオデバイス不在時は no-op。
            .UseFrameWaiter(pacer.WaitAsync)
            .ConfigureServices(s =>
            {
                s.AddSingleton<IFileStore>(fileStore);
                s.AddSingleton<IInputSource>(keyboard);
                s.AddSingleton(sp => new RangeGame(sp.GetRequiredService<IFileStore>()));
                s.AddSingleton<RangeRealtimeScene>();
            })
            .AddScene<RangeRealtimeScene>()
            .Build();

        host.Start();
        var scene = host.Services.GetRequiredService<RangeRealtimeScene>();

        var sw = Stopwatch.StartNew();
        int drawn = 0;
        while (windows.Pump())
        {
            long t0 = sw.ElapsedMilliseconds;
            pacer.Tick();
            if (scene.Framebuffer is { } fb)
                surface.Present(fb, scene.StridePixels, (uint)w, (uint)h);

            if (scene.QuitRequested) { win.Close(); windows.Pump(); break; }
            if (frames > 0 && ++drawn >= frames) { win.Close(); windows.Pump(); break; }

            int elapsed = (int)(sw.ElapsedMilliseconds - t0);
            if (elapsed < 16) Thread.Sleep(16 - elapsed);
        }

        host.StopAsync().GetAwaiter().GetResult();
        Console.WriteLine(frames > 0 ? $"range: {frames} フレーム描画して終了 (smoke ok)" : "range: 終了");
        return 0;
    }
    catch (Exception ex)
    {
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "range-crash.log"), ex.ToString()); } catch { }
        Console.Error.WriteLine(ex);
        return 1;
    }
}

/// <summary>メインスレッドの <see cref="Tick"/> で GameLoop の 1 フレームを同期的に進めるペーサ (Cavern と同型)。</summary>
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

/// <summary>Win32 のキーイベントを <see cref="InputBus"/> へ流す入力源 (GameLoop が毎フレーム Poll)。</summary>
sealed class KeyboardSource : IInputSource
{
    private readonly List<(KeyCode Key, bool Down)> _pending = new();
    public string Name => "range-keyboard";

    public void Down(ushort vk) { if (Map(vk) is { } k) lock (_pending) _pending.Add((k, true)); }
    public void Up(ushort vk) { if (Map(vk) is { } k) lock (_pending) _pending.Add((k, false)); }

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
