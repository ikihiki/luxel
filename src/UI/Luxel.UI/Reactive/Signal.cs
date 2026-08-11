namespace Luxel.UI;

/// <summary>依存追跡される signal/computed が実装する。</summary>
internal interface ISignalSource
{
    void Unsubscribe(ReactiveEffect effect);
}

/// <summary>
/// リアクティブな副作用。実行中に読まれた signal を自動購読し、いずれかが変わると再実行される
/// (Preact/Solid 風)。<see cref="Current"/> でアクティブ Effect を追跡する。
/// </summary>
public sealed class ReactiveEffect : IDisposable
{
    [ThreadStatic] internal static ReactiveEffect? Current;

    private readonly Action _run;
    internal readonly HashSet<ISignalSource> Deps = new();
    private bool _disposed;

    internal ReactiveEffect(Action run)
    {
        _run = run;
        Execute();
    }

    internal void Execute()
    {
        if (_disposed) return;
        foreach (ISignalSource d in Deps) d.Unsubscribe(this);
        Deps.Clear();

        ReactiveEffect? prev = Current;
        Current = this;
        try { _run(); }
        catch (Exception ex) { UiError.Report(ex, "Effect"); }   // エラー境界: effect は生かす (次の変化で再試行)
        finally { Current = prev; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (ISignalSource d in Deps) d.Unsubscribe(this);
        Deps.Clear();
    }
}

/// <summary>可変リアクティブ値。read で現在 Effect を依存登録、set で依存 Effect を再実行する。</summary>
public sealed class Signal<T> : ISignalSource
{
    private T _value;
    private readonly HashSet<ReactiveEffect> _subs = new();

    public Signal(T initial = default!) => _value = initial;

    /// <summary>Raised synchronously after the value changes. Hosts use this for bidirectional story args.</summary>
    public event Action<T>? Changed;

    public T Value
    {
        get { Track(); return _value; }
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value)) return;
            _value = value;
            Notify();
            Changed?.Invoke(value);
        }
    }

    /// <summary>依存追跡せずに読む。</summary>
    public T Peek() => _value;

    private void Track()
    {
        ReactiveEffect? e = ReactiveEffect.Current;
        if (e != null && _subs.Add(e)) e.Deps.Add(this);
    }

    private void Notify()
    {
        foreach (ReactiveEffect e in _subs.ToArray()) e.Execute();
    }

    void ISignalSource.Unsubscribe(ReactiveEffect e) => _subs.Remove(e);

    public override string ToString() => _value?.ToString() ?? "";
}

/// <summary>他の signal から導出されるリアクティブ値 (依存変化で再計算しキャッシュ)。</summary>
public sealed class Computed<T> : ISignalSource, IDisposable
{
    private T _value = default!;
    private readonly HashSet<ReactiveEffect> _subs = new();
    private readonly ReactiveEffect _effect;

    public Computed(Func<T> compute)
    {
        _effect = new ReactiveEffect(() =>
        {
            T v = compute();
            if (!EqualityComparer<T>.Default.Equals(_value, v)) { _value = v; Notify(); }
        });
    }

    public T Value { get { Track(); return _value; } }

    private void Track()
    {
        ReactiveEffect? e = ReactiveEffect.Current;
        if (e != null && _subs.Add(e)) e.Deps.Add(this);
    }

    private void Notify()
    {
        foreach (ReactiveEffect e in _subs.ToArray()) e.Execute();
    }

    void ISignalSource.Unsubscribe(ReactiveEffect e) => _subs.Remove(e);
    public void Dispose() => _effect.Dispose();
}

/// <summary>リアクティブ ユーティリティ。</summary>
public static class Reactive
{
    /// <summary>副作用を登録する。依存 signal が変わるたび再実行。Dispose で停止。</summary>
    public static IDisposable Effect(Action run) => new ReactiveEffect(run);

    /// <summary>本体を **1 回だけ** 依存追跡付きで実行し、読んだ signal が変化したら本体を再実行せず
    /// <paramref name="onInvalidate"/> を 1 回呼ぶ (Flutter の markNeedsBuild 相当 —
    /// CompositeControl の TrackedBuild 用)。通知後は依存が空になるため自然にワンショット。
    /// subscription の Dispose で購読解除。</summary>
    public static (T result, IDisposable subscription) Track<T>(Func<T> body, Action onInvalidate)
    {
        T result = default!;
        bool first = true;
        var eff = new ReactiveEffect(() =>
        {
            // 初回 = 本体 (依存捕捉)。以降 = 無効化通知のみ — Execute が依存をクリアしてから
            // 走るので、通知後この effect は不活性になる (次の Track が新しい購読を張る)。
            if (first) { first = false; result = body(); }
            else onInvalidate();
        });
        return (result, eff);
    }
}
