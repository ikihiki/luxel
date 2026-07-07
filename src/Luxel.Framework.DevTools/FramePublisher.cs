using Luxel;
using Luxel.Diagnostics;

namespace Luxel.Framework.DevTools;

/// <summary>
/// ゲームの提示フレームを DevTools のライブビューへ配信する口 (Q05 E)。ゲームは提示ループで
/// <see cref="Publish"/> を呼ぶだけでよい (どのフロントエンドが購読しているかは意識しない)。
/// 購読者ゼロなら <see cref="EngineDiagnostics.IsEnabled"/> 判定だけで即 return する zero-cost。
/// </summary>
public interface IFramePublisher
{
    /// <summary>提示したフレームバッファ (RGBA8、行ピッチ <paramref name="paddedStridePixels"/>) を配信する。
    /// パディング列を落とした密 RGBA に変換して emit。購読者が居るときだけ実コストが発生する。</summary>
    void Publish(GpuBuffer framebuffer, int paddedStridePixels, int width, int height);
}

/// <summary>
/// <see cref="IFramePublisher"/> の既定実装。
///
/// <para><b>要点 (性能)</b>: 提示バッファ (<see cref="GpuMemoryKind.HostMapped"/>) は host-visible な
/// GPU メモリで、多くの環境で **write-combined/uncached** = **CPU 読み戻しが激遅** (960×540 で ~75ms/frame 実測)。
/// そのまま毎フレーム CPU で読むと購読中ゲームが 10fps 台へ落ちる。そこで <see cref="GpuMemoryKind.HostCached"/>
/// の readback バッファへ **GPU コピー**してから CPU で読む (cached = 高速)。tight バッファと
/// <see cref="DiagFrame"/> も使い回すので、購読中でも main スレッド割り当てはゼロ。</para>
///
/// さらにライブビューは配信レートを <see cref="MaxFps"/> に間引く (デバッグ表示に 60fps 全量は不要 = GPU 読み戻し
/// submit のコストを更に下げる)。off (購読者ゼロ) のときは何もしない。
/// </summary>
internal sealed class FramePublisher : IFramePublisher, IDisposable
{
    private readonly GpuDevice _device;
    private byte[]? _tight;
    private DiagFrame? _frame;
    private GpuBuffer? _readback;   // HostCached (CPU 読みが速い) — WC framebuffer の GPU コピー先
    private int _w, _h, _readbackBytes;

    // ライブビュー配信の上限レート。60fps 全量は不要で、GPU 読み戻し submit を毎フレーム出すのも無駄なので間引く。
    private const double MaxFps = 30.0;
    private long _lastTicks;

    public FramePublisher(GpuDevice device) => _device = device;

    public void Publish(GpuBuffer framebuffer, int paddedStridePixels, int width, int height)
    {
        if (!EngineDiagnostics.IsEnabled(EngineDiagnostics.Frame)) return;
        if (width <= 0 || height <= 0) return;

        // レート間引き: MaxFps を超える呼び出しは捨てる (ゲームの提示レートには影響しない)。
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double sinceMs = (now - _lastTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        if (_lastTicks != 0 && sinceMs < 1000.0 / MaxFps) return;
        _lastTicks = now;

        int len = width * height * 4;
        int paddedBytes = paddedStridePixels * height * 4;
        if (_tight is null || _w != width || _h != height)
        {
            _tight = new byte[len];
            _frame = new DiagFrame(width, height, _tight);   // 寸法が変わったときだけ record を作り直す
            _w = width; _h = height;
        }
        if (_readback is null || _readbackBytes != paddedBytes)
        {
            _readback?.Dispose();
            _readback = _device.Malloc((ulong)paddedBytes, GpuMemoryKind.HostCached);   // READBACK: CPU 読みが速い
            _readbackBytes = paddedBytes;
        }

        // WC の framebuffer を GPU で cached readback へコピー (GPU の読みは WC でも速い)。
        using (GpuCommandBuffer cmd = _device.MainQueue.StartCommandRecording())
        {
            cmd.CopyBuffer(framebuffer, _readback, (ulong)paddedBytes);
            cmd.Finish();
            _device.MainQueue.SubmitAndWait(cmd);
        }

        // cached バッファを CPU 読み (高速) → パディング列を落として密 RGBA に。
        ReadOnlySpan<byte> src = _readback.Span<byte>(paddedBytes);
        int tightStride = width * 4, paddedStrideBytes = paddedStridePixels * 4;
        for (int y = 0; y < height; y++)
            src.Slice(y * paddedStrideBytes, tightStride).CopyTo(_tight.AsSpan(y * tightStride));
        EngineDiagnostics.Emit(EngineDiagnostics.Frame, _frame!);
    }

    public void Dispose() => _readback?.Dispose();
}
