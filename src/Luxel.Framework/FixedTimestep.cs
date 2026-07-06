namespace Luxel.Framework;

/// <summary>
/// 固定タイムステップの蓄積器。可変 dt を積み、固定刻み <see cref="FixedDt"/> ごとに
/// FixedUpdate を何回回すかを決める (フレームレート非依存の決定的シミュレーションの土台)。
///
/// <para><b>spiral of death 防止</b>: 1 フレームの実行回数は <see cref="MaxStepsPerFrame"/> で頭打ち。
/// 超過した余剰時間は**捨てる** (スローモーション化を避ける) — 捨てた分は <see cref="DroppedSteps"/> に計上する。</para>
///
/// <para><b>描画補間</b>: <see cref="Alpha"/> は「次の固定ステップまでの進捗 [0,1)」。
/// Render 時に <c>lerp(prevState, currState, Alpha)</c> すると 60Hz 表示 + 1/60 固定でもガタつかない。</para>
///
/// <para>浮動小数の蓄積誤差を避けるため内部は <c>double</c> で持つ (0.1×n の累積で割れる前例あり)。
/// GPU 非依存 — 単体テスト可能。</para>
/// </summary>
public sealed class FixedTimestep
{
    private double _accum;

    /// <summary>固定タイムステップ (秒)。既定 1/60。</summary>
    public double FixedDt { get; }

    /// <summary>1 フレームで回す固定ステップの上限 (spiral of death 防止)。既定 8。</summary>
    public int MaxStepsPerFrame { get; }

    /// <summary>次の固定ステップまでの進捗 [0,1)。描画補間係数。</summary>
    public float Alpha { get; private set; }

    /// <summary>これまでに実行した固定ステップの総数 (診断用)。</summary>
    public long TotalSteps { get; private set; }

    /// <summary>上限クランプで捨てた固定ステップの総数 (診断用)。0 でなければ処理落ちの兆候。</summary>
    public long DroppedSteps { get; private set; }

    /// <param name="fixedDt">固定刻み (秒)。0 以下は 1e-4 に丸める。</param>
    /// <param name="maxStepsPerFrame">1 フレームの実行上限 (最低 1)。</param>
    public FixedTimestep(double fixedDt = 1.0 / 60, int maxStepsPerFrame = 8)
    {
        FixedDt = Math.Max(1e-4, fixedDt);
        MaxStepsPerFrame = Math.Max(1, maxStepsPerFrame);
    }

    /// <summary>可変 dt を蓄積し、このフレームで回すべき固定ステップ数を返す。
    /// 返した回数だけ呼び出し側が FixedUpdate を回し、その後 <see cref="Alpha"/> を読む。
    /// 上限超過分は捨てて <see cref="DroppedSteps"/> に計上する (Alpha は常に [0,1))。</summary>
    public int Advance(double dt)
    {
        if (dt > 0) _accum += dt;
        int steps = 0;
        while (_accum >= FixedDt && steps < MaxStepsPerFrame)
        {
            _accum -= FixedDt;
            steps++;
        }
        // 上限に達してまだ余っている = 処理落ち。余剰を捨てて Alpha を [0,1) に保つ。
        if (steps >= MaxStepsPerFrame && _accum >= FixedDt)
        {
            long dropped = (long)(_accum / FixedDt);
            DroppedSteps += dropped;
            _accum -= dropped * FixedDt;
        }
        TotalSteps += steps;
        Alpha = (float)(_accum / FixedDt);
        return steps;
    }

    /// <summary>蓄積器を初期状態へ戻す (シーン再構築時など)。総数カウンタは維持。</summary>
    public void Reset()
    {
        _accum = 0;
        Alpha = 0;
    }
}
