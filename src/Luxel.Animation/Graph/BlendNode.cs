namespace Luxel.Animation;

/// <summary>
/// 2 つの子の値を <see cref="Weight"/> で線形補間する。weight=0 は A の値、weight=1 は B の値。
/// 両子が同じ path に書き込む場合のみ blend、片方だけが書き込む path はそのまま採用。
/// </summary>
public sealed class BlendNode : GraphNode
{
    public GraphNode A { get; }
    public GraphNode B { get; }
    public float Weight { get; set; }

    public override float Duration => MathF.Max(A.Duration, B.Duration);

    public BlendNode(GraphNode a, GraphNode b, float weight = 0.5f)
    {
        A = a ?? throw new ArgumentNullException(nameof(a));
        B = b ?? throw new ArgumentNullException(nameof(b));
        Weight = weight;
    }

    public override void Evaluate(float time, GraphEvaluator output)
    {
        var subA = new GraphEvaluator();
        var subB = new GraphEvaluator();
        A.Evaluate(time, subA);
        B.Evaluate(time, subB);

        var allPaths = new HashSet<string>();
        foreach (var p in subA.Paths) allPaths.Add(p);
        foreach (var p in subB.Paths) allPaths.Add(p);

        foreach (var path in allPaths)
        {
            bool hasA = subA.TryGet(path, out var va, out var sa);
            bool hasB = subB.TryGet(path, out var vb, out var sb);
            if (hasA && hasB)
            {
                object blended = GraphEvaluator.Lerp(va, vb, Math.Clamp(Weight, 0f, 1f), sa);
                output.Set(path, blended, sa);
            }
            else if (hasA) output.Set(path, va, sa);
            else if (hasB) output.Set(path, vb, sb);
        }
    }
}
