using Luxel.Animation;
using Luxel.UI;
using Luxel.UI.Styling;

namespace Luxel.Animation.UI;

// TransitionSpec は Luxel.Animation へ移設 (AS-M2 — TransitionTable/PropertyStateMachine が使うため)。

/// <summary>P.Transition.* で使われる Attached キー定数。</summary>
public static class TransitionKeys
{
    public const string Color = "Transition.Color";
    public const string Opacity = "Transition.Opacity";
    public const string Translation = "Transition.Translation";
    public const string TranslationX = "Transition.TranslationX";
    public const string TranslationY = "Transition.TranslationY";
    public const string Scale = "Transition.Scale";
    public const string ScaleX = "Transition.ScaleX";
    public const string ScaleY = "Transition.ScaleY";
    public const string Rotation = "Transition.Rotation";
}

/// <summary>
/// Transition 用の Attached パート。Luxel.UI の <see cref="AttachedPart"/> と同じ役割だが、
/// <see cref="Spec"/> を public で公開するため、parts 配列を直接走査して spec を読み取れる。
/// (AttachedPart の value プロパティが外部公開されていないため、Luxel.UI 本体を変えずに対処する設計。)
/// Apply 時には Widget.SetAttached(new Attached(Key, Spec)) として既存ストレージにも入れる。
/// </summary>
public sealed class TransitionAttachment(string key, TransitionSpec spec) : IConfigPart
{
    public string Key { get; } = key;
    public TransitionSpec Spec { get; } = spec;
    public void Apply(Widget target) => target.SetAttached(new Attached(Key, Spec));
}

/// <summary>
/// <c>P.Transition.Color(0.3f, ease)</c> 等の DSL ファクトリ。各メソッドは <see cref="IConfigPart"/> を返す
/// (TransitionAttachment 経由で子 Widget / parts 配列に保存される)。
/// Grid.Column と同じパターン。
/// </summary>
public readonly struct TransitionDecl
{
    public IConfigPart Color(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionAttachment(TransitionKeys.Color, new TransitionSpec(duration, curve, delay));

    public IConfigPart Opacity(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionAttachment(TransitionKeys.Opacity, new TransitionSpec(duration, curve, delay));

    public IConfigPart Translation(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionAttachment(TransitionKeys.Translation, new TransitionSpec(duration, curve, delay));

    public IConfigPart TranslationX(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionAttachment(TransitionKeys.TranslationX, new TransitionSpec(duration, curve, delay));

    public IConfigPart TranslationY(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionAttachment(TransitionKeys.TranslationY, new TransitionSpec(duration, curve, delay));

    public IConfigPart Scale(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionAttachment(TransitionKeys.Scale, new TransitionSpec(duration, curve, delay));

    public IConfigPart ScaleX(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionAttachment(TransitionKeys.ScaleX, new TransitionSpec(duration, curve, delay));

    public IConfigPart ScaleY(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionAttachment(TransitionKeys.ScaleY, new TransitionSpec(duration, curve, delay));

    public IConfigPart Rotation(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionAttachment(TransitionKeys.Rotation, new TransitionSpec(duration, curve, delay));

    // ---- 状態遷移設定 (AS-M3): 状態レイヤ (On(WidgetState.Hover, Bg(...)) 等) の値変化に対して
    //      from / to / from→to 毎 × プロパティ選択で遷移を宣言する。Widget.Realize が添付された
    //      TransitionTable を見て setter を自動配線 (TransitionWiring) する。----

    /// <summary>全状態遷移・全プロパティの既定。</summary>
    public IConfigPart Default(TransitionSpec spec) => new StateTransitionRulePart(null, null, null, spec);
    /// <summary>プロパティ既定 (どの状態遷移でも)。</summary>
    public IConfigPart On(string prop, TransitionSpec spec) => new StateTransitionRulePart(null, null, prop, spec);
    /// <summary>状態へ入るとき (enter)。</summary>
    public IConfigPart To(WidgetState to, TransitionSpec spec) => new StateTransitionRulePart(null, to.ToString(), null, spec);
    public IConfigPart To(WidgetState to, string prop, TransitionSpec spec) => new StateTransitionRulePart(null, to.ToString(), prop, spec);
    /// <summary>状態から出るとき (leave)。</summary>
    public IConfigPart From(WidgetState from, TransitionSpec spec) => new StateTransitionRulePart(from.ToString(), null, null, spec);
    public IConfigPart From(WidgetState from, string prop, TransitionSpec spec) => new StateTransitionRulePart(from.ToString(), null, prop, spec);
    /// <summary>特定の from→to ペア。</summary>
    public IConfigPart Between(WidgetState from, WidgetState to, TransitionSpec spec)
        => new StateTransitionRulePart(from.ToString(), to.ToString(), null, spec);
    public IConfigPart Between(WidgetState from, WidgetState to, string prop, TransitionSpec spec)
        => new StateTransitionRulePart(from.ToString(), to.ToString(), prop, spec);
}

/// <summary>状態遷移ルール 1 本を widget 添付の <see cref="TransitionTable"/> へ積む部品
/// (fluent <see cref="TransitionExtensions"/> と同じ <see cref="TransitionWiring.AddRule"/> 経路)。</summary>
public sealed class StateTransitionRulePart(string? from, string? to, string? prop, TransitionSpec spec) : IConfigPart
{
    public void Apply(Widget target) => TransitionWiring.AddRule(target, from, to, prop, spec);
}

/// <summary>
/// <c>using static Luxel.UI.Decl;</c> したコードで <c>P.Transition</c> を使えるようにする拡張プロパティ。
/// C# 14 extension member。Luxel.UI 本体を変更せずに <see cref="PRoot"/> へ Transition ファサードを追加する。
/// </summary>
public static class PTransitionExtensions
{
    extension(PRoot p)
    {
        public TransitionDecl Transition => default;
    }
}
