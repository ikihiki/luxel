using System.Collections.Concurrent;

namespace Luxel.Controls;

/// <summary>
/// シンタックスハイライトの共有ワーカー (プロセスで 1 スレッド)。
/// UI スレッドは <see cref="Enqueue"/> でジョブを投げ、結果は呼び出し側のキュー
/// (エディタの concurrent キュー等) へ積んでもらい、毎フレームのドレインで適用する —
/// 「解析は別スレッド、色付けは結果が来たら」の骨格。
/// snap 等の決定的な撮影は <see cref="WaitIdle"/> で静定を待つ。
/// </summary>
public static class HighlightQueue
{
    private static readonly BlockingCollection<Action> Jobs = new();
    private static int _pending;
    private static Thread? _worker;
    private static readonly object Gate = new();

    public static void Enqueue(Action job)
    {
        EnsureWorker();
        Interlocked.Increment(ref _pending);
        Jobs.Add(() =>
        {
            try { job(); }
            catch { /* トークナイズ失敗は無視 — 単色のまま表示される */ }
            finally { Interlocked.Decrement(ref _pending); }
        });
    }

    /// <summary>未処理ジョブなし (実行中含む)。</summary>
    public static bool Idle => Volatile.Read(ref _pending) == 0;

    /// <summary>全ジョブの完了を待つ (snap の決定性用)。true = 静定。</summary>
    public static bool WaitIdle(int timeoutMs = 3000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!Idle)
        {
            if (sw.ElapsedMilliseconds > timeoutMs) return false;
            Thread.Sleep(1);
        }
        return true;
    }

    private static void EnsureWorker()
    {
        if (_worker is not null) return;
        lock (Gate)
        {
            if (_worker is not null) return;
            _worker = new Thread(() =>
            {
                foreach (Action a in Jobs.GetConsumingEnumerable()) a();
            })
            { IsBackground = true, Name = "Luxel.Highlight" };
            _worker.Start();
        }
    }
}
