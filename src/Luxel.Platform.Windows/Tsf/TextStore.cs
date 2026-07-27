using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.TextServices;
using DrawPoint = System.Drawing.Point;

namespace Luxel.Platform.Windows;

/// <summary>
/// TSF の文書ストア。フォーカス中の <see cref="ITextInputClient"/> へ読み書きを委譲する。
/// 注: 構造的なブリッジ。実 IME での日本語変換挙動は対話(手動)検証が必要 (ヘッドレス検証不可)。
/// CsWin32 は ITextStoreACP を void 返し(失敗時 throw)で生成するため、未対応は COMException を投げる。
/// **display attribute (ED-M5)**: <see cref="ITfContextOwnerCompositionSink"/> で合成範囲を追跡し、
/// <see cref="ITfTextEditSink"/> の OnEndEdit で GUID_PROP_ATTRIBUTE を読んで変換対象節
/// (TF_ATTR_TARGET_CONVERTED) を特定 — preedit の下線/強調を装飾専用経路
/// (<see cref="ITextInputClient.SetCompositionHighlight"/>) でクライアントへ流す (preedit 本文は従来どおり SetText)。
/// </summary>
internal sealed unsafe class TsfTextStore : ITextStoreACP, ITfContextOwnerCompositionSink, ITfTextEditSink
{
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int TS_E_NOLAYOUT = unchecked((int)0x80040206);

    private readonly Func<ITextInputClient?> _getClient;   // 遅延解決 — content の IME 対象は動的に変わりうる
    private readonly Func<HWND> _getHwnd;
    private readonly Func<float> _getScale;    // DPI スケール (caret 矩形: 論理 → 物理クライアント px)
    private ITextStoreACPSink? _sink;

    private ITfContext? _context;                       // AttachContext で受け取る (attribute 読み用)
    private ITfCompositionView? _composition;           // 進行中の合成 (null = なし)
    private ITfCategoryMgr? _catMgr;
    private ITfDisplayAttributeMgr? _daMgr;
    private bool _attrBroken;                           // attribute 読みが失敗したら以後 range 下線のみ

    public TsfTextStore(ITextInputClient client, Func<HWND> getHwnd) : this(() => client, getHwnd) { }

    public TsfTextStore(Func<ITextInputClient?> getClient, Func<HWND> getHwnd, Func<float>? getScale = null)
    { _getClient = getClient; _getHwnd = getHwnd; _getScale = getScale ?? (() => 1f); }

    /// <summary>文書の ITfContext を受け取る (TsfThread.CreateDocument が Push 後に呼ぶ)。</summary>
    public void AttachContext(ITfContext context) => _context = context;

    private ITextInputClient? Client => _getClient();
    private string Text => Client?.Text ?? "";
    private static void Fail(int hr) => throw new COMException(null, hr);

    public void AdviseSink(Guid* riid, object punk, uint dwMask) => _sink = punk as ITextStoreACPSink;
    public void UnadviseSink(object punk) => _sink = null;

    public void RequestLock(uint dwLockFlags, HRESULT* phrSession)
    {
        if (_sink == null) { *phrSession = new HRESULT(unchecked((int)0x80004005)); return; }
        try { _sink.OnLockGranted((TEXT_STORE_LOCK_FLAGS)dwLockFlags); *phrSession = new HRESULT(0); }
        catch (COMException ex) { *phrSession = new HRESULT(ex.HResult); }
    }

    public void GetStatus(TS_STATUS* pdcs) { pdcs->dwDynamicFlags = 0; pdcs->dwStaticFlags = 0; }

    public void QueryInsert(int acpTestStart, int acpTestEnd, uint cch, out int pacpResultStart, out int pacpResultEnd)
    {
        int len = Text.Length;
        pacpResultStart = Math.Clamp(acpTestStart, 0, len);
        pacpResultEnd = Math.Clamp(acpTestEnd, 0, len);
    }

    public void GetSelection(uint ulIndex, uint ulCount, TS_SELECTION_ACP* pSelection, out uint pcFetched)
    {
        pcFetched = 0;
        if (ulCount == 0) return;
        (int start, int length) = Client?.Selection ?? (0, 0);
        pSelection[0].acpStart = start;
        pSelection[0].acpEnd = start + length;
        pSelection[0].style.ase = TsActiveSelEnd.TS_AE_END;
        pSelection[0].style.fInterimChar = false;
        pcFetched = 1;
    }

    public void SetSelection(uint ulCount, TS_SELECTION_ACP* pSelection)
    {
        if (ulCount > 0) Client?.Select(pSelection[0].acpStart, pSelection[0].acpEnd);
    }

