using Luxel;
using Luxel.Platform;
using Luxel.Typography;
using Luxel.UI;
using LuxelCavern.Core;

// LuxelCavern — スタンドアロン起動 (capstone ①, Stage A: タイトル画面のみ)。
//   LuxelCavern[.exe] [vk|dx] [--frames N]
//     vk (既定) / dx : GPU バックエンド
//     --frames N      : N フレーム描画して自動終了 (publish スモーク・CI 用。既定は閉じるまで対話実行)
//
// 実窓 (Win32) と TSF は STA スレッドを要求するため、Gallery の Program.cs と同様に
// エントリで専用スレッドを張って SetApartmentState(STA) してから実処理を回す。

string backend = "vk";
int frames = 0;   // 0 = 対話実行 (閉じるまで)
for (int i = 0; i < args.Length; i++)
{
    string a = args[i].ToLowerInvariant();
    if (a is "vk" or "vulkan") backend = "vk";
    else if (a is "dx" or "d3d12") backend = "dx";
    else if (a == "--frames" && i + 1 < args.Length && int.TryParse(args[i + 1], out int n)) { frames = Math.Max(1, n); i++; }
}

int exit = 1;
var thread = new Thread(() => exit = Run(backend, frames)) { Name = "LuxelCavern-Main" };
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
        // ダークテーマを既定テーマ島に設定 — Kit の複合ヘルパ (Heading/Muted) はこれを購読する
        UiTheme.Current.Value = Theme.Dark;

        using GpuDevice device = CreateDevice(backend);
        Console.WriteLine($"=== Luxel Cavern (backend: {backend}, device: {device.Name}) ===");

        using VectorFont font = CavernAssets.LoadBodyFont();   // 同梱フォント (システム非依存)
        using var window = new AppWindow(device, font, 1280, 720, TitleScreen.GameTitle);

        window.SetRoot(TitleScreen.Build(new TitleScreen.Actions(
            Start: () => Console.WriteLine("[title] はじめる (Playing への遷移は Stage B で実装)"),
            Settings: () => Console.WriteLine("[title] せってい (設定画面は ToDo 15 で実装)"),
            Quit: () => { Console.WriteLine("cavern: 「おわる」で終了"); window.Close(); })));

        if (frames > 0)
        {
            window.RunFrames(frames);   // N フレーム回して閉じる (スモーク)
            Console.WriteLine($"cavern: {frames} フレーム描画して終了 (smoke ok)");
        }
        else
        {
            window.Run();   // 閉じられる (× または「おわる」) までフレームを回す
        }
        return 0;
    }
    catch (Exception ex)
    {
        // WinExe はコンソールを持たないため、起動失敗を exe 隣のログにも残す (publish スモークの診断用)
        string log = Path.Combine(AppContext.BaseDirectory, "cavern-crash.log");
        try { File.WriteAllText(log, ex.ToString()); } catch { /* ログ書き込み失敗は無視 */ }
        Console.Error.WriteLine(ex);
        return 1;
    }
}
