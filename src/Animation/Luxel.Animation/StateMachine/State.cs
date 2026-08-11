namespace Luxel.Animation;

/// <summary>
/// StateMachine の状態。1 つの <see cref="GraphNode"/> (普通は ClipNode、複雑なら BlendNode/AddNode の DAG) を持つ。
/// 遷移は <see cref="Transitions"/> に追加する。
/// </summary>
public sealed class State
{
    public string Name { get; }
    public GraphNode Graph { get; }
    public List<StateTransition> Transitions { get; } = new();

    public State(string name, GraphNode graph)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
    }

    /// <summary>Trigger 名で別 state へ遷移するルールを追加。</summary>
    public State AddTransition(string triggerName, State to, float crossfadeSec = 0.2f)
    {
        Transitions.Add(new StateTransition(triggerName, to, crossfadeSec));
        return this;
    }
}

/// <summary>State 間の遷移。Trigger 名でマッチ、CrossfadeSec で BlendNode 経由の補間。</summary>
public sealed class StateTransition
{
    public string Trigger { get; }
    public State To { get; }
    public float CrossfadeSec { get; }

    public StateTransition(string trigger, State to, float crossfadeSec)
    {
        Trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
        To = to ?? throw new ArgumentNullException(nameof(to));
        CrossfadeSec = MathF.Max(0f, crossfadeSec);
    }
}
