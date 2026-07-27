using System.Diagnostics;
using Luxel;
using Luxel.Framework;
using Luxel.Platform;
using Luxel.Player;
using Luxel.Resources;
using Luxel.Typography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Luxel.Player.App — Studio プロジェクトフォルダを実行する汎用プレイヤー (ToDo 27 GE-3 S3)。
//   Luxel.Player.App[.exe] <プロジェクトフォルダ> [vk|dx] [--frames N]
//     --frames N : N フレーム回して自動終了 (スモーク・CI 用)。Esc で終了。
// LuxelCavern exe と同型: LuxelHostBuilder + GameScene を FramePacer で同期駆動し、
// フレームバッファをスワップチェーンへ Present する。実窓 (Win32) は STA スレッド必須。

// --ship <プロジェクト> <出力> [--csproj path] = 出荷 (publish + project/ コピー、GE-6)。窓は開かない
if (args.Length >= 1 && args[0] == "--ship")
{
    if (args.Length < 3) { Console.Error.WriteLine("使い方: Luxel.Player.App --ship <プロジェクトフォルダ> <出力フォルダ> [--csproj path]"); return 2; }
    string csproj = args.Length >= 5 && args[3] == "--csproj" ? args[4]
        : Path.Combine("src", "Luxel.Player.App", "Luxel.Player.App.csproj");   // 既定 = リポジトリルートから
    try
    {
        string outp = Luxel.Player.PlayerShipper.Ship(csproj, args[1], args[2]);
        Console.WriteLine($"ship: {outp} (exe + project/ — フォルダごと配布可)");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
}

string? folder = null;
string backend = "vk";
int frames = 0;
for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    if (a.Equals("vk", StringComparison.OrdinalIgnoreCase)) backend = "vk";
    else if (a.Equals("dx", StringComparison.OrdinalIgnoreCase)) backend = "dx";
    else if (a == "--frames" && i + 1 < args.Length && int.TryParse(args[i + 1], out int n)) { frames = Math.Max(1, n); i++; }
    else folder ??= a;
}
// 引数省略時は exe 隣の project/ (PlayerShipper の出荷レイアウト規約 — 配布フォルダはダブルクリックで動く)
folder ??= Path.Combine(AppContext.BaseDirectory, "project");
if (!Directory.Exists(folder))
{
    Console.Error.WriteLine("使い方: Luxel.Player.App <プロジェクトフォルダ> [vk|dx] [--frames N] (省略時は exe 隣の project/)");
    return 2;
}

int exit = 1;
var thread = new Thread(() => exit = Run(folder, backend, frames)) { Name = "LuxelPlayer-Main" };
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();
return exit;

static int Run(string folder, string backend, int frames)
{
    try
    {
        using var fs = new PhysicalFileSystem(folder);
        PlayerGame game = PlayerLoader.LoadStart(fs);
        foreach (string d in game.World.Behaviours!.Diagnostics) Console.Error.WriteLine($"[csx] {d}");

        using var device = new GpuDevice(backend == "dx" ? Luxel.Graphics.DirectX12.D3D12Backend.Create() : Luxel.Graphics.Vulkan.VulkanBackend.Create());
        Console.WriteLine($"=== {game.Project.Name} (Luxel.Player, backend: {backend}, device: {device.Name}) ===");
        using VectorFont font = VectorFont.LoadSystem();

        int w = game.Project.WindowWidth, h = game.Project.WindowHeight;
        using var windows = new WindowSystem(Win32WindowBackend.Create());
        NativeWindow win = windows.CreateWindow(new Luxel.Abstraction.WindowDesc(game.Project.Name, w, h));
        using GpuSurface surface = device.CreateSurface(win.Handle, (uint)Math.Max(1, win.Width), (uint)Math.Max(1, win.Height));

        // 生キー → world.KeysDown (csx が world.KeysDown.Contains("Right") 等で読む)
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool esc = false;
        win.KeyDown += input => { if (KeyName(input.Key) is { } k) lock (keys) keys.Add(k); if (input.Key == WindowKey.Escape) esc = true; };
        win.KeyUp += input => { if (KeyName(input.Key) is { } k) lock (keys) keys.Remove(k); };

        var pacer = new FramePacer();
        using IHost host = LuxelHostBuilder.Create()
            .UseGpuDevice(device)
            .UseFrameWaiter(pacer.WaitAsync)
            .ConfigureServices(s =>
            {
                s.AddSingleton(font);
                s.AddSingleton(game);
                s.AddSingleton<Func<ISet<string>>>(() => { lock (keys) return new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase); });
                s.AddSingleton<PlayerRealtimeScene>();
            })
            .AddScene<PlayerRealtimeScene>()
            .Build();

        host.Start();
        var scene = host.Services.GetRequiredService<PlayerRealtimeScene>();

        var sw = Stopwatch.StartNew();
        int drawn = 0;
        while (windows.Pump())
        {
            long t0 = sw.ElapsedMilliseconds;
            pacer.Tick();
            if (scene.Framebuffer is { } fb)
                surface.Present(fb, scene.StridePixels, (uint)w, (uint)h);
            if (esc || (frames > 0 && ++drawn >= frames)) { win.Close(); windows.Pump(); break; }
            int elapsed = (int)(sw.ElapsedMilliseconds - t0);
            if (elapsed < 16) Thread.Sleep(16 - elapsed);
        }

        host.StopAsync().GetAwaiter().GetResult();
        Console.WriteLine(frames > 0 ? $"player: {frames} フレーム描画して終了 (smoke ok)" : "player: 終了");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        return 1;
    }
}

// Portable window key → csx が読むキー名 (最小セット。網羅は InputAction 宣言と合わせて GE-7 で)
static string? KeyName(WindowKey key) => key switch
{
    >= WindowKey.A and <= WindowKey.Z => key.ToString(),
    WindowKey.Space => "Space",
    WindowKey.Enter => "Enter",
    WindowKey.Left => "Left",
    WindowKey.Right => "Right",
    WindowKey.Up => "Up",
    WindowKey.Down => "Down",
    _ => null,
};

/// <summary>メインスレッドの Tick で GameLoop の 1 フレームを同期的に進めるペーサ (LuxelCavern と同型)。</summary>
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
