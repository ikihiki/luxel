using Luxel.Animation;
using Luxel.UI.Styling;

namespace Luxel.UI;

/// <summary>
/// トランジションの fluent 宣言 — **どのプロパティ群をアニメーションさせるかを指定する**唯一の場所
/// (状態の値は生成された <c>When(state, ...)</c> で宣言し、トランジションはここで独立して宣言する)。
/// 引数は DSL の他所と同じ流儀 (duration 秒 + 任意の curve/delay — <c>new TransitionSpec</c> は書かない):
/// <code>
/// Button(onClick, "Save", background: blue)
///     .When(Hover, background: red, scaleX: 1.1f)
///     .Transition(0.4f, EaseInOut, ButtonProps.Background)   // Background だけ 400ms
///     .Transition(0.12f, Transform.ScaleX)                   // ScaleX は 120ms (既定カーブ)
///     .TransitionTo(Hover, 0.08f, ButtonProps.Background)    // enter は速く
///     .TransitionBetween(Pressed, Hover, 0f);                // 離した瞬間は即時 (全 prop)
/// </code>
/// プロパティ群 (<c>params props</c>) を省略すると全プロパティが対象 (ワイルドカード)。
/// 解決は <see cref="TransitionTable"/> の 8 段優先度。ジェネリック this で具象型を返しチェーンできる。
/// </summary>
public static class TransitionExtensions
{
    // ---- プロパティ既定 (どの状態遷移でも) ----
    public static T Transition<T>(this T w, float duration, params string[] props) where T : Widget
        => Add(w, null, null, new TransitionSpec(duration), props);
    public static T Transition<T>(this T w, float duration, ICurve curve, params string[] props) where T : Widget
        => Add(w, null, null, new TransitionSpec(duration, curve), props);
    public static T Transition<T>(this T w, float duration, ICurve curve, float delay, params string[] props) where T : Widget
        => Add(w, null, null, new TransitionSpec(duration, curve, delay), props);

    // ---- 状態へ入るとき (enter) ----
    public static T TransitionTo<T>(this T w, WidgetState to, float duration, params string[] props) where T : Widget
        => Add(w, null, to.ToString(), new TransitionSpec(duration), props);
    public static T TransitionTo<T>(this T w, WidgetState to, float duration, ICurve curve, params string[] props) where T : Widget
        => Add(w, null, to.ToString(), new TransitionSpec(duration, curve), props);
    public static T TransitionTo<T>(this T w, WidgetState to, float duration, ICurve curve, float delay, params string[] props) where T : Widget
        => Add(w, null, to.ToString(), new TransitionSpec(duration, curve, delay), props);

    // ---- 状態から出るとき (leave) ----
    public static T TransitionFrom<T>(this T w, WidgetState from, float duration, params string[] props) where T : Widget
        => Add(w, from.ToString(), null, new TransitionSpec(duration), props);
    public static T TransitionFrom<T>(this T w, WidgetState from, float duration, ICurve curve, params string[] props) where T : Widget
        => Add(w, from.ToString(), null, new TransitionSpec(duration, curve), props);
    public static T TransitionFrom<T>(this T w, WidgetState from, float duration, ICurve curve, float delay, params string[] props) where T : Widget
        => Add(w, from.ToString(), null, new TransitionSpec(duration, curve, delay), props);

    // ---- 特定の from→to ペア (2 状態に跨るため When には同居できない唯一のルール) ----
    public static T TransitionBetween<T>(this T w, WidgetState from, WidgetState to, float duration, params string[] props) where T : Widget
        => Add(w, from.ToString(), to.ToString(), new TransitionSpec(duration), props);
    public static T TransitionBetween<T>(this T w, WidgetState from, WidgetState to, float duration, ICurve curve, params string[] props) where T : Widget
        => Add(w, from.ToString(), to.ToString(), new TransitionSpec(duration, curve), props);
    public static T TransitionBetween<T>(this T w, WidgetState from, WidgetState to, float duration, ICurve curve, float delay, params string[] props) where T : Widget
        => Add(w, from.ToString(), to.ToString(), new TransitionSpec(duration, curve, delay), props);

    private static T Add<T>(T w, string? from, string? to, TransitionSpec spec, string[] props) where T : Widget
    {
        if (props.Length == 0)
        {
            TransitionWiring.AddRule(w, from, to, null, spec);
        }
        else
        {
            foreach (string p in props) TransitionWiring.AddRule(w, from, to, p, spec);
        }
        return w;
    }
}
