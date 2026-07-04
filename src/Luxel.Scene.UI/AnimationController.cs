namespace Luxel.Scene.UI;

/// <summary>
/// Animation 制御の reactive state ヘルパ (PlaybackState を参照)。
/// <para><b>注</b>: 具体的な Widget 構築 (Button/Slider/Segmented) は Sample 側で組む。
/// 本クラスは state の生成と再生 tick の制御を提供するのみで、UI ライブラリへの依存を最小化。</para>
///
/// <code>
/// var state = new PlaybackState();
/// state.Duration.Value = animation.Duration;
///
/// // Sample 側で Widget 構築:
/// host.SetRoot(VStack[
///     Button(_ => state.IsPlaying.Value = !state.IsPlaying.Value, "Play/Pause"),
///     Kit.Slider(state.CurrentTime, 0, state.Duration.Value),
///     // ...
/// ]);
///
/// // 毎フレーム:
/// state.Tick(deltaTime);
/// </code>
/// </summary>
public static class AnimationController
{
    public static PlaybackState CreateState() => new();
}
