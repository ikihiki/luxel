namespace Luxel.UI;

/// <summary>
/// 値直接 (<c>T</c>) / <see cref="Signal{T}"/> / <see cref="Func{T}"/> の 3 経路を 1 引数で受ける reactive 束縛。
/// <para>
/// <list type="bullet">
/// <item><c>Bindable&lt;uint&gt; bg = Tw.Slate100;</c> ─ 値直接 (暗黙変換)</item>
/// <item><c>Bindable&lt;uint&gt; bg = mySignal;</c> ─ <see cref="Signal{T}"/> 直接 (暗黙変換)</item>
/// <item><c>Bindable&lt;uint&gt; bg = Bind.From(() =&gt; theme.Value.Surface);</c> ─ ラムダは <see cref="Bind.From{T}"/> で wrap</item>
/// <item><c>new Bindable&lt;T&gt;()</c> は <c>HasValue = false</c> で「未設定」を表す</item>
/// </list>
/// </para>
/// <para><b>フィールドは差し替えず、中の値を書き換える</b>: [UiParam] フィールドは
/// <c>readonly Bindable&lt;T&gt; Foo = new();</c> で宣言し、書き込みは <see cref="SetBase"/> (基底値の
/// 差し替え — 状態レイヤ / override は維持される)。インスタンス自体の再代入を封じることで、
/// 積んだ状態レイヤや DevTools override が上書きで消える事故を型で防ぐ。</para>
/// <para><b>状態レイヤ (Tailwind の hover:/active: 相当)</b>: <see cref="SetState"/> で状態別の値を積める。
/// <see cref="Get"/> は owner widget の <see cref="Widget.IsStateActive"/> を読んで
/// 「アクティブな最優先状態のレイヤ &gt; 基底値」で解決する。判定は signal 経由なので
/// Effect 内で読めば状態変化に自動追従する。</para>
/// <para><b>DevTools override</b>: <see cref="SetOverride"/> は状態レイヤよりさらに優先。</para>
/// </summary>
public sealed class Bindable<T> : IBindable
{
    private T _value = default!;
    private Func<T>? _getter;
    private bool _set;
    private Signal<T>? _override;   // DevTools 書換用、null=素通し
    private StateSlots? _states;    // 状態別レイヤ、null=なし

    private sealed class StateSlots
    {
        public Widget Owner = null!;
        public readonly Bindable<T>?[] Values = new Bindable<T>?[7];   // WidgetState のメンバー数
        public byte Mask;
    }

    /// <summary>解決優先度 (Default 基底より先に見る順)。CSS の specificity 相当の固定順。</summary>
    private static readonly WidgetState[] StatePriority =
        [WidgetState.Disabled, WidgetState.Pressed, WidgetState.Hover, WidgetState.Focused, WidgetState.Checked, WidgetState.Selected];

    /// <summary>未設定 (factory default 相当)。フィールド宣言 <c>= new()</c> で使う。</summary>
    public Bindable() { }
    public Bindable(T value)          { _value = value; _set = true; }
    public Bindable(Func<T> getter)   { _getter = getter; _set = true; }
    public Bindable(Signal<T> signal) { _getter = () => signal.Value; _set = true; }

    /// <summary>「未設定」(factory default) かどうか。状態レイヤ/override は含まない。</summary>
    public bool HasValue => _set;

    /// <summary>解決値があるか (override / アクティブ状態レイヤ / 基底値 の順に探す)。
    /// 状態判定は owner の signal を読むため、Effect 内なら依存追跡される。</summary>
    public bool TryGet(out T value)
    {
        if (_override is not null) { value = _override.Value; return true; }
        if (_states is { Mask: not 0 } ss)
        {
            foreach (WidgetState s in StatePriority)
            {
                if ((ss.Mask & (1 << (int)s)) == 0) continue;
                if (ss.Owner.IsStateActive(s)) { value = ss.Values[(int)s]!.Get(); return true; }
            }
        }
        if (_set) { value = _getter is not null ? _getter() : _value; return true; }
        value = default!;
        return false;
    }

    /// <summary>現在値を取得 (override &gt; アクティブ状態レイヤ &gt; 基底)。未設定は default(T)。</summary>
    public T Get() => TryGet(out T v) ? v : default!;

    /// <summary>未設定なら fallback。override / アクティブ状態レイヤも解決に含む。
    /// 旧 <c>float? Width ?? x</c> 相当の読み出し。</summary>
    public T Or(T fallback) => TryGet(out T v) ? v : fallback;

    /// <summary>Func/Signal 経由かどうか (true なら値変化に追従)。override は含まない。</summary>
    public bool IsReactive => _getter is not null;

    /// <summary>基底値を書き換える (状態レイヤ / DevTools override は維持)。値/Signal/Func は
    /// 暗黙変換で渡せる (<c>w.Background.SetBase(Tw.Red500)</c>)。ソース生成ファクトリ/SetProp が使う。</summary>
    public void SetBase(Bindable<T> v)
    {
        _value = v._value;
        _getter = v._getter;
        _set = v._set;
    }

    /// <summary>状態レイヤを積む (Tailwind の <c>hover:bg-...</c> 相当)。値は Bindable なので Signal/Func も可。
    /// owner の <see cref="Widget.IsStateActive"/> が判定に使われる。</summary>
    public void SetState(WidgetState state, Bindable<T> value, Widget owner)
    {
        _states ??= new StateSlots();
        _states.Owner = owner;
        _states.Values[(int)state] = value;
        _states.Mask |= (byte)(1 << (int)state);
    }

    /// <summary>DevTools 用書き戻し。内部 Signal を作成/更新して以後 <see cref="Get"/> が override を返す。</summary>
    public void SetOverride(T value)
    {
        if (_override is null) _override = new Signal<T>(value);
        else _override.Value = value;
    }

    /// <summary>override を除去して元の Bindable 挙動に戻す。</summary>
    public void ClearOverride() { _override = null; }

    public bool HasOverride => _override is not null;

    // ---- IBindable (非ジェネリック経由の boxed アクセス) ----
    object? IBindable.GetBoxed() => Get();
    System.Type IBindable.ValueType => typeof(T);
    void IBindable.SetOverrideBoxed(object? value)
    {
        if (value is T typed) SetOverride(typed);
        else if (value is null && default(T) is null) SetOverride(default!);
        else SetOverride((T)System.Convert.ChangeType(value!, typeof(T))!);
    }
    bool IBindable.HasOverride => HasOverride;
    void IBindable.ClearOverrideBoxed() => ClearOverride();

    public static implicit operator Bindable<T>(T value)          => new(value);
    public static implicit operator Bindable<T>(Func<T> getter)   => new(getter);
    public static implicit operator Bindable<T>(Signal<T> signal) => new(signal);
}

/// <summary>Bindable&lt;T&gt; の非ジェネリック interface (boxed アクセス。DevTools 等の動的経路用)。</summary>
public interface IBindable
{
    object? GetBoxed();
    System.Type ValueType { get; }
    void SetOverrideBoxed(object? value);
    bool HasOverride { get; }
    void ClearOverrideBoxed();
}

/// <summary>ラムダの暗黙変換が解決しない場合の helper (<c>Bind.From(() =&gt; x)</c>)。</summary>
public static class Bind
{
    public static Bindable<T> From<T>(Func<T> getter) => new(getter);
}
