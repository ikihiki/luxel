using Windows.Win32.Foundation;

namespace Luxel.Platform.Windows;

/// <summary>Connects a platform window to the Windows Text Services Framework.</summary>
public sealed class WindowsTextInputContext : IWindowTextInputContext
{
    private readonly NativeWindow _window;
    private TsfThread? _thread;
    private TsfDocument? _document;
    private bool _keyEatenByTip;

    public WindowsTextInputContext(NativeWindow window, Func<ITextInputClient?> getClient, Func<float>? getScale = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(getClient);
        _window = window;
        if (getClient() is null) return;

        _thread = TsfThread.Acquire();
        _document = _thread?.CreateDocument(getClient, () => new global::Windows.Win32.Foundation.HWND(window.Handle), getScale);
        if (_thread is not null && _document is null)
        {
            _thread.Release();
            _thread = null;
            return;
        }

        if (window.GetFeature<IWin32RawKeyInput>() is { } rawKeyInput)
            rawKeyInput.KeyPreFilter = (vk, lp) => _keyEatenByTip = _thread?.HandleKeyDown(vk, lp) ?? false;
        window.FocusChanged += OnFocusChanged;
        if (window.IsFocused) _document?.Focus();
    }

    public bool Active => _document is not null;
    public bool ShouldDispatchTextInput => !Active || !_keyEatenByTip;

    public void Focus() => _document?.Focus();
    public void SetJapaneseInputMode() => _thread?.SetJapaneseInputMode();

    private void OnFocusChanged(bool focused)
    {
        if (focused) _document?.Focus();
    }

    public void Dispose()
    {
        _window.FocusChanged -= OnFocusChanged;
        if (_window.GetFeature<IWin32RawKeyInput>() is { } rawKeyInput)
            rawKeyInput.KeyPreFilter = null;
        _document?.Dispose();
        _document = null;
        _thread?.Release();
        _thread = null;
    }
}

/// <summary>
/// 単一ウィンドウ用の TSF 窓口 (AppWindow が使う)。実体は <see cref="TsfThread"/> (スレッド共有) +
/// <see cref="TsfDocument"/> (この窓の文書) の薄いラッパ。
/// 失敗時は TSF 無効で続行 (WM_CHAR のみ)。
/// </summary>
internal sealed class TsfManager : IDisposable
{
    private TsfThread? _thread;
    private TsfDocument? _doc;

    public bool Active => _doc is not null;

    public TsfManager(ITextInputClient client, Func<HWND> getHwnd)
    {
        _thread = TsfThread.Acquire();
        _doc = _thread?.CreateDocument(() => client, getHwnd);
        if (_thread is not null && _doc is null) { _thread.Release(); _thread = null; }
    }

    /// <summary>テキスト入力にフォーカスが入ったとき呼ぶ (IME を有効化)。</summary>
    public void Focus() => _doc?.Focus();

    /// <summary>テキスト入力からフォーカスが外れたとき呼ぶ。</summary>
    public void Unfocus() => _thread?.SetFocus(null);

    /// <summary>
    /// キー押下をまず TIP に渡す (TestKeyDown→KeyDown)。TIP が消費したら true (=変換に使われた)。
    /// false なら呼び出し側が通常のキー入力経路で処理する。programmatic 注入で擬似テストにも使える。
    /// </summary>
    public bool HandleKeyDown(ushort vk, nint lParam) => _thread?.HandleKeyDown(vk, lParam) ?? false;

    public bool HandleKeyUp(ushort vk, nint lParam) => _thread?.HandleKeyUp(vk, lParam) ?? false;

    /// <summary>
    /// TSF コンパートメントで IME を ON + ひらがな/ローマ字入力モードにする。
    /// ITfKeystrokeMgr 注入経路で TIP に かな→漢字 変換させるのに必須 (IMM 設定はこの経路に効かない)。
    /// </summary>
    public void SetJapaneseInputMode() => _thread?.SetJapaneseInputMode();

    public void Dispose()
    {
        _doc?.Dispose(); _doc = null;
        _thread?.Release(); _thread = null;
    }
}
