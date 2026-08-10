using Luxel.Graphics.Abstraction;

namespace Luxel.Graphics;

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

    /// <summary>記録済みコマンドを非同期投入する。非同期 backend では GPU 完了まで await する。</summary>
    public ValueTask SubmitAsync(GpuCommandBuffer commandBuffer, CancellationToken cancellationToken = default)
        => _queue is IAsyncGpuBackendQueue asyncQueue
            ? asyncQueue.SubmitAsync(commandBuffer.Backend, cancellationToken)
            : SubmitSynchronously(commandBuffer);

    /// <summary>キューが空になるまで非同期に待つ。同期 backend では完了済み ValueTask を返す。</summary>
    public ValueTask WaitIdleAsync(CancellationToken cancellationToken = default)
        => _queue is IAsyncGpuBackendQueue asyncQueue
            ? asyncQueue.WaitIdleAsync(cancellationToken)
            : WaitSynchronously();

    private ValueTask SubmitSynchronously(GpuCommandBuffer commandBuffer)
    {
        _queue.Submit(commandBuffer.Backend);
        _queue.WaitIdle();
        return ValueTask.CompletedTask;
    }

    private ValueTask WaitSynchronously()
    {
        _queue.WaitIdle();
        return ValueTask.CompletedTask;
    }

    /// <summary>キューが空になるまで待つ。</summary>
    public void WaitIdle() => _queue.WaitIdle();
}
