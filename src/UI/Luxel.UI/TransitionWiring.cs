using Luxel.Animation;
using Luxel.UI.Styling;

namespace Luxel.UI;

/// <summary>
/// 状態スタイル (Bindable の状態レイヤ / Styles.Resolve) の**値変化**を、widget の支配状態の
/// from→to で <see cref="TransitionTable"/> を解決して自動補間する配線 (AS-M3)。
/// - fluent Transition 系 (Transition/TransitionTo/From/Between) が widget に TransitionTable を添付する
/// - <see cref="Widget.Realize"/> が添付を見て <see cref="Widget.SetSetterWrapFallback"/> を登録する
/// - 各プロパティは独立した <see cref="PropertyStateMachine"/> (from = そのプロパティが最後に
///   向かっていた支配状態) — 途中の状態変化も現在値起点で滑らかに繋がる
/// </summary>
public static class TransitionWiring
{
    /// <summary>添付キー: widget に積まれた <see cref="TransitionTable"/>。</summary>
    public const string TableKey = "Transition.Table";

    /// <summary>widget 添付の TransitionTable へルールを積む (無ければ作って添付)。
    /// fluent (<see cref="TransitionExtensions"/>) が使う共通経路。</summary>
    public static void AddRule(Widget w, string? from, string? to, string? prop, TransitionSpec spec)
    {
        var table = w.GetAttached<TransitionTable>(TableKey);
        if (table is null)
        {
            table = new TransitionTable();
            w.SetAttached(new Attached(TableKey, table));
        }
        table.Add(from, to, prop, spec);
    }

    /// <summary>支配状態の判定順 (Bindable の StatePriority と同じ)。先頭のアクティブ状態が from/to 名になる。</summary>
    private static readonly WidgetState[] Priority =
        [WidgetState.Disabled, WidgetState.Pressed, WidgetState.Hover, WidgetState.Focused, WidgetState.Checked, WidgetState.Selected];

    /// <summary>widget の現在の支配状態名 ("Default" = どれもアクティブでない)。</summary>
    public static string DominantState(Widget w)
    {
        foreach (WidgetState s in Priority)
            if (w.IsStateActive(s)) return s.ToString();
        return "Default";
    }

    internal sealed class Provider(Widget w, UiBuildContext ctx, TransitionTable table) : ISetterWrapProvider
    {
        public Action<T> Wrap<T>(string prop, Action<T> raw)
        {
            var m = new PropertyStateMachine(table);
            IClock clock = ctx.Host?.Clock ?? new ManualClock();
            m.Bind(prop, raw);
            ctx.AddAnimation(dt => { m.Tick(clock); return false; });
            // 初回は Start 縮退 (瞬時 — Realize 直後は静止値)。以後は支配状態を from/to に遷移。
            return v => m.Goto(DominantState(w), clock, prop, v!);
        }
    }
}

/// <summary>プロパティ名毎の setter ラッパが無いときに適用される一括フォールバック
/// (<see cref="Widget.SetSetterWrapFallback"/>)。</summary>
public interface ISetterWrapProvider
{
    Action<T> Wrap<T>(string prop, Action<T> raw);
}
