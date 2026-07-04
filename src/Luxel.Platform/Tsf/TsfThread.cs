using Luxel.UI;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.TextServices;

namespace Luxel.Platform;

/// <summary>
/// TSF のスレッドレベル資源 (ITfThreadMgr / ITfKeystrokeMgr)。**1 スレッドに 1 つ** — マルチウィンドウでは
/// 全ウィンドウがこれを共有し、ウィンドウ毎の <see cref="TsfDocument"/> を <see cref="SetFocus"/> で切り替える
/// (TSF の標準モデル: thread mgr 1 : document mgr N)。
/// <see cref="Acquire"/> は参照カウント式 — 利用者 (AppWindow の TsfManager / WindowHost) 毎に取得し、
/// <see cref="Release"/> で最後の 1 つが Deactivate する。STA 必須 (MTA では初期化失敗 → null)。
/// </summary>
internal sealed unsafe class TsfThread
{
    [ThreadStatic] private static TsfThread? _current;

    private ITfThreadMgr? _mgr;
    private ITfKeystrokeMgr? _keystroke;
    private uint _clientId;
    private int _refs;

    private TsfThread() { }

    /// <summary>このスレッドの TSF を取得する (初回は Activate)。失敗時 null (=WM_CHAR フォールバック)。</summary>
    public static TsfThread? Acquire()
    {
        if (_current is { } cur) { cur._refs++; return cur; }
        var t = new TsfThread();
        try
        {
            PInvoke.CoInitializeEx(null, COINIT.COINIT_APARTMENTTHREADED);
            Guid clsid = PInvoke.CLSID_TF_ThreadMgr;
            PInvoke.CoCreateInstance(in clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, out ITfThreadMgr? mgr);
            mgr!.Activate(out t._clientId);
            t._mgr = mgr;
            try { t._keystroke = (ITfKeystrokeMgr)mgr; }   // STA でのみ QI 可。失敗時はキー先取りなしで続行
            catch { t._keystroke = null; }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[TSF] 初期化失敗 (WM_CHAR にフォールバック): {e.Message}");
            return null;
        }
        t._refs = 1;
        _current = t;
        return t;
    }

    /// <summary>ウィンドウ (文書) 単位の TSF コンテキストを作る。<paramref name="getScale"/> は
    /// DPI スケール (caret 矩形の論理→物理変換、null = 1)。</summary>
    public TsfDocument? CreateDocument(Func<UiHost?> getHost, Func<HWND> getHwnd, Func<float>? getScale = null)
    {
        if (_mgr is null) return null;
        try
        {
            _mgr.CreateDocumentMgr(out ITfDocumentMgr? doc);
            var store = new TsfTextStore(getHost, getHwnd, getScale);
            doc!.CreateContext(_clientId, 0, store, out ITfContext? ctx, out uint _);
            doc.Push(ctx);
            store.AttachContext(ctx!);
            // display attribute (preedit 下線/対象節) 用に編集完了通知を購読 — 失敗しても入力は成立する
            try
            {
                if (ctx is ITfSource src)
                {
                    Guid iid = typeof(ITfTextEditSink).GUID;
                    src.AdviseSink(in iid, store, out uint _);
                }
            }
            catch (Exception e) { Console.Error.WriteLine($"[TSF] TextEditSink 購読失敗 (下線なしで続行): {e.Message}"); }
            return new TsfDocument(this, doc);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[TSF] 文書作成失敗: {e.Message}");
            return null;
        }
    }

    /// <summary>IME のフォーカス文書を切り替える (null で解除)。ウィンドウの WM_SETFOCUS から呼ぶ。</summary>
    public void SetFocus(TsfDocument? doc)
    {
        try { _mgr?.SetFocus(doc?.DocMgr); } catch { }
    }

    /// <summary>キー押下をまず TIP に渡す (TestKeyDown→KeyDown)。消費されたら true。
    /// スレッド単位 — TIP はフォーカス中の文書 (=フォーカス中のウィンドウ) に対して変換する。</summary>
    public bool HandleKeyDown(ushort vk, nint lParam)
    {
        if (_keystroke is null) return false;
        _keystroke.TestKeyDown(new WPARAM(vk), new LPARAM(lParam), out BOOL eaten);
        if (!eaten) return false;
        _keystroke.KeyDown(new WPARAM(vk), new LPARAM(lParam), out BOOL consumed);
        return consumed;
    }

    public bool HandleKeyUp(ushort vk, nint lParam)
    {
        if (_keystroke is null) return false;
        _keystroke.TestKeyUp(new WPARAM(vk), new LPARAM(lParam), out BOOL eaten);
        if (!eaten) return false;
        _keystroke.KeyUp(new WPARAM(vk), new LPARAM(lParam), out BOOL consumed);
        return consumed;
    }

    /// <summary>TSF コンパートメントで IME を ON + ひらがな/ローマ字入力モードにする (スレッド単位)。</summary>
    public void SetJapaneseInputMode()
    {
        if (_mgr is null) return;
        try
        {
            var cmgr = (ITfCompartmentMgr)_mgr;
            SetCompartmentI4(cmgr, PInvoke.GUID_COMPARTMENT_KEYBOARD_OPENCLOSE, 1);              // IME ON
            SetCompartmentI4(cmgr, PInvoke.GUID_COMPARTMENT_KEYBOARD_INPUTMODE_CONVERSION, 0x11); // NATIVE|ROMAN
        }
        catch (Exception e) { Console.Error.WriteLine($"[TSF] 入力モード設定失敗: {e.Message}"); }
    }

    private void SetCompartmentI4(ITfCompartmentMgr cmgr, Guid guid, int value)
    {
        cmgr.GetCompartment(in guid, out ITfCompartment comp);
        object boxed = value;                 // CsWin32 は VARIANT を in object に射影 (int → VT_I4)
        comp.SetValue(_clientId, boxed);
    }

    /// <summary>参照を返す。最後の利用者で Deactivate する。</summary>
    public void Release()
    {
        if (--_refs > 0) return;
        try { _mgr?.Deactivate(); } catch { }
        _mgr = null; _keystroke = null;
        if (_current == this) _current = null;
    }
}

/// <summary>ウィンドウ 1 枚分の TSF 文書 (ITfDocumentMgr + ITfContext + TsfTextStore)。</summary>
internal sealed class TsfDocument : IDisposable
{
    private readonly TsfThread _thread;
    internal ITfDocumentMgr? DocMgr { get; private set; }

    internal TsfDocument(TsfThread thread, ITfDocumentMgr doc)
    {
        _thread = thread;
        DocMgr = doc;
    }

    /// <summary>この文書へ IME フォーカスを移す (ウィンドウの WM_SETFOCUS で呼ぶ)。</summary>
    public void Focus() => _thread.SetFocus(this);

    public void Dispose()
    {
        try { DocMgr?.Pop(0); } catch { }
        DocMgr = null;
    }
}
