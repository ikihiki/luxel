namespace Luxel.Diagnostics;

/// <summary>
/// ウィンドウ一覧とウィンドウピクセルの読み取り契約 (DevTools の DebugServer が /windows, /winframe で配信)。
/// 実装は Platform 側 (WindowManager) — 双方が core のこの型のみに依存する。
/// 操作 (window.* と UI 入力) はここを通らず、共有 <see cref="EngineCommands"/> の op として登録する
/// (window.* は WindowManager、UI 入力は UiHostCommands.RegisterDefaults(cmds, registry))。
/// UI ツリーは /trees (Luxel.Trees イベント)、UI オフスクリーン画像は /uiframe (Luxel.UiFrame) — ウィンドウとは別リソース。
/// </summary>
public interface IWindowRemoteHost
{
    /// <summary>全ウィンドウの一覧 JSON (id/title/x/y/w/h/visible/focused/uis)。サーバスレッドから呼ばれる。</summary>
    string ListWindowsJson();

    /// <summary>ウィンドウ id の最新フレーム (8B ヘッダ w,h LE + tight RGBA)。sinceRev と同じなら body=null。未知 id は rev=-1。</summary>
    (byte[]? body, long rev) GetFrame(int id, long? sinceRev);
}
