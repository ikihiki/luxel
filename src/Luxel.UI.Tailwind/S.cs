using Luxel.UI;

namespace Luxel.UI.Tailwind;

/// <summary>
/// Tailwind 風 utility ファクトリ。各メソッドは <see cref="IConfigPart"/> (<see cref="PropPart{T}"/>) を返し、
/// Widget の <c>parts:</c> 引数に渡される。Apply 時に <see cref="Widget.SetProp{T}"/> (ソース生成 switch) 経由で
/// **任意の widget の任意の [UiParam] プロパティ**へ書き込む — widget 別の分岐は存在しない。
///
/// <para>使用例:</para>
/// <code>
/// using Luxel.UI.Tailwind;
/// using static Luxel.UI.Tailwind.S;
///
/// Button("OK", onClick,
///     Bg(Tw.Blue500), Fg(Tw.White), Rounded(Tw.RoundedMd), W(180), H(60),
///     On(WidgetState.Hover, Bg(Tw.Blue600), Scale(1.05f)),
///     On(WidgetState.Pressed, Scale(0.95f)));
/// </code>
///
/// <para>値は <see cref="Bindable{T}"/> なので値直接 / Signal / <c>Bind.From(() =&gt; ...)</c> をすべて受ける。
/// ファクトリ引数と utility が共存する場合、**後勝ち**で utility が引数値を上書きする (CSS specificity 同様)。</para>
/// </summary>
public static class S
{
    // === 視覚 ===
    public static IConfigPart Bg(Bindable<uint> color) => Prop("Background", color);
    /// <summary>前景色。Button/コントロールでは "Foreground"、Text では "Color" に解決される。</summary>
    public static IConfigPart Fg(Bindable<uint> color) => new PropPart<uint>(["Foreground", "Color"], color);
    public static IConfigPart Opacity(Bindable<float> a) => Prop("Opacity", a);

    // === 変形 ===
    public static IConfigPart Scale(Bindable<float> v) => Prop("Scale", v);
    public static IConfigPart Rotate(Bindable<float> radians) => Prop("Rotate", radians);

    // === 境界 ===
    public static IConfigPart Rounded(Bindable<float> r) => Prop("Rounded", r);
    public static IConfigPart Border(Bindable<uint> color, Bindable<float>? width = null)
        => new MultiPart([Prop("BorderColor", color), Prop("BorderWidth", width ?? 1f)]);

    // === レイアウト ===
    public static IConfigPart P(float all) => Prop<Thickness>("Padding", new Thickness(all));
    public static IConfigPart P(float h, float v) => Prop<Thickness>("Padding", new Thickness(h, v));
    public static IConfigPart Px(float n) => Prop<Thickness>("Padding", new Thickness(n, 0));
    public static IConfigPart Py(float n) => Prop<Thickness>("Padding", new Thickness(0, n));
    public static IConfigPart M(float all) => Prop<Thickness>("Margin", new Thickness(all));
    public static IConfigPart M(float h, float v) => Prop<Thickness>("Margin", new Thickness(h, v));
    public static IConfigPart W(Bindable<float> v) => Prop("Width", v);
    public static IConfigPart H(Bindable<float> v) => Prop("Height", v);

    // === タイポグラフィ ===
    public static IConfigPart FontSize(Bindable<float> v) => Prop("FontSize", v);

    // === 汎用 (任意の [UiParam] プロパティを名前指定) ===
    public static IConfigPart Prop<T>(string name, Bindable<T> value) => new PropPart<T>([name], value);

    // === 状態 variant (Tailwind の hover:/focus:/active:/disabled:/checked:) ===

    /// <summary>指定状態のレイヤに utility を当てる。Tailwind の <c>hover:</c>/<c>active:</c> 等と等価。
    /// 対象プロパティは限定されない — [UiParam] な全プロパティが状態対応。</summary>
    public static IConfigPart On(WidgetState state, params IConfigPart[] utilities)
        => new OnVariantPart(state, utilities);
}
