namespace Luxel.UI;

/// <summary>状態 (hover:/active: 等) を後から差し替えられるパーツ。<c>S.On(state, ...)</c> が使う。</summary>
public interface IStatePart : IConfigPart
{
    /// <summary>同じ内容で state だけ差し替えた新しいパーツを返す。</summary>
    IConfigPart WithState(WidgetState state);
}

/// <summary>
/// 名前ベースのプロパティ書込パーツ。<see cref="Widget.SetProp{T}"/> (ソース生成 switch) 経由で
/// **任意の widget の任意の [UiParam] プロパティ**を型安全・boxing なしに書く。
/// 候補名を複数持てる (例: Fg は Button では "Foreground"、Text では "Color")。
/// 名前も型も合わない widget には何もしない (Tailwind utility の「効かないクラスは無視」と同じ)。
/// </summary>
public sealed class PropPart<T>(string[] names, Bindable<T> value, WidgetState state = WidgetState.Default) : IStatePart
{
    public void Apply(Widget target)
    {
        foreach (string n in names)
            if (target.SetProp(n, state, value)) return;
    }

    public IConfigPart WithState(WidgetState newState) => new PropPart<T>(names, value, newState);
}

/// <summary>状態 variant パーツ。内部の <see cref="IStatePart"/> を state 付きで適用し直す (<c>S.On</c> の実体)。</summary>
public sealed class OnVariantPart(WidgetState state, IConfigPart[] utilities) : IConfigPart
{
    public void Apply(Widget target)
    {
        foreach (IConfigPart u in utilities)
        {
            if (u is IStatePart sp) sp.WithState(state).Apply(target);
            else u.Apply(target);
        }
    }
}

/// <summary>複数パーツをまとめて適用する (Border(color,width) のような複合 utility 用)。
/// <c>On(...)</c> 内でも使えるよう state は内側の <see cref="IStatePart"/> に伝播する。</summary>
public sealed class MultiPart(IConfigPart[] parts) : IStatePart
{
    public void Apply(Widget target)
    {
        foreach (IConfigPart p in parts) p.Apply(target);
    }

    public IConfigPart WithState(WidgetState state)
    {
        var mapped = new IConfigPart[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            mapped[i] = parts[i] is IStatePart sp ? sp.WithState(state) : parts[i];
        return new MultiPart(mapped);
    }
}
