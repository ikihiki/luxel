using Luxel.UI;

namespace Luxel.Scene.UI;

/// <summary>
/// AnimationController から制御される再生状態 (Signal ベース、reactive)。
/// 1 つの <see cref="Luxel.Assets.AssetAnimation"/> 単位、または複数 clip を切替えても再利用可能。
/// </summary>
public sealed class PlaybackState
{
    public Signal<bool> IsPlaying { get; } = new(false);
    public Signal<float> CurrentTime { get; } = new(0f);
    public Signal<float> Speed { get; } = new(1f);
    public Signal<bool> Looped { get; } = new(true);
    public Signal<int> ActiveClipIndex { get; } = new(0);
    /// <summary>登録された clip の duration (ActiveClipIndex で参照)。</summary>
    public Signal<float> Duration { get; } = new(0f);

    /// <summary>1 frame 進める (dt 秒)。Loop 設定で wrap、終端で停止 (loop off の場合)。</summary>
    public void Tick(float dt)
    {
        if (!IsPlaying.Value) return;
        var t = CurrentTime.Value + dt * Speed.Value;
        var d = Duration.Value;
        if (d <= 0) { CurrentTime.Value = 0; return; }
        if (t >= d)
        {
            if (Looped.Value) t = t % d;
            else { t = d; IsPlaying.Value = false; }
        }
        else if (t < 0)
        {
            if (Looped.Value) t = d + (t % d);
            else { t = 0; IsPlaying.Value = false; }
        }
        CurrentTime.Value = t;
    }
}
