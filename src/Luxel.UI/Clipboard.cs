namespace Luxel.UI;

/// <summary>クリップボード抽象 (OS API は Platform 層が実装、テストはフェイクを差す)。</summary>
public interface IClipboard
{
    string? GetText();
    void SetText(string text);
}

/// <summary>プロセス共有のクリップボード (OS クリップボードは本質的にグローバル)。
/// Platform 層 (Win32WindowBackend/AppWindow) が起動時に実装を設定する。null = 貼り付け/コピー無効。</summary>
public static class UiClipboard
{
    public static IClipboard? Instance { get; set; }
}
