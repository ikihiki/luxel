using Luxel.Animation;

namespace Luxel.UI;

/// <summary>
/// <see cref="PropertyStateMachine"/> の UI ブリッジ (AS-M3) — 全アニメーションの統一管理点。
/// 責務は「prop → signal 束縛」と「ホスト時計 (<see cref="UiHost.Clock"/>) での駆動」だけで、
/// 計算 (tween/curve/from-to 解決) はすべて Luxel.Animation 側。
/// <code>
/// var st = ctx.States(new TransitionTable().Default(0.14f))
///     .AddState("off", ("t", 0f))
///     .AddState("on",  ("t", 1f));
/// st.Start(_on.Peek() ? "on" : "off");                       // 初期は瞬時 (snap 不変)
/// ctx.Effect(() => st.Goto(_on.Value ? "on" : "off"));       // 状態遷移 (途中でも現在値起点)
/// ctx.Effect(() => node.Transform = Translate(st.Float("t") * W, 0));   // tracked 読み
/// </code>
/// 動的状態 (リスト選択/スクロール等の非有界な状態空間) は値をその場で渡す:
/// <code>st.Goto("wheel", ("offset", target));   // 状態名は TransitionTable の from/to 解決キー</code>
/// </summary>
public sealed class UiStates
{
    private readonly PropertyStateMachine _m;
    private readonly IClock _clock;
    private readonly Dictionary<string, object> _signals = new();   // prop → Signal<T>

    internal UiStates(UiBuildContext ctx, TransitionTable table)
    {
        _m = new PropertyStateMachine(table);
        _clock = ctx.Host?.Clock ?? new ManualClock();
        ctx.AddAnimation(dt => { _m.Tick(_clock); return false; });
    }

    /// <summary>中の機械 (検査/高度な用途)。</summary>
    public PropertyStateMachine Machine => _m;
    public string Current => _m.Current;
    public bool IsTransitioning => _m.IsTransitioning;

    public UiStates AddState(string name, params (string Prop, object Value)[] values)
    {
        _m.AddState(name, ToDict(values));
        return this;
    }

    /// <summary>初期状態を瞬時適用する (Realize 中に呼ぶ — snap golden を揺らさない)。</summary>
    public void Start(string state) => _m.Start(state);
    public void Start(string state, params (string Prop, object Value)[] values) => _m.Start(state, ToDict(values));

    /// <summary>登録状態へ遷移する (途中でも現在値起点)。effect 内から呼んでよい
    /// (signal を書くのは機械の Tick = ホストの Tick 内のみ)。</summary>
    public void Goto(string state) => _m.Goto(state, _clock);
    /// <summary>動的状態へ遷移する (値をその場で供給 — 同名でも値が違えば retarget)。</summary>
    public void Goto(string state, params (string Prop, object Value)[] values) => _m.Goto(state, _clock, ToDict(values));

    /// <summary>float プロパティの現在値 (tracked 読み — effect の依存になる)。</summary>
    public float Float(string prop) => Sig<float>(prop).Value;
    /// <summary>色プロパティの現在値 (tracked 読み)。</summary>
    public uint Color(string prop) => Sig<uint>(prop).Value;

    private Signal<T> Sig<T>(string prop)
    {
        if (_signals.TryGetValue(prop, out object? o)) return (Signal<T>)o;
        var s = new Signal<T>(default!);
        _signals[prop] = s;
        _m.Bind<T>(prop, v => s.Value = v);   // 束ねた時点の値も即反映される
        return s;
    }

    private static Dictionary<string, object> ToDict((string Prop, object Value)[] values)
    {
        var d = new Dictionary<string, object>(values.Length);
        foreach ((string p, object v) in values) d[p] = v;
        return d;
    }
}

public static class UiStatesExtensions
{
    /// <summary>状態機械を作る (Realize 中に呼ぶ — AddAnimation で常駐駆動、スコープ破棄で停止)。</summary>
    public static UiStates States(this UiBuildContext ctx, TransitionTable table) => new(ctx, table);
}
