using System.Numerics;

namespace Luxel.Animation;

/// <summary>
/// アニメーション DSL のファクトリ。<c>using static Luxel.Animation.Animate;</c> で短縮形を使う想定。
///
/// 使用例:
/// <code>
/// var clock = new FixedFrameClock();
/// var player = new AnimationPlayer();
///
/// Animate.Tween(v => x = v, from: 0f, to: 100f, dur: 0.5f)
///        .WithCurve(CubicBezierCurve.EaseOut)
///        .OnComplete(() => Console.WriteLine("done"))
///        .Play(player, clock);
///
/// Animate.Sequence(
///     Animate.Tween(v => opacity = v, 0f, 1f, 0.3f),
///     Animate.Tween(v => pos = v, new Vector2(0,0), new Vector2(100,0), 0.3f).WithCurve(CubicBezierCurve.EaseOut)
/// ).Play(player, clock);
///
/// Animate.Parallel(
///     Animate.Tween(v => opacity = v, 0f, 1f, 0.3f),
///     Animate.Tween(v => scale = v, 0.5f, 1f, 0.3f).WithCurve(CubicBezierCurve.EaseOut)
/// ).Play(player, clock);
/// </code>
/// </summary>
public static class Animate
{
    // === float ===
    public static TweenCommand<float> Tween(Action<float> setter, float from, float to, float dur)
        => new(setter, new FloatTween(from, to), dur);

    // === Vector2 ===
    public static TweenCommand<Vector2> Tween(Action<Vector2> setter, Vector2 from, Vector2 to, float dur)
        => new(setter, new Vector2Tween(from, to), dur);

    // === Vector3 ===
    public static TweenCommand<Vector3> Tween(Action<Vector3> setter, Vector3 from, Vector3 to, float dur)
        => new(setter, new Vector3Tween(from, to), dur);

    // === Vector4 (Color RGBA etc.) ===
    public static TweenCommand<Vector4> Tween(Action<Vector4> setter, Vector4 from, Vector4 to, float dur)
        => new(setter, new Vector4Tween(from, to), dur);

    // === uint RGBA ===
    public static TweenCommand<uint> TweenColor(Action<uint> setter, uint from, uint to, float dur)
        => new(setter, new RgbaTween(from, to), dur);

    // === Quaternion (slerp) ===
    public static TweenCommand<Quaternion> Tween(Action<Quaternion> setter, Quaternion from, Quaternion to, float dur)
        => new(setter, new QuaternionTween(from, to), dur);

    // === 任意の ITween<T> ===
    public static TweenCommand<T> Tween<T>(Action<T> setter, ITween<T> tween, float dur)
        => new(setter, tween, dur);

    // === 合成 ===
    public static SequenceCommand Sequence(params IAnimationCommand[] children) => new(children);
    public static ParallelCommand Parallel(params IAnimationCommand[] children) => new(children);

    // === AnimationClip 再生 (AN-M3) ===
    /// <summary>AnimationClip と Target を結びつけて再生するコマンド。</summary>
    public static ClipCommand Clip(AnimationClip clip, IAnimationTarget target) => new(clip, target);
}
