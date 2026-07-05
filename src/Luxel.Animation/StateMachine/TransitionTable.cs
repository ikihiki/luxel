namespace Luxel.Animation;

/// <summary>
/// 1 つの遷移の仕様 (duration / curve / delay)。Duration=0 は「瞬時」。
/// <c>(duration, curve, delay)</c> tuple または <c>float</c> からの暗黙変換あり (記述短縮)。
/// (旧 Luxel.Animation.UI から移設 — AS-M2)
/// </summary>
public readonly record struct TransitionSpec(float Duration, ICurve? Curve = null, float Delay = 0f)
{
    public static implicit operator TransitionSpec(float duration) => new(duration);
    public static implicit operator TransitionSpec((float duration, ICurve? curve) t) => new(t.duration, t.curve);
    public static implicit operator TransitionSpec((float duration, ICurve? curve, float delay) t) => new(t.duration, t.curve, t.delay);
}

/// <summary>
/// 状態遷移のトランジション設定表 (AS-M2)。ルールは (from, to, prop) の 3 軸で、null = ワイルドカード。
/// <see cref="Resolve"/> は具体的なものが勝つ 8 段優先度で引く:
/// pair+prop &gt; pair &gt; to+prop &gt; to &gt; from+prop &gt; from &gt; prop &gt; 既定。
/// to (enter) が from (leave) より優先 — CSS の「遷移先のルールが適用される」慣習に一致。
/// </summary>
public sealed class TransitionTable
{
    private const string Any = "*";
    private readonly Dictionary<(string From, string To, string Prop), TransitionSpec> _rules = new();

    /// <summary>ルールを追加する (同じ軸の再 Add は上書き)。null = ワイルドカード。</summary>
    public TransitionTable Add(string? from, string? to, string? prop, TransitionSpec spec)
    {
        _rules[(from ?? Any, to ?? Any, prop ?? Any)] = spec;
        return this;
    }

    // ---- 糖衣 ----
    /// <summary>全遷移の既定。</summary>
    public TransitionTable Default(TransitionSpec spec) => Add(null, null, null, spec);
    /// <summary>プロパティ既定。</summary>
    public TransitionTable On(string prop, TransitionSpec spec) => Add(null, null, prop, spec);
    /// <summary>状態へ入るとき (enter)。</summary>
    public TransitionTable To(string to, TransitionSpec spec) => Add(null, to, null, spec);
    public TransitionTable To(string to, string prop, TransitionSpec spec) => Add(null, to, prop, spec);
    /// <summary>状態から出るとき (leave)。</summary>
    public TransitionTable From(string from, TransitionSpec spec) => Add(from, null, null, spec);
    public TransitionTable From(string from, string prop, TransitionSpec spec) => Add(from, null, prop, spec);
    /// <summary>特定の from→to ペア。</summary>
    public TransitionTable Between(string from, string to, TransitionSpec spec) => Add(from, to, null, spec);
    public TransitionTable Between(string from, string to, string prop, TransitionSpec spec) => Add(from, to, prop, spec);

    /// <summary>(from → to, prop) に適用する spec を 8 段優先度で解決する。該当なし = null (瞬時)。</summary>
    public TransitionSpec? Resolve(string from, string to, string prop)
    {
        if (_rules.TryGetValue((from, to, prop), out TransitionSpec s)) return s;
        if (_rules.TryGetValue((from, to, Any), out s)) return s;
        if (_rules.TryGetValue((Any, to, prop), out s)) return s;
        if (_rules.TryGetValue((Any, to, Any), out s)) return s;
        if (_rules.TryGetValue((from, Any, prop), out s)) return s;
        if (_rules.TryGetValue((from, Any, Any), out s)) return s;
        if (_rules.TryGetValue((Any, Any, prop), out s)) return s;
        if (_rules.TryGetValue((Any, Any, Any), out s)) return s;
        return null;
    }
}
