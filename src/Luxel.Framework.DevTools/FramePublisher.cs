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
/// <see cref="IFramePublisher"/> の既定実装。tight バッファと <see cref="DiagFrame"/> を使い回し、
/// 購読中でも main スレッドの毎フレーム割り当てを出さない (F1 と対の書き手側)。
/// 変換後の tight RGBA は <see cref="Luxel.DevTools.FrameChannel"/> が seqlock でリングへコピーする。
/// </summary>
internal sealed class FramePublisher : IFramePublisher
{
    private byte[]? _tight;
    private DiagFrame? _frame;
    private int _w, _h;

    public void Publish(GpuBuffer framebuffer, int paddedStridePixels, int width, int height)
    {
        if (!EngineDiagnostics.IsEnabled(EngineDiagnostics.Frame)) return;
        if (width <= 0 || height <= 0) return;

        int len = width * height * 4;
        if (_tight is null || _w != width || _h != height)
        {
            _tight = new byte[len];
            _frame = new DiagFrame(width, height, _tight);   // 寸法が変わったときだけ作り直す
            _w = width; _h = height;
        }

        ReadOnlySpan<byte> src = framebuffer.Span<byte>(paddedStridePixels * height * 4);
        int tightStride = width * 4, paddedStrideBytes = paddedStridePixels * 4;
        for (int y = 0; y < height; y++)
            src.Slice(y * paddedStrideBytes, tightStride).CopyTo(_tight.AsSpan(y * tightStride));
        EngineDiagnostics.Emit(EngineDiagnostics.Frame, _frame!);
    }
}
