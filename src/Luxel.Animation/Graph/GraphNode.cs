namespace Luxel.Animation;

/// <summary>
/// AnimationGraph の DAG ノード基底。Bevy 風の 3 種 (Clip / Blend / Add) を <see cref="ClipNode"/>,
/// <see cref="BlendNode"/>, <see cref="AddNode"/> が実装する。
/// </summary>
public abstract class GraphNode
{
    /// <summary>ノード全体の再生長 (秒)。子の最大値などから決まる。</summary>
    public abstract float Duration { get; }

    /// <summary>
    /// time (秒) における値を <paramref name="output"/> に書き込む。ループは AnimationGraph 側で扱う前提のため
    /// ノード自体は範囲外をクランプしない (Track の Sample に任せる)。
    /// </summary>
    public abstract void Evaluate(float time, GraphEvaluator output);
}
