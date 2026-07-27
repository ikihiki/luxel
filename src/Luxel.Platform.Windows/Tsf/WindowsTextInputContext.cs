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
