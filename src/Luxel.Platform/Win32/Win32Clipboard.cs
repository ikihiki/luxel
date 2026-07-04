using System.Runtime.InteropServices;
using Luxel.UI;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;

namespace Luxel.Platform;

/// <summary>Win32 クリップボード (CF_UNICODETEXT)。<see cref="UiClipboard.Instance"/> へ登録して使う。</summary>
public sealed unsafe class Win32Clipboard : IClipboard
{
    private const uint CF_UNICODETEXT = 13;

    public string? GetText()
    {
        if (!PInvoke.OpenClipboard(default)) return null;
        try
        {
            HANDLE h = PInvoke.GetClipboardData(CF_UNICODETEXT);
            if (h.IsNull) return null;
            var hg = new HGLOBAL((void*)h.Value);
            void* p = PInvoke.GlobalLock(hg);
            if (p is null) return null;
            try { return new string((char*)p); }
            finally { PInvoke.GlobalUnlock(hg); }
        }
        finally { PInvoke.CloseClipboard(); }
    }

    public void SetText(string text)
    {
        text ??= "";
        if (!PInvoke.OpenClipboard(default)) return;
        try
        {
            PInvoke.EmptyClipboard();
            nuint bytes = (nuint)((text.Length + 1) * sizeof(char));
            HGLOBAL hg = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, bytes);
            if (hg == default) return;
            void* p = PInvoke.GlobalLock(hg);
            if (p is null) return;
            fixed (char* src = text)
            {
                Buffer.MemoryCopy(src, p, bytes, (ulong)(text.Length * sizeof(char)));
                ((char*)p)[text.Length] = '\0';
            }
            PInvoke.GlobalUnlock(hg);
            // SetClipboardData 成功後はメモリの所有権が OS に移る (解放しない)
            PInvoke.SetClipboardData(CF_UNICODETEXT, new HANDLE((void*)hg.Value));
        }
        finally { PInvoke.CloseClipboard(); }
    }
}
