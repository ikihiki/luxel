using System.Numerics;

namespace Luxel.Animation;

/// <summary>
/// CSS `transition` 相当の「値変化を検知して自動補間」を実現する **setter ラッパー**。
/// scene-agnostic ─ Signal / UiNode / ECS / 任意の <see cref="Action{T}"/> に等しく適用可能。
///
/// 使い方:
/// <code>
/// var animatedColor = Transition.Animate&lt;uint&gt;(
///     v =&gt; node.Color = v,            // 真の書込み先
///     player, clock,
///     duration: 0.3f,
///     curve: CubicBezierCurve.EaseInOut);
/// animatedColor(Red);   // 初回は即時 Apply
/// animatedColor(Blue);  // 0.3s で Red→Blue を補間
/// animatedColor(Green); // 進行中なら現在値からフル duration で Blue 経由せず Green へ
/// </code>
///
/// 設計決定 (UI_TRANSITION_PLAN.md):
///   #2 Smooth interrupt = フル duration、現在値起点
///   #3 delay = WithDelay 同等の引数で対応
///   #4 複数プロパティ = 別 setter で分割 (ラッパーは 1 prop 1 個)
///   #6 デフォルト curve = CubicBezierCurve.EaseInOut
///   #7 キャンセル = AnimationPlayer.Stop で凍結 (現在値保持)
/// </summary>
public static class Transition
{
    /// <summary>
    /// setter をラップし、値変化時に古い値から新しい値へフル <paramref name="duration"/> で補間する関数を返す。
    /// 初回呼出しは即時 Apply (補間なし)。同じ値の連続呼出しはスキップ。進行中なら smooth interrupt で
    /// 現在値からフル duration で新値へ。
    /// </summary>
    /// <param name="setter">真の書込み先。例: <c>v =&gt; node.Color = v</c>。</param>
    /// <param name="player">補間を駆動する <see cref="AnimationPlayer"/>。Tick されている前提。</param>
    /// <param name="clock">時刻供給。Play 時の StartTime を決める。</param>
    /// <param name="duration">補間時間 (秒)。</param>
    /// <param name="curve">easing curve。null なら <see cref="CubicBezierCurve.EaseInOut"/>。</param>
    /// <param name="delay">補間開始前の遅延 (秒)。</param>
    public static Action<T> Animate<T>(
        Action<T> setter,
        AnimationPlayer player,
        IClock clock,
        float duration,
        ICurve? curve = null,
        float delay = 0f)
    {
        ArgumentNullException.ThrowIfNull(setter);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(clock);
        if (duration < 0f) throw new ArgumentException("duration >= 0", nameof(duration));
        if (delay < 0f) throw new ArgumentException("delay >= 0", nameof(delay));
        curve ??= CubicBezierCurve.EaseInOut;

        bool hasInitial = false;
        T current = default!;            // 現在の (補間後) 値
        T target = default!;             // 直近の Set で要求された目標値
        TrackEntryBase? active = null;

        return newValue =>
        {
            if (!hasInitial)
            {
                // 初回: 即時 Apply (補間なし)
                setter(newValue);
                current = newValue;
                target = newValue;
                hasInitial = true;
                return;
            }

            // 同じ目標値への連続呼出しはスキップ (idempotent)
            if (EqualityComparer<T>.Default.Equals(target, newValue)) return;
            target = newValue;

            // 進行中 transition を凍結 (現在値保持)
            if (active != null && !active.Done)
            {
                player.Stop(active);
            }

            if (duration <= 0f)
            {
                // duration=0: 即時 Apply (補間スキップ)
                setter(newValue);
                current = newValue;
                active = null;
                return;
            }

            // 現在値 → 新値 をフル duration で新規補間
            T from = current;
            ITween<T> tween = CreateTween(from, newValue);
            var anim = new Animatable<T> { Curve = curve, Tween = tween, Duration = duration };
            var entry = player.Play(anim, v =>
            {
                current = v;
                setter(v);
            }, clock);
            if (delay > 0f) entry.StartTime += delay;
            active = entry;
        };
    }

    /// <summary>型ごとに ITween&lt;T&gt; を自動選択。AN-M3 の TrackValue と同パターン
    /// (<see cref="PropertyStateMachine"/> も使う)。</summary>
    internal static ITween<T> CreateTween<T>(T from, T to)
    {
        // 主要型を網羅。未対応型は StepTween (Step 動作) でフォールバック。
        if (typeof(T) == typeof(float)) return (ITween<T>)(object)new FloatTween((float)(object)from!, (float)(object)to!);
        if (typeof(T) == typeof(Vector2)) return (ITween<T>)(object)new Vector2Tween((Vector2)(object)from!, (Vector2)(object)to!);
        if (typeof(T) == typeof(Vector3)) return (ITween<T>)(object)new Vector3Tween((Vector3)(object)from!, (Vector3)(object)to!);
        if (typeof(T) == typeof(Vector4)) return (ITween<T>)(object)new Vector4Tween((Vector4)(object)from!, (Vector4)(object)to!);
        if (typeof(T) == typeof(Quaternion)) return (ITween<T>)(object)new QuaternionTween((Quaternion)(object)from!, (Quaternion)(object)to!);
        if (typeof(T) == typeof(uint)) return (ITween<T>)(object)new RgbaTween((uint)(object)from!, (uint)(object)to!);
        return new StepTween<T>(from, to);   // 不明型は Step (0.5 未満で from、以上で to)
    }

    /// <summary>不明型用の step tween。t&lt;0.5 で from、それ以上で to。</summary>
    private readonly struct StepTween<T> : ITween<T>
    {
        private readonly T _from;
        private readonly T _to;
        public StepTween(T from, T to) { _from = from; _to = to; }
        public T Lerp(float t) => t < 0.5f ? _from : _to;
    }
}
