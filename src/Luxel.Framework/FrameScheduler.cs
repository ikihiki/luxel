namespace Luxel.Framework;

/// <summary>
/// Scene loop のフレーム発生方法を抽象化する。
/// <see cref="GameFrameScheduler"/> は常時フレームを発生させ、
/// <see cref="UiFrameScheduler"/> は変更要求があるときだけフレームを発生させる。
/// </summary>
public interface IFrameScheduler
{
    /// <summary>次のフレームを実行できるまで待つ。</summary>
    ValueTask WaitForNextFrameAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 状態変更、入力、リサイズなどにより 1 フレームを要求する。
    /// 同じ待機区間の複数要求は 1 フレームにまとめてよい。
    /// </summary>
    void RequestFrame();

    /// <summary>
    /// アニメーションや Scene transition の間、連続フレームを要求する。
    /// 戻り値を破棄すると要求を解除する。
    /// </summary>
    IDisposable AcquireContinuousFrames();
}

/// <summary>ゲーム向けの常時駆動 scheduler。すべての待機で pacer を 1 回進める。</summary>
public sealed class GameFrameScheduler : IFrameScheduler
{
    private readonly Func<CancellationToken, Task>? _waiter;
    private readonly int _frameDelayMs;

    public GameFrameScheduler(Func<CancellationToken, Task>? waiter = null, int frameDelayMs = 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameDelayMs);
        _waiter = waiter;
        _frameDelayMs = frameDelayMs;
    }

    public ValueTask WaitForNextFrameAsync(CancellationToken cancellationToken)
    {
        if (_waiter is not null) return new ValueTask(_waiter(cancellationToken));
        if (_frameDelayMs == 0) return ValueTask.CompletedTask;
        return new ValueTask(Task.Delay(_frameDelayMs, cancellationToken));
    }

    // 常時駆動なので個別要求と lease は追加の効果を持たない。
    public void RequestFrame() { }
    public IDisposable AcquireContinuousFrames() => NoopLease.Instance;

    private sealed class NoopLease : IDisposable
    {
        public static NoopLease Instance { get; } = new();
        public void Dispose() { }
    }
}

/// <summary>
/// UI向けの要求駆動 scheduler。初回と <see cref="RequestFrame"/> 呼び出し時だけフレームを発生させる。
/// continuous lease が存在する間はゲームと同様に pacer に従って連続駆動する。
/// </summary>
public sealed class UiFrameScheduler : IFrameScheduler
{
    private readonly Func<CancellationToken, Task>? _waiter;
    private readonly int _frameDelayMs;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly object _gate = new();
    private bool _frameRequested = true; // 初期 UI の構築・描画用
    private int _continuousLeases;

    public UiFrameScheduler(Func<CancellationToken, Task>? waiter = null, int frameDelayMs = 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameDelayMs);
        _waiter = waiter;
        _frameDelayMs = frameDelayMs;
    }

    public async ValueTask WaitForNextFrameAsync(CancellationToken cancellationToken)
    {
        bool continuous;
        while (true)
        {
            lock (_gate)
            {
                continuous = _continuousLeases > 0;
                if (continuous || _frameRequested)
                {
                    _frameRequested = false;
                    break;
                }
            }

            await _wake.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // 埋め込みホストの waiter は実行スレッド同期も担うため、要求駆動フレームでも必ず通す。
        if (_waiter is not null)
            await _waiter(cancellationToken);
        else if (continuous && _frameDelayMs > 0)
            await Task.Delay(_frameDelayMs, cancellationToken).ConfigureAwait(false);
    }

    public void RequestFrame()
    {
        lock (_gate)
        {
            if (_frameRequested) return;
            _frameRequested = true;
        }
        Wake();
    }

    public IDisposable AcquireContinuousFrames()
    {
        lock (_gate)
        {
            _continuousLeases++;
            // idle 待機との競合中に即 Dispose されても最低 1 フレームは発生させる。
            _frameRequested = true;
        }
        Wake();
        return new ContinuousLease(this);
    }

    private void ReleaseContinuousFrames()
    {
        lock (_gate)
        {
            if (_continuousLeases > 0) _continuousLeases--;
        }
    }

    private void Wake()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { } // 複数要求は 1 回に coalesce
    }

    private sealed class ContinuousLease(UiFrameScheduler owner) : IDisposable
    {
        private UiFrameScheduler? _owner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.ReleaseContinuousFrames();
        }
    }
}
