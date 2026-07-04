using System.Diagnostics;
using Luxel.Abstraction;
using Luxel.Controls;
using Luxel.DevTools;
using Luxel.Diagnostics;
using Luxel.Platform;
using Luxel.Typography;
using Luxel.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Samples;

/// <summary>
/// Sample 95: マルチウィンドウ + DevTools 統合リモート制御 + マルチウィンドウ TSF。
/// <see cref="WindowSystem"/> (Win32) で実ウィンドウを 2 枚生成し、各スワップチェーンへ独立 UI を描画。
/// さらにオフスクリーン UI ("status") も登録する — UI はウィンドウと 1:1 ではない。
/// リモートは既存 <see cref="DebugServer"/> に統合:
///   GET /windows /winframe?id= (ウィンドウ) ・ /trees /uiframe (UI) ・ POST /cmd
///   (window.* は id、UI 入力/ui.set は "ui" index/名前でルーティング)
/// 使い方: dotnet run --project src/Luxel.Samples -- vk 95 [port=5175] [seconds=60]
///         dotnet run --project src/Luxel.Samples -- vk 95 tsf   … TSF 実変換 E2E (要 日本語 IME + 前面, CI 不可)
/// </summary>
public static class Sample95MultiWindow
{
    public static int Run(Func<GpuDevice> createDevice, int port, int seconds, bool tsfTest = false)
    {
        int exit = 1;
        var t = new Thread(() => exit = tsfTest ? TsfBody(createDevice) : Body(createDevice, port, seconds));
        t.SetApartmentState(ApartmentState.STA);   // ウィンドウ/COM/TSF は STA (AppWindow 系と同じ)
        t.Start();
        t.Join();
        return exit;
    }

    /// <summary>マルチウィンドウ TSF の実変換 E2E: 窓 1 で "nihongo"→変換→確定、窓 2 へフォーカス切替後
    /// "sushi"→変換→確定。それぞれの TextField に かな/漢字 が入り、他方が汚れないことを検証する。</summary>
    private static int TsfBody(Func<GpuDevice> createDevice)
    {
        using GpuDevice device = createDevice();
        using VectorFont font = VectorFont.LoadSystemJapanese();
        using var windows = new WindowSystem(Win32WindowBackend.Create());
        using var manager = new WindowManager(device, font, windows);

        var textA = new Signal<string>("");
        var textB = new Signal<string>("");
        var textC = new Signal<string>("");
        WindowHost winA = manager.CreateUiWindow(new WindowDesc("TSF A", 480, 240) { X = 80, Y = 80 }, "editorA",
            () => Frame(VStack(8)[Text("Editor A", 16, color: Bind.From(() => UiTheme.T.Text)), TextField(textA)]));
        WindowHost winB = manager.CreateUiWindow(new WindowDesc("TSF B", 480, 240) { X = 620, Y = 80 }, "editorB",
            () => Frame(VStack(8)[Text("Editor B", 16, color: Bind.From(() => UiTheme.T.Text)), TextField(textB)]));
        // 窓 C: SurfaceView (iframe 相当) 越しの TextArea — ギャラリーのプレビューと同じ経路。
        // SurfaceView の ITextInput 転送 (ChildTextInput) が無いと TSF が空文書になる回帰の検証。
        SurfaceView sv = null!;
        WindowHost winC = manager.CreateUiWindow(new WindowDesc("TSF C (SurfaceView)", 480, 300) { X = 80, Y = 380 }, "editorC",
            () =>
            {
                sv = SurfaceView(320, 200);
                sv.SetContent(Frame(TextArea(textC, height: 120)));
                return Frame(sv);
            });
        Console.WriteLine($"TSF: A={winA.TsfActive} B={winB.TsfActive} C={winC.TsfActive}");

        UiHost hostA = ((UiContent)winA.Content).Host;
        UiHost hostB = ((UiContent)winB.Content).Host;
        hostA.FocusNext();   // TextField にフォーカス (click 相当)
        hostB.FocusNext();

        void Frames(int n) { for (int i = 0; i < n; i++) { manager.RunFrame(1f / 60f); Thread.Sleep(8); } }
        // display attribute の目視検証用: 変換途中のフレームを PNG 保存
        void Snap(WindowHost win, string name)
        {
            (byte[]? body, _) = ((Luxel.Diagnostics.IWindowRemoteHost)manager).GetFrame(win.Id, null);
            if (body is null) return;
            int w = BitConverter.ToInt32(body, 0), h = BitConverter.ToInt32(body, 4);
            string path = Path.Combine(Path.GetTempPath(), $"tsf-{name}.png");
            File.WriteAllBytes(path, PngWriter.ToBytes(w, h, body.AsSpan(8)));
            Console.WriteLine($"snap: {path}");
        }
        void TypeConvertCommit(string romaji, WindowHost? snapWin = null, string snapName = "")
        {
            foreach (char c in romaji) { WindowHost.SendKeyStroke((ushort)char.ToUpperInvariant(c)); Frames(4); }
            if (snapWin is not null) { Frames(15); Snap(snapWin, snapName + "-preedit"); }    // かな preedit + 下線
            WindowHost.SendKeyStroke(0x20); Frames(30);   // Space=変換
            if (snapWin is not null) Snap(snapWin, snapName + "-converted");                  // 変換対象節の強調
            WindowHost.SendKeyStroke(0x0D); Frames(30);   // Enter=確定
        }
        static bool HasJapanese(string s) => s.Any(c => c >= 0x3040);

        // 窓 A を前面化して日本語入力。前面化できないまま SendInput すると他アプリへキーが飛ぶため必ず確認する。
        bool fgA = winA.ForceForeground(); Frames(30);
        Console.WriteLine($"A foreground={fgA} focused={winA.Window.IsFocused} activeInput={hostA.ActiveTextInput is not null}");
        if (!winA.Window.IsFocused) { Console.WriteLine("SKIP: 前面化できない環境 (キー注入を中止)"); return 1; }
        winA.ActivateJapaneseIme(); Frames(30);
        TypeConvertCommit("nihongo", winA, "a");
        string a1 = hostA.InputText;
        Console.WriteLine($"A after nihongo: '{a1}'");

        // 窓 B へフォーカス切替 (WM_SETFOCUS → TSF 文書切替) して日本語入力
        bool fgB = winB.ForceForeground(); Frames(30);
        if (!winB.Window.IsFocused) { Console.WriteLine("SKIP: 窓 B を前面化できない (キー注入を中止)"); return 1; }
        winB.ActivateJapaneseIme(); Frames(30);
        TypeConvertCommit("sushi");
        string b1 = hostB.InputText;
        string a2 = hostA.InputText;
        Console.WriteLine($"B after sushi: '{b1}' / A unchanged: '{a2}'");

        // 窓 C (SurfaceView 越しの TextArea) へフォーカス切替して日本語入力。
        // クリックで SurfaceView (親) と TextArea (子ホスト) の両方にフォーカスが立つ。
        UiHost hostC = ((UiContent)winC.Content).Host;
        bool fgC = winC.ForceForeground(); Frames(30);
        if (!winC.Window.IsFocused) { Console.WriteLine("SKIP: 窓 C を前面化できない (キー注入を中止)"); return 1; }
        hostC.PointerDown(240, 150); hostC.PointerUp(240, 150); Frames(10);
        Console.WriteLine($"C activeInput(parent)={hostC.ActiveTextInput is not null} activeInput(child)={sv.Child?.ActiveTextInput is not null}");
        winC.ActivateJapaneseIme(); Frames(30);
        TypeConvertCommit("nihongo", winC, "c");
        string c1 = sv.Child?.InputText ?? "";
        Console.WriteLine($"C after nihongo: '{c1}'");

        bool ok = HasJapanese(a1) && HasJapanese(b1) && a2 == a1 && !b1.Contains(a1) && HasJapanese(c1);
        Console.WriteLine(ok ? "OK: マルチウィンドウ TSF (窓毎の文書切替 + 実変換 + SurfaceView 転送)" : "FAILED");
        return ok ? 0 : 1;
    }

