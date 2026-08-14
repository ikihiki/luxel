using System.Diagnostics;
using Luxel;
using Luxel.Framework.Game.Native;
using Luxel.Framework.Game;
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
// LuxelHostBuilder + GameLoop (RangeRealtimeScene) でゲームループを駆動し、Win32Window + GpuSurface へ提示する。
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

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("The LuxelRange native sample currently requires Windows.");
    return 3;
}

int exit = 1;
var thread = new Thread(() => exit = Run(backend, frames)) { Name = "LuxelRange-Main" };
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return exit;

static GpuDevice CreateDevice(string backend) => backend switch
{
    "dx" => new GpuDevice(Luxel.Graphics.DirectX12.D3D12Backend.Create()),
    _ => new GpuDevice(Luxel.Graphics.Vulkan.VulkanBackend.Create()),
};

static int Run(string backend, int frames)
{
    try
    {
        using GpuDevice device = CreateDevice(backend);
        Console.WriteLine($"=== Luxel Range (backend: {backend}, device: {device.Name}) ===");

        int w = RangeRealtimeScene.Width, h = RangeRealtimeScene.Height;
        using var windows = new WindowSystem(Win32WindowBackend.Create());
        Window win = windows.CreateWindow(new Luxel.Platform.Abstraction.WindowDesc("Luxel Range", w, h));
        Win32Window nativeWindow = win.RequireBackendWindow<Win32Window>();
        using GpuSurface surface = device.Backend switch
        {
            Luxel.Graphics.DirectX12.D3D12Backend d3d12 => d3d12.CreateSurface(nativeWindow.Handle, (uint)Math.Max(1, win.Width), (uint)Math.Max(1, win.Height)),
            Luxel.Graphics.Vulkan.VulkanBackend vulkan => vulkan.CreateWin32Surface(nativeWindow.Handle, (uint)Math.Max(1, win.Width), (uint)Math.Max(1, win.Height)),
            _ => throw new PlatformNotSupportedException($"Unsupported backend: {device.Backend.GetType().FullName}"),
        };

        using WindowInputSource input = win.CreateInputSource("range-window");

        // ハイスコアは %APPDATA%/LuxelRange/ へ (リポジトリ非依存の実ユーザ書込パス)。
        string saveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LuxelRange");
        var fileStore = new PhysicalFileStore(saveDir);

        var pacer = new FramePacer();
        using IHost host = LuxelHostBuilder.Create()
            .UseGpuDevice(device)
            .UseAudio()   // Native拡張がWindowsではXAudio2 + AudioMixerをDI登録。
            .UseFrameWaiter(pacer.WaitAsync)
            .ConfigureServices(s =>
            {
                s.AddSingleton<IFileStore>(fileStore);
                s.AddSingleton<IInputSource>(input);
                s.AddSingleton(sp => new RangeGame(sp.GetRequiredService<IFileStore>()));
                s.AddSingleton<RangeRealtimeScene>();
                s.AddSingleton<IGameSceneBootstrap>(sp =>
                    new RangeSceneBootstrap(sp.GetRequiredService<RangeRealtimeScene>()));
            })
            .UseStandardCadences()
            .AddGameLoop<GameLoop>()
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
            {
                scene.DrawHud();
                surface.Present(fb, scene.StridePixels, (uint)w, (uint)h);
            }

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

sealed class RangeSceneBootstrap(RangeRealtimeScene scene) : IGameSceneBootstrap
{
    public ValueTask BootstrapAsync(IGameSceneSystem scenes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        scenes.Enqueue(new GameSceneCommand.Push(GameSceneId.New(), scene));
        return ValueTask.CompletedTask;
    }
}

/// <summary>メインスレッドの <see cref="Tick"/> で GameLoop の 1 フレームを同期的に進めるペーサ (Cavern と同型)。</summary>
sealed class FramePacer
{
    private readonly object _gate = new();
    private TaskCompletionSource? _tick;
    private TaskCompletionSource? _frameComplete;
    private bool _cancelHooked;

    public Task WaitAsync(CancellationToken token)
    {
        TaskCompletionSource tick;
        TaskCompletionSource? frameComplete;
        lock (_gate)
        {
            if (!_cancelHooked)
            {
                _cancelHooked = true;
                token.Register(() => Cancel(token));
            }
            frameComplete = _frameComplete;
            _frameComplete = null;
            tick = _tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        frameComplete?.TrySetResult();
        if (token.IsCancellationRequested) tick.TrySetCanceled(token);
        return tick.Task;
    }

    public void Tick()
    {
        TaskCompletionSource? tick;
        TaskCompletionSource frameComplete;
        lock (_gate)
        {
            tick = _tick;
            _tick = null;
            if (tick is null) return;
            frameComplete = _frameComplete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        tick.TrySetResult();
        frameComplete.Task.GetAwaiter().GetResult();
    }

    private void Cancel(CancellationToken token)
    {
        TaskCompletionSource? tick;
        TaskCompletionSource? frameComplete;
        lock (_gate)
        {
            tick = _tick;
            frameComplete = _frameComplete;
            _tick = null;
            _frameComplete = null;
        }
        tick?.TrySetCanceled(token);
        frameComplete?.TrySetCanceled(token);
    }
}
