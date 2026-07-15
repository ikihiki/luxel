namespace Luxel.Framework;

/// <summary>Scene遷移中に通知を受ける側の役割。</summary>
public enum SceneTransitionRole
{
    Outgoing,
    Incoming,
}

/// <summary>
/// Scene遷移の1 frame分の状態。<see cref="LinearProgress"/>は時間の正規化値、
/// <see cref="Progress"/>はeasing適用後の値。通常は0が開始、1が完了を表す。
/// </summary>
public readonly record struct SceneTransitionContext(
    SceneNode Outgoing,
    SceneNode Incoming,
    float LinearProgress,
    float Progress);

/// <summary>
/// Scene自身がopacity、transform、cameraなどへ遷移値を反映するための任意契約。
/// 実装しなくてもoutgoing/incomingの同時実行とlifecycle切替は行われる。
/// </summary>
public interface ISceneTransitionParticipant
{
    void OnSceneTransition(SceneTransitionContext context, SceneTransitionRole role);
}

/// <summary>
/// root Scene間の時間付き遷移設定。描画方式をFrameworkへ固定せず、participantまたは
/// <see cref="Apply"/> callbackが各Sceneのopacity、slide位置、render target合成等を更新する。
/// </summary>
public sealed class SceneTransitionSpec
{
    public SceneTransitionSpec(
        float durationSeconds,
        Action<SceneTransitionContext>? apply = null,
        Func<float, float>? easing = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(durationSeconds);
        DurationSeconds = durationSeconds;
        Apply = apply;
        Easing = easing;
    }

    public float DurationSeconds { get; }
    public Action<SceneTransitionContext>? Apply { get; }
    public Func<float, float>? Easing { get; }

    internal float Evaluate(float linearProgress)
    {
        float linear = Math.Clamp(linearProgress, 0f, 1f);
        float value = Easing?.Invoke(linear) ?? linear;
        return float.IsFinite(value) ? value : linear;
    }
}
