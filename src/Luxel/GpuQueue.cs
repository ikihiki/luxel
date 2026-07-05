using Luxel.Abstraction;

namespace Luxel;

/// <summary>コマンドの記録開始と投入を行うキュー。</summary>
public sealed class GpuQueue
{
    private readonly IGpuBackendQueue _queue;

    internal GpuQueue(IGpuBackendQueue queue) => _queue = queue;

    /// <summary>使い捨てコマンドバッファの記録を開始する (<c>gpuStartCommandRecording</c>)。</summary>
    public GpuCommandBuffer StartCommandRecording() => new(_queue.StartCommandRecording());

    /// <summary>記録済みコマンドバッファを投入する。<see cref="GpuCommandBuffer.Finish"/> 済みであること。</summary>
    public void Submit(GpuCommandBuffer commandBuffer) => _queue.Submit(commandBuffer.Backend);

    /// <summary>コマンドを投入し、完了まで待つ簡易ヘルパ。</summary>
    public void SubmitAndWait(GpuCommandBuffer commandBuffer)
    {
        _queue.Submit(commandBuffer.Backend);
        _queue.WaitIdle();
    }

    /// <summary>キューが空になるまで待つ。</summary>
    public void WaitIdle() => _queue.WaitIdle();
}