    public void GetText(int acpStart, int acpEnd, PWSTR pchPlain, uint cchPlainReq,
        out uint pcchPlainRet, TS_RUNINFO* prgRunInfo, uint cRunInfoReq, out uint pcRunInfoRet, out int pacpNext)
    {
        string t = Text;
        int start = Math.Clamp(acpStart, 0, t.Length);
        int end = acpEnd < 0 ? t.Length : Math.Clamp(acpEnd, start, t.Length);
        int n = Math.Min(end - start, (int)cchPlainReq);
        for (int i = 0; i < n; i++) pchPlain.Value[i] = t[start + i];
        pcchPlainRet = (uint)n;
        pcRunInfoRet = 0;
        if (cRunInfoReq > 0 && n > 0) { prgRunInfo[0].uCount = (uint)n; prgRunInfo[0].type = TsRunType.TS_RT_PLAIN; pcRunInfoRet = 1; }
        pacpNext = start + n;
    }

    public void SetText(uint dwFlags, int acpStart, int acpEnd, PCWSTR pchText, uint cch, TS_TEXTCHANGE* pChange)
    {
        string s = cch > 0 ? new string(pchText.Value, 0, (int)cch) : "";
        Client?.Replace(acpStart, acpEnd, s);
        if (pChange != null) { pChange->acpStart = acpStart; pChange->acpOldEnd = acpEnd; pChange->acpNewEnd = acpStart + s.Length; }
    }

    public void InsertTextAtSelection(uint dwFlags, PCWSTR pchText, uint cch,
        out int pacpStart, out int pacpEnd, TS_TEXTCHANGE* pChange)
    {
        string s = cch > 0 ? new string(pchText.Value, 0, (int)cch) : "";
        (int start, int length) = Client?.Selection ?? (0, 0);
        Client?.Replace(start, start + length, s);
        pacpStart = start; pacpEnd = start + s.Length;
        if (pChange != null) { pChange->acpStart = start; pChange->acpOldEnd = start + length; pChange->acpNewEnd = start + s.Length; }
    }

    public void GetEndACP(out int pacp) => pacp = Text.Length;
    public void GetActiveView(out uint pvcView) => pvcView = 0;
    public void GetWnd(uint vcView, HWND* phwnd) => *phwnd = _getHwnd();

    public void GetTextExt(uint vcView, int acpStart, int acpEnd, RECT* prc, BOOL* pfClipped)
    {
        if (Client?.CaretRect is not TextInputRect c) { Fail(TS_E_NOLAYOUT); return; }
        float s = _getScale();   // caret は UI 論理 px → 物理クライアント px (候補ウィンドウ位置用)
        var tl = new DrawPoint((int)(c.X * s), (int)(c.Y * s));
        var br = new DrawPoint((int)((c.X + MathF.Max(c.Width, 1)) * s), (int)((c.Y + c.Height) * s));
        HWND hwnd = _getHwnd();
        PInvoke.ClientToScreen(hwnd, ref tl);
        PInvoke.ClientToScreen(hwnd, ref br);
        prc->left = tl.X; prc->top = tl.Y; prc->right = br.X; prc->bottom = br.Y;
        *pfClipped = false;
    }

    public void GetScreenExt(uint vcView, RECT* prc)
    {
        HWND hwnd = _getHwnd();
        PInvoke.GetClientRect(hwnd, out RECT r);
        var tl = new DrawPoint(r.left, r.top);
        var br = new DrawPoint(r.right, r.bottom);
        PInvoke.ClientToScreen(hwnd, ref tl);
        PInvoke.ClientToScreen(hwnd, ref br);
        prc->left = tl.X; prc->top = tl.Y; prc->right = br.X; prc->bottom = br.Y;
    }

    public void GetACPFromPoint(uint vcView, DrawPoint* ptScreen, uint dwFlags, out int pacp) { pacp = 0; Fail(E_NOTIMPL); }

    // attributes (未対応)
    public void RequestSupportedAttrs(uint dwFlags, uint cFilterAttrs, Guid* paFilterAttrs) { }
    public void RequestAttrsAtPosition(int acpPos, uint cFilterAttrs, Guid* paFilterAttrs, uint dwFlags) { }
    public void RequestAttrsTransitioningAtPosition(int acpPos, uint cFilterAttrs, Guid* paFilterAttrs, uint dwFlags) { }
    public void FindNextAttrTransition(int acpStart, int acpHalt, uint cFilterAttrs, Guid* paFilterAttrs, uint dwFlags,
        out int pacpNext, BOOL* pfFound, out int plFoundOffset)
    { pacpNext = acpHalt; *pfFound = false; plFoundOffset = 0; }
    public void RetrieveRequestedAttrs(uint ulCount, TS_ATTRVAL[] paAttrVals, out uint pcFetched) => pcFetched = 0;

    // ---- ITfContextOwnerCompositionSink (合成範囲の追跡 — display attribute 用) ----

    public void OnStartComposition(ITfCompositionView pComposition, BOOL* pfOk)
    {
        _composition = pComposition;
        *pfOk = true;
    }

