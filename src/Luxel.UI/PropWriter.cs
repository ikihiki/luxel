namespace Luxel.UI;

/// <summary>
/// ソース生成された <see cref="Widget.SetProp{T}"/> の switch case から呼ばれる書込ヘルパ。
/// <c>typeof(T) == typeof(TField)</c> は JIT の特殊化で定数畳み込みされ、
/// 一致時は参照キャストのみの直書きになる。フィールドは差し替えず
/// <see cref="Bindable{T}.SetBase"/> / <see cref="Bindable{T}.SetState"/> で中身を書く。
/// </summary>
public static class PropWriter
{
    /// <summary>型が一致すればフィールドへ書き込んで true (Default=基底差し替え、それ以外=状態レイヤ追加)。</summary>
    public static bool Set<TField, T>(Bindable<TField> field, WidgetState state, Bindable<T> value, Widget owner)
    {
        if (typeof(T) != typeof(TField)) return false;
        var v = (Bindable<TField>)(object)value;
        if (state == WidgetState.Default) field.SetBase(v);
        else field.SetState(state, v, owner);
        return true;
    }

    /// <summary>BindableString フィールド版 (T=string のみ受理)。Signal/Func 束縛は Get 委譲で維持される。</summary>
    public static bool Set<T>(BindableString field, WidgetState state, Bindable<T> value, Widget owner)
    {
        if (typeof(T) != typeof(string)) return false;
        var v = (Bindable<string>)(object)value;
        BindableString bs = v.IsReactive ? new BindableString(() => v.Get()) : new BindableString(v.Get() ?? "");
        if (state == WidgetState.Default) field.SetBase(bs);
        else field.SetState(state, bs, owner);
        return true;
    }
}