    private static int Body(Func<GpuDevice> createDevice, int port, int seconds)
    {
        using GpuDevice device = createDevice();
        Console.WriteLine($"device: {device.Name}");
        using VectorFont font = VectorFont.LoadSystem();
        using var windows = new WindowSystem(Win32WindowBackend.Create());

        // 既存 DevTools と同じ結線: 共有 EngineCommands + DiagnosticListener 購読 + DebugServer
        var cmds = new EngineCommands();
        using var listener = new DevToolsListener(cmds);
        using var manager = new WindowManager(device, font, windows, cmds);

        manager.RegisterContent("counter", BuildCounter);
        manager.RegisterContent("toggle", BuildToggle);

        manager.CreateUiWindow(new WindowDesc("Luxel Counter", 480, 320) { X = 80, Y = 80 }, "counter", BuildCounter);
        manager.CreateUiWindow(new WindowDesc("Luxel Toggle", 400, 300) { X = 640, Y = 120 }, "toggle", BuildToggle);
        manager.AddOffscreenUi("status", 320, 200, BuildStatus);   // ウィンドウ無し UI (tree/入力のみ)

        using var server = new DebugServer(listener, port, windows: manager);
        server.Start();
        Console.WriteLine($"DevTools URL: {server.Url} (windows: /windows /winframe, ui: /trees, ops: /cmd)");

        var sw = Stopwatch.StartNew();
        long last = 0;
        while (manager.RunFrame(Dt(sw, ref last)))
        {
            if (seconds > 0 && sw.Elapsed.TotalSeconds > seconds) break;
            Thread.Sleep(8);
        }
        Console.WriteLine("sample95: shutting down");
        return 0;
    }

    private static float Dt(Stopwatch sw, ref long last)
    {
        long now = sw.ElapsedMilliseconds;
        float dt = Math.Min(0.1f, (now - last) / 1000f);
        last = now;
        return dt;
    }

    private static Border Frame(Widget child) =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Center()[child]];

    /// <summary>ウィンドウ 1: クリックカウンタ (状態は UI 毎に独立)。</summary>
    private static Widget BuildCounter()
    {
        var count = new Signal<int>(0);
        return Frame(VStack(12)[
            Text($"count: {count}", 20, color: Bind.From(() => UiTheme.T.Text)),
            Button(_ => count.Value++, "+1")]);
    }

    /// <summary>ウィンドウ 2: トグル (counter とツリー/見た目が異なる = 判別用)。</summary>
    private static Widget BuildToggle()
    {
        var on = new Signal<bool>(false);
        return Frame(VStack(10)[
            Text("Second window", 18, color: Bind.From(() => UiTheme.T.Text)),
            Check(on, "toggle me"),
            Text($"state: {on}", 14, color: Bind.From(() => UiTheme.T.Text))]);
    }

    /// <summary>オフスクリーン UI: ウィンドウを持たないが /trees に出て入力/ui.set の対象になる。</summary>
    private static Widget BuildStatus()
    {
        var pings = new Signal<int>(0);
        return Frame(VStack(8)[
            Text("offscreen status", 16, color: Bind.From(() => UiTheme.T.Text)),
            Text($"pings: {pings}", 14, color: Bind.From(() => UiTheme.T.Text)),
            Button(_ => pings.Value++, "ping")]);
    }
}
