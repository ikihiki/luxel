using Luxel.Diagnostics;
using Luxel.Platform;
using Luxel.UI.App;
using Luxel.Graphics.TwoD;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.DevTools;

/// <summary>
/// ネイティブ DevTools ウィンドウ (別スレッドの UI 島)。
/// <see cref="Launch"/> で専用 STA スレッドを起動し、**自前の GpuDevice + WindowSystem + テーマ signal**
/// を所有する — メインスレッドとの共有は <see cref="DevToolsListener"/> (読み、スレッドセーフ) と
/// <see cref="EngineCommands.Enqueue"/> (書き、ConcurrentQueue) のみ。GPU キュー/signal は一切共有しない。
/// ウィンドウを閉じると DevTools スレッドだけ終了する (アプリ本体は継続)。
/// </summary>
public sealed class DevToolsApp : IDisposable
{
    private Thread? _thread;
    private volatile bool _stop;

    /// <summary>E2E 検証用の第二 DebugServer の URL (e2ePort 指定時のみ、起動後に設定)。</summary>
    public volatile string? E2EUrl;

    private DevToolsApp() { }

    /// <summary>DevTools ウィンドウを別スレッドで起動する。
    /// <paramref name="createDevice"/> は DevTools スレッド上で呼ばれ、専用デバイスを作る。</summary>
    public static DevToolsApp Launch(Func<GpuDevice> createDevice, DevToolsListener listener,
                                     EngineCommands commands, int e2ePort = 0)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The native DevTools window currently requires Windows.");
        var app = new DevToolsApp();
        var thread = new Thread(() => app.Run(createDevice, listener, commands, e2ePort))
        {
            Name = "Luxel.DevTools",
            IsBackground = true,   // アプリ終了を妨げない
        };
        thread.SetApartmentState(ApartmentState.STA);
        app._thread = thread;
        thread.Start();
        return app;
    }

    private void Run(Func<GpuDevice> createDevice, DevToolsListener listener, EngineCommands commands, int e2ePort)
    {
        Clipboard? installedClipboard = null;
        try
        {
            using GpuDevice device = createDevice();
            Console.WriteLine($"[devtools] device: {device.Name}");
            using VectorFont font = VectorFont.LoadSystemJapanese();
            using var windows = new WindowSystem(Win32WindowBackend.Create());
            using var clipboard = new Clipboard(Win32WindowBackend.CreateClipboardBackend());
            if (PlatformClipboard.Current is null)
            {
                PlatformClipboard.Current = clipboard;
                installedClipboard = clipboard;
            }

            // この島専用のテーマ signal (グローバル UiTheme.Current は購読しない)
            var theme = new Signal<Theme>(Theme.Dark.Compact());
            // ウィンドウ操作/入力用の島内コマンド (エンジン側 commands とは別 — こちらは島スレッドで Drain)
            var localCmds = new EngineCommands();
            using var manager = new WindowManager(device, font, windows, localCmds);

            var ui = new DevToolsUi(listener, commands, theme);
            manager.CreateUiWindow(
                new Luxel.Platform.Abstraction.WindowDesc("Luxel DevTools", 900, 700) { X = 60, Y = 60 },
                "devtools", ui.BuildRoot, theme);

            // E2E 用の第二 DebugServer (devtools 窓の /windows /winframe と島内入力 op)
            DevToolsListener? e2eListener = null;
            DebugServer? e2e = null;
            if (e2ePort > 0)
            {
                e2eListener = new DevToolsListener(localCmds);
                e2e = new DebugServer(e2eListener, e2ePort, windows: manager);
                e2e.Start();
                E2EUrl = e2e.Url;
                Console.WriteLine($"[devtools] E2E URL: {E2EUrl}");
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            long last = 0;
            while (!_stop)
            {
                long now = sw.ElapsedMilliseconds;
                float dt = Math.Min(0.1f, (now - last) / 1000f);
                last = now;
                if (!manager.RunFrame(dt)) break;   // ウィンドウが閉じられた
                ui.Update();
                Thread.Sleep(16);
            }
            e2e?.Dispose();
            e2eListener?.Dispose();
            Console.WriteLine("[devtools] shutting down");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[devtools] スレッド異常終了: {e}");
        }
        finally
        {
            if (ReferenceEquals(PlatformClipboard.Current, installedClipboard))
                PlatformClipboard.Current = null;
        }
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread is { IsAlive: true }) _thread.Join(3000);
    }
}
