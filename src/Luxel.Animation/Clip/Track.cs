using System.Numerics;

namespace Luxel.Animation;

/// <summary>
/// 型消去された Track 基底クラス。AnimationClip は <see cref="TrackBase"/> の配列で複数 Track を持つ。
/// </summary>
public abstract class TrackBase
{
    /// <summary>ターゲットを示す path 文字列。例: "cube/translation"、"cube/rotation"。</summary>
    public string TargetPath { get; }

    /// <summary>Track 単体の長さ (最後の keyframe の時刻)。</summary>
    public abstract float Duration { get; }

    /// <summary>キーフレーム数。</summary>
    public abstract int KeyframeCount { get; }

    /// <summary>time (秒) における値を <see cref="IAnimationTarget"/> へ書き込む。</summary>
    public abstract void Apply(float timeSec, IAnimationTarget target);

    protected TrackBase(string targetPath) { TargetPath = targetPath; }
}

/// <summary>
/// 型付き Track。<see cref="Keyframe{T}"/> を時刻昇順で持ち、<see cref="Sample"/> で補間値を返す。
/// Lerp 関数 (型ごとの線形補間) を constructor で受け取り、Quaternion は slerp を渡して使う。
/// </summary>
public sealed class Track<T> : TrackBase
{
    public Keyframe<T>[] Keyframes { get; }
    public InterpolationKind Interpolation { get; }
    private readonly Func<T, T, float, T> _lerp;

    public override float Duration => Keyframes.Length > 0 ? Keyframes[^1].Time : 0f;
    public override int KeyframeCount => Keyframes.Length;

    public Track(string targetPath, InterpolationKind interpolation, Keyframe<T>[] keyframes,
                 Func<T, T, float, T> lerp)
        : base(targetPath)
    {
        ArgumentNullException.ThrowIfNull(keyframes);
        ArgumentNullException.ThrowIfNull(lerp);
        if (keyframes.Length == 0) throw new ArgumentException("keyframes must not be empty", nameof(keyframes));
        Interpolation = interpolation;
        Keyframes = keyframes;
        _lerp = lerp;
    }

    /// <summary>time (秒) における補間値。範囲外はクランプ。</summary>
    public T Sample(float timeSec)
    {
        if (Keyframes.Length == 1) return Keyframes[0].Value;
        if (timeSec <= Keyframes[0].Time) return Keyframes[0].Value;
        if (timeSec >= Keyframes[^1].Time) return Keyframes[^1].Value;

        // 線形探索 (AN-M3 では keyframe 数が少ない前提。多数なら二分探索に置換)
        int i = 0;
        while (i + 1 < Keyframes.Length && Keyframes[i + 1].Time <= timeSec) i++;
        var a = Keyframes[i];
        var b = Keyframes[i + 1];

        if (Interpolation == InterpolationKind.Step) return a.Value;

        // Linear / CubicSpline 共通 (CUBICSPLINE は AN-M3 では Linear にフォールバック)
        float p = (timeSec - a.Time) / (b.Time - a.Time);
        return _lerp(a.Value, b.Value, p);
    }

    public override void Apply(float timeSec, IAnimationTarget target)
    {
        target.Apply(TargetPath, Sample(timeSec)!);
    }
}

/// <summary>各値型用の Track ファクトリ。Lerp 関数を埋め込んで Track&lt;T&gt; を作る。</summary>
public static class Tracks
{
    public static Track<float> Float(string path, InterpolationKind interp, Keyframe<float>[] kf)
        => new(path, interp, kf, (a, b, p) => a + (b - a) * p);

    public static Track<Vector2> Vector2(string path, InterpolationKind interp, Keyframe<Vector2>[] kf)
        => new(path, interp, kf, System.Numerics.Vector2.Lerp);

    public static Track<Vector3> Vector3(string path, InterpolationKind interp, Keyframe<Vector3>[] kf)
        => new(path, interp, kf, System.Numerics.Vector3.Lerp);

    public static Track<Vector4> Vector4(string path, InterpolationKind interp, Keyframe<Vector4>[] kf)
        => new(path, interp, kf, System.Numerics.Vector4.Lerp);

    /// <summary>Quaternion track は slerp で補間 (glTF spec も rotation channel は slerp)。</summary>
    public static Track<Quaternion> Quaternion(string path, InterpolationKind interp, Keyframe<Quaternion>[] kf)
        => new(path, interp, kf, System.Numerics.Quaternion.Slerp);

    public static Track<uint> Color(string path, InterpolationKind interp, Keyframe<uint>[] kf)
        => new(path, interp, kf, (a, b, p) => new RgbaTween(a, b).Lerp(p));
}
