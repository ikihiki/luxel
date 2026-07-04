namespace Luxel.UI;

/// <summary>論理キー (programmatic 入力)。</summary>
public enum Key { None, Tab, Enter, Space, Escape, Left, Right, Up, Down, Home, End, Backspace, Delete, PageUp, PageDown, A, B, C, D, E, I, V, X, Y, Z }

public readonly record struct KeyEvent(Key Key, bool Shift = false, bool Ctrl = false, bool Alt = false);

/// <summary>アプリ全域ショートカットのキー組み合わせ (<see cref="UiHost.RegisterShortcut"/>)。
/// 配送は「フォーカス中コントロールがキーを消費しなかったときだけ」— エディタのタイプや
/// Ctrl+B (太字) 等、コントロール自身のキーバインドを奪わない。</summary>
public readonly record struct KeyGesture(Key Key, bool Ctrl = false, bool Shift = false, bool Alt = false);

/// <summary>フォーカス対象 (タブ順 = 登録順)。コントロールが Realize 時に登録する。</summary>
public sealed class FocusTarget
{
    public Action<bool>? OnFocus { get; init; }       // フォーカス取得/喪失
    public Func<KeyEvent, bool>? OnKey { get; init; }  // true=消費
    public Action<string>? OnText { get; init; }       // 文字入力 (host.Char)
    public Action<string>? OnCompose { get; init; }    // IME 編集中文字列 (host.Compose(string))
    public Action<ImeComposition>? OnComposeEx { get; init; }  // IME 状態 (host.Compose(comp))
    public Action<string>? OnCommit { get; init; }     // IME 確定 (host.Commit)
    public ITextInput? TextInput { get; init; }        // テキスト入力面 (TSF/caret 用)
}

/// <summary>スクロール対象 (wheel ルーティング用)。rect はノードのローカル座標 (判定は transform 追従)。</summary>
public sealed class ScrollTarget
{
    public required Luxel.TwoD.UiNode Node { get; init; }
    public required Rect Rect { get; init; }
    public Action<float>? OnScroll { get; init; }                  // 引数 = ホイール量
    public Action<float, float, float>? OnScrollPos { get; init; } // ローカル x,y + ホイール量 (指定時優先)
}
