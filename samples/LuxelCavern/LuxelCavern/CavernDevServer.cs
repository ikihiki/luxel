using Luxel;
using Luxel.DevTools;
using Luxel.Diagnostics;

namespace LuxelCavern;

/// <summary>
/// ゲームに DevTools サーバ (Q05 の <see cref="DebugServer"/>) を結線する薄いラッパ (<c>--devtools</c> 時のみ)。
/// <see cref="DevToolsListener"/> がエンジンの診断 (<see cref="EngineDiagnostics"/>) を購読 → <see cref="DevStats"/>
/// (fps/状態/HP など、<c>CavernDevOverlay.PublishStats</c> が毎フレーム載せる) が <c>/custom</c> に流れる。
/// さらにゲームのフレームバッファを <c>Luxel.Frame</c> として配信し、ブラウザ (Claude in Chrome) で
/// <c>{Url}?...</c> / <c>/frame?format=png</c> から実画面を見られるようにする。購読者ゼロ時は zero-cost。
/// </summary>
sealed class CavernDevServer : IDisposable
{
    private readonly DevToolsListener _listener;
    private readonly DebugServer _server;
    private byte[]? _tight;
    private int _skip;

    public string Url => _server.Url;

    public CavernDevServer(EngineCommands commands, int port)
    {
        _listener = new DevToolsListener(commands);   // "Luxel" 診断を購読 (以降 EngineDiagnostics.IsEnabled=true)
        _server = new DebugServer(_listener, port);
        _server.Start();
    }

    /// <summary>ゲームのフレームバッファを DevTools の <c>/frame</c> へ配信する (~15fps、購読者が居るときだけ)。
    /// <paramref name="fb"/> は RGBA8 (Color2D は R が下位バイト) でパディング幅 <paramref name="paddedW"/>。</summary>
    public void EmitFrame(GpuBuffer fb, int paddedW, int w, int h)
    {
        if (!EngineDiagnostics.IsEnabled(EngineDiagnostics.Frame)) return;
        if (--_skip > 0) return;
        _skip = 4;

        _tight ??= new byte[w * h * 4];
        Span<byte> src = fb.Span<byte>(paddedW * h * 4);
        for (int y = 0; y < h; y++)
            src.Slice(y * paddedW * 4, w * 4).CopyTo(_tight.AsSpan(y * w * 4));   // パディング列を落として密 RGBA に
        EngineDiagnostics.Emit(EngineDiagnostics.Frame, new DiagFrame(w, h, _tight));   // listener が同期でコピー
    }

    public void Dispose()
    {
        _server.Dispose();
        _listener.Dispose();
    }
}
