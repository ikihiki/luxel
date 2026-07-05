namespace Luxel.Animation;

/// <summary>
/// <see cref="Base"/> の値に <see cref="Additive"/> の値を加算する (Quaternion は base * additive で合成)。
/// 「歩行 (base) に手を振る変化 (additive)」のような分解アニメに使う。
/// </summary>
public sealed class AddNode : GraphNode
{
    public GraphNode Base { get; }
    public GraphNode Additive { get; }
    public float Weight { get; set; }

    public override float Duration => MathF.Max(Base.Duration, Additive.Duration);

    public AddNode(GraphNode baseNode, GraphNode additiveNode, float weight = 1.0f)
    {
        Base = baseNode ?? throw new ArgumentNullException(nameof(baseNode));
        Additive = additiveNode ?? throw new ArgumentNullException(nameof(additiveNode));
        Weight = weight;
    }

    public override void Evaluate(float time, GraphEvaluator output)
    {
        var subBase = new GraphEvaluator();
        var subAdd = new GraphEvaluator();
        Base.Evaluate(time, subBase);
        Additive.Evaluate(time, subAdd);

        // 全 path を base から取り、additive 側にも同じ path があれば weight 付きで加算
        foreach (var path in subBase.Paths)
        {
            subBase.TryGet(path, out var baseVal, out var src);
            if (subAdd.TryGet(path, out var addVal, out _))
            {
                object combined = GraphEvaluator.Add(baseVal, addVal, Weight, src);
                output.Set(path, combined, src);
            }
            else
            {
                output.Set(path, baseVal, src);
            }
        }
        // base に無いが additive にだけある path は無視 (Bevy の AddNode と同じ挙動)
    }
}
