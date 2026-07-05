using Luxel.UI;
using Windows.Win32.Foundation;

namespace Luxel.Platform;

/// <summary>
/// 単一ウィンドウ用の TSF 窓口 (AppWindow が使う)。実体は <see cref="TsfThread"/> (スレッド共有) +
/// <see cref="TsfDocument"/> (この窓の文書) の薄いラッパ — マルチウィンドウは WindowHost が両者を直接使う。
/// 失敗時は TSF 無効で続行 (WM_CHAR のみ)。
/// </summary>
internal sealed class TsfManager : IDisposable
{
    private TsfThread? _thread;
    private TsfDocument? _doc;

    public bool Active => _doc is not null;

    public TsfManager(UiHost host, Func<HWND> getHwnd)
    {
        _thread = TsfThread.Acquire();
        _doc = _thread?.CreateDocument(() => host, getHwnd);
        if (_thread is not null && _doc is null) { _thread.Release(); _thread = null; }
    }

    /// <summary>テキスト入力にフォーカスが入ったとき呼ぶ (IME を有効化)。</summary>
    public void Focus() => _doc?.Focus();

    /// <summary>テキスト入力からフォーカスが外れたとき呼ぶ。</summary>
    public void Unfocus() => _thread?.SetFocus(null);

    /// <summary>
    /// キー押下をまず TIP に渡す (TestKeyDown→KeyDown)。TIP が消費したら true (=変換に使われた)。
    /// false なら呼び出し側が UI 層 (UiHost.KeyDown) で処理する。programmatic 注入で擬似テストにも使える。
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