    public void OnUpdateComposition(ITfCompositionView pComposition, ITfRange pRangeNew)
        => _composition = pComposition;

    public void OnEndComposition(ITfCompositionView pComposition)
    {
        _composition = null;
        try { Client?.SetCompositionHighlight(0, 0, 0, 0); } catch { }
    }

    // ---- ITfTextEditSink (編集完了ごとに合成範囲 + 変換対象節を読み UI へ流す) ----

    public void OnEndEdit(ITfContext pic, uint ecReadOnly, ITfEditRecord pEditRecord)
    {
        try { PublishCompositionHighlight(pic, ecReadOnly); }
        catch { /* 装飾は任意機能 — 失敗しても入力自体は成立させる */ }
    }

    private void PublishCompositionHighlight(ITfContext pic, uint ec)
    {
        if (_composition is not ITfCompositionView comp) return;
        comp.GetRange(out ITfRange range);
        if (range is not ITfRangeACP acp) return;
        acp.GetExtent(out int start, out int len);
        if (len <= 0) { Client?.SetCompositionHighlight(0, 0, 0, 0); return; }

        (int ts, int tl) = _attrBroken ? (0, 0) : ReadTargetSegment(pic, ec, range, start, len);
        Client?.SetCompositionHighlight(start, len, ts, tl);
    }

    /// <summary>GUID_PROP_ATTRIBUTE を列挙して TF_ATTR_TARGET_CONVERTED の範囲 (変換対象節) を得る。</summary>
    private (int start, int len) ReadTargetSegment(ITfContext pic, uint ec, ITfRange compRange, int compStart, int compLen)
    {
        try
        {
            if (_catMgr is null)
            {
                Guid catClsid = PInvoke.CLSID_TF_CategoryMgr;
                PInvoke.CoCreateInstance(in catClsid, null, CLSCTX.CLSCTX_INPROC_SERVER, out ITfCategoryMgr? cm);
                Guid daClsid = PInvoke.CLSID_TF_DisplayAttributeMgr;
                PInvoke.CoCreateInstance(in daClsid, null, CLSCTX.CLSCTX_INPROC_SERVER, out ITfDisplayAttributeMgr? dm);
                _catMgr = cm;
                _daMgr = dm;
            }
            if (_catMgr is null || _daMgr is null) { _attrBroken = true; return (0, 0); }

            Guid propGuid = PInvoke.GUID_PROP_ATTRIBUTE;
            pic.GetProperty(in propGuid, out ITfProperty prop);
            prop.EnumRanges(ec, out IEnumTfRanges ranges, compRange);

            int tStart = 0, tEnd = 0;
            var buf = new ITfRange[1];
            while (true)
            {
                ranges.Next(1, buf, out uint fetched);
                if (fetched == 0) break;
                ITfRange r = buf[0];
                object? val = null;
                try { prop.GetValue(ec, r, out val); } catch { continue; }
                if (val is not int atom || atom == 0) continue;
                _catMgr.GetGUID((uint)atom, out Guid attrGuid);
                ITfDisplayAttributeInfo? info = null;
                try { _daMgr.GetDisplayAttributeInfo(in attrGuid, out info, out _); } catch { continue; }
                if (info is null) continue;
                info.GetAttributeInfo(out TF_DISPLAYATTRIBUTE da);
                if (da.bAttr != TF_DA_ATTR_INFO.TF_ATTR_TARGET_CONVERTED) continue;
                if (r is not ITfRangeACP racp) continue;
                racp.GetExtent(out int rs, out int rl);
                if (tEnd == 0) { tStart = rs; tEnd = rs + rl; }
                else { tStart = Math.Min(tStart, rs); tEnd = Math.Max(tEnd, rs + rl); }
            }
            return tEnd > tStart ? (tStart, tEnd - tStart) : (0, 0);
        }
        catch
        {
            _attrBroken = true;   // 環境差で失敗したら以後は合成範囲の下線のみ
            return (0, 0);
        }
    }

    // embedded (未対応)
    public void GetFormattedText(int acpStart, int acpEnd, out IDataObject ppDataObject) { ppDataObject = null!; Fail(E_NOTIMPL); }
    public void GetEmbedded(int acpPos, Guid* rguidService, Guid* riid, out object ppunk) { ppunk = null!; Fail(E_NOTIMPL); }
    public void QueryInsertEmbedded(Guid* pguidService, FORMATETC* pFormatEtc, BOOL* pfInsertable) => *pfInsertable = false;
    public void InsertEmbedded(uint dwFlags, int acpStart, int acpEnd, IDataObject pDataObject, TS_TEXTCHANGE* pChange) => Fail(E_NOTIMPL);
    public void InsertEmbeddedAtSelection(uint dwFlags, IDataObject pDataObject, out int pacpStart, out int pacpEnd, TS_TEXTCHANGE* pChange)
    { pacpStart = 0; pacpEnd = 0; Fail(E_NOTIMPL); }
}
