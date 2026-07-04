using System.Diagnostics;

namespace Luxel.Animation;

/// <summary>
/// アニメーションシステムが参照する時刻供給源。<see cref="AnimationPlayer.Update(IClock)"/> で渡す。
/// `Tick(dt)` の累積モデルではなく**絶対時刻**を提供する設計のため、`TrackEntry.StartTime`/`EndTime` は
/// Play 時に事前計算され、各更新で `(clock.TimeSec - StartTime)` を直接求める。
/// これにより `dt` を 60 回累積する際の浮動小数丸め誤差が発生しない。
/// </summary>
public interface IClock
{
    /// <summary>現在の絶対時刻 (秒)。任意の基準点からの単調増加値。</summary>
    float TimeSec { get; }
}

/// <summary>
/// 固定フレームレートの整数フレーム時計。<see cref="Frame"/> を進めると `Frame / FrameRate` で時刻が決まる。
/// 累積でなく毎回計算するため浮動小数誤差ゼロ。サンプル/オフライン描画/決定的テスト向け。
/// </summary>
public sealed class FixedFrameClock : IClock
{
    public int Frame { get; set; }
    public float FrameRate { get; init; } = 60f;
    public float TimeSec => Frame / FrameRate;

    /// <summary>1 フレーム進める。</summary>
    public void Advance() => Frame++;

    /// <summary>n フレーム進める。</summary>
    public void Advance(int n) => Frame += n;
}

/// <summary>System の Stopwatch を使う実時間クロック。ゲームループ向け。</summary>
public sealed class WallClock : IClock
{
    private readonly Stopwatch _sw;

    public WallClock()
    {
        _sw = Stopwatch.StartNew();
    }

    public float TimeSec => (float)_sw.Elapsed.TotalSeconds;

    /// <summary>時計をリセット (TimeSec を 0 に戻す)。</summary>
    public void Reset() => _sw.Restart();
}

/// <summary>
/// テスト/手動制御用の時計。<see cref="SetTime"/> や <see cref="Advance"/> で値を直接決める。
/// 累積は加算で行うため、float 加算誤差が嫌な場合は <see cref="FixedFrameClock"/> を使うこと。
/// </summary>
public sealed class ManualClock : IClock
{
    public float TimeSec { get; private set; }

    public void SetTime(float t) => TimeSec = t;
    public void Advance(float dt) => TimeSec += dt;
}
