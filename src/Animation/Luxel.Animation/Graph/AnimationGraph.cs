namespace Luxel.Animation;

/// <summary>
/// AnimationGraph (Bevy 風 DAG) のトップレベル。Root ノードを毎フレーム評価し、結果を <see cref="IAnimationTarget"/> へ
/// 書き込む。AnimationPlayer/TrackEntry とは別系統 (ブレンドは Player のレイヤを超えるため独立 evaluator)。
///
/// 使い方:
/// <code>
/// var graph = new AnimationGraph(rootNode, target) { StartTime = clock.TimeSec, Loop = true };
/// for (each frame) graph.Tick(clock);
/// </code>
/// </summary>
public sealed class AnimationGraph
{
    public GraphNode Root { get; }
    public IAnimationTarget Target { get; }
    public float StartTime { get; set; }
    public float TimeScale { get; set; } = 1f;
    public bool Loop { get; set; }
    public bool Done { get; private set; }

    public AnimationGraph(GraphNode root, IAnimationTarget target)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>絶対時刻で評価する。Loop=true なら Duration で modulo、false なら Done フラグを立てて停止。</summary>
    public void Tick(float absoluteTimeSec)
    {
        if (Done) return;
        float t = (absoluteTimeSec - StartTime) * TimeScale;
        if (t < 0f) t = 0f;

        float dur = Root.Duration;
        if (dur > 0f)
        {
            if (t >= dur)
            {
                if (Loop) t %= dur;
                else { t = dur; Done = true; }
            }
        }

        var eval = new GraphEvaluator();
        Root.Evaluate(t, eval);
        eval.FlushTo(Target);
    }

    public void Tick(IClock clock) => Tick(clock.TimeSec);

    /// <summary>再生位置をリセット (Done もクリア)。</summary>
    public void Reset(float startTimeAbs = 0f)
    {
        StartTime = startTimeAbs;
        Done = false;
    }
}
