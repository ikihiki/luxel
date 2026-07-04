using Luxel.Animation;
using Luxel.UI;

namespace Luxel.Animation.UI;

/// <summary>
/// 事前に <see cref="AnimationPlayer"/> + <see cref="IClock"/> を保持しておき、
/// メソッド呼び出しで <see cref="IConfigPart"/> を生成する Transition ファクトリクラス。
///
/// Tailwind 風の「parts に並べて宣言する」スタイルで Widget に補間を埋め込む:
/// <code>
/// var fx = new TransitionFactory(player, clock);
/// Button(_ => {}, "Hover Me",
///     Bg(blue), On(WidgetState.Hover, Bg(red), Scale(1.15f)),
///     fx.Background(0.30f, CubicBezierCurve.EaseInOut),
///     fx.Scale(0.20f, CubicBezierCurve.EaseOut));
/// </code>
///
/// 各メソッドは <see cref="IConfigPart"/> を返し、Apply で <see cref="Widget.SetSetterWrap{T}"/> に
/// <see cref="Transition.Animate"/> のラッパを登録する — widget の Realize が
/// <see cref="Widget.WrapSetter{T}"/> 経由で setter を作るため、対応プロパティは自動補間される。
/// widget 別の分岐は存在しない (プロパティ名ベース)。
/// </summary>
public sealed class TransitionFactory
{
    private readonly AnimationPlayer _player;
    private readonly IClock _clock;

    public TransitionFactory(AnimationPlayer player, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(clock);
        _player = player;
        _clock = clock;
    }

    public IConfigPart Background(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionSetterPart<uint>(["Background"], _player, _clock, new TransitionSpec(duration, curve, delay));

    /// <summary>前景色 (Button 等は "Foreground"、Text は "Color")。</summary>
    public IConfigPart Foreground(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionSetterPart<uint>(["Foreground", "Color"], _player, _clock, new TransitionSpec(duration, curve, delay));

    public IConfigPart Scale(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionSetterPart<float>(["Scale"], _player, _clock, new TransitionSpec(duration, curve, delay));

    public IConfigPart Opacity(float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionSetterPart<float>(["Opacity"], _player, _clock, new TransitionSpec(duration, curve, delay));

    /// <summary>任意プロパティ名 (uint 色) の補間。</summary>
    public IConfigPart Color(string prop, float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionSetterPart<uint>([prop], _player, _clock, new TransitionSpec(duration, curve, delay));

    /// <summary>任意プロパティ名 (float) の補間。</summary>
    public IConfigPart Float(string prop, float duration, ICurve? curve = null, float delay = 0f)
        => new TransitionSetterPart<float>([prop], _player, _clock, new TransitionSpec(duration, curve, delay));

    /// <summary>
    /// <see cref="TransitionSet"/> から全プロパティの IConfigPart を一括生成。
    /// <c>parts: [.. fx.FromSet(set)]</c> で展開できる。
    /// </summary>
    public IEnumerable<IConfigPart> FromSet(TransitionSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set.Background is { Duration: > 0 } bg) yield return Background(bg.Duration, bg.Curve, bg.Delay);
        if (set.Foreground is { Duration: > 0 } fg) yield return Foreground(fg.Duration, fg.Curve, fg.Delay);
        if (set.Scale      is { Duration: > 0 } sc) yield return Scale(sc.Duration, sc.Curve, sc.Delay);
        if (set.Opacity    is { Duration: > 0 } op) yield return Opacity(op.Duration, op.Curve, op.Delay);
    }
}

/// <summary>
/// TransitionFactory が生成する IConfigPart 実装。Apply で対象プロパティ名の setter ラッパを登録する。
/// </summary>
internal sealed class TransitionSetterPart<T>(
    string[] props, AnimationPlayer player, IClock clock, TransitionSpec spec) : IConfigPart
{
    public void Apply(Widget widget)
    {
        foreach (string p in props)
            widget.SetSetterWrap<T>(p, raw => Transition.Animate(raw, player, clock, spec.Duration, spec.Curve, spec.Delay));
    }
}
