namespace Luxel.UI;

/// <summary>
/// コールバックの UI パラメータ (EV)。<c>[UiEvent]</c> 付き public フィールドとして宣言すると、
/// ジェネレーターがファクトリの省略可能引数 (<c>Action&lt;...&gt;?</c>) に出す — コンストラクタに
/// コールバック引数を持たせないための <see cref="Bindable{T}"/> の対 (値ではなく動作)。
/// <para>**規約: 第一引数は発火元の UI 自身 (sender)** — ハンドラはイベントを起こしたコントロールへ
/// 型付きでアクセスできる (`onSelect: (lv, i) => lv.ScrollTo(i)` — 自己参照の循環宣言問題も解消)。</para>
/// <list type="bullet">
/// <item>状態レイヤ/アニメ/DevTools 値編集の対象にはならない (動作は補間できない)</item>
/// <item><see cref="Action{T}"/> からの暗黙変換で <c>w.OnClick = b =&gt; ...;</c> と書ける</item>
/// <item>sender のみのイベントは生成される <see cref="Widget.InvokeEvent"/> でテストから発火できる</item>
/// </list>
/// </summary>
public struct UiEvent<TSender>
{
    private Action<TSender>? _handler;
    public UiEvent(Action<TSender>? handler) => _handler = handler;
    public readonly bool HasHandler => _handler is not null;
    public readonly void Invoke(TSender sender) => _handler?.Invoke(sender);
    public static implicit operator UiEvent<TSender>(Action<TSender>? handler) => new(handler);
}

/// <summary>sender + 引数 1 つ (例: 行選択の index)。</summary>
public struct UiEvent<TSender, T>
{
    private Action<TSender, T>? _handler;
    public UiEvent(Action<TSender, T>? handler) => _handler = handler;
    public readonly bool HasHandler => _handler is not null;
    public readonly void Invoke(TSender sender, T arg) => _handler?.Invoke(sender, arg);
    public static implicit operator UiEvent<TSender, T>(Action<TSender, T>? handler) => new(handler);
}

/// <summary>sender + 引数 2 つ (例: 並べ替えの from/to)。</summary>
public struct UiEvent<TSender, T1, T2>
{
    private Action<TSender, T1, T2>? _handler;
    public UiEvent(Action<TSender, T1, T2>? handler) => _handler = handler;
    public readonly bool HasHandler => _handler is not null;
    public readonly void Invoke(TSender sender, T1 a, T2 b) => _handler?.Invoke(sender, a, b);
    public static implicit operator UiEvent<TSender, T1, T2>(Action<TSender, T1, T2>? handler) => new(handler);
}

/// <summary>ファクトリ引数にするコールバックフィールド (<see cref="UiEvent{TSender}"/> 系型のみ)。</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class UiEventAttribute : Attribute { }
