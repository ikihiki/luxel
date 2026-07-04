using Luxel.UI;

namespace Luxel.Input;

/// <summary>
/// state (enum) と <see cref="InputContext"/> のマッピング。<see cref="Signal{TState}"/> の変化を
/// per-tick で拾い、必要なら stack の top を切り替える (INPUT-M5)。
///
/// <code>
/// enum AppState { Menu, Gameplay }
/// var sm = new InputStateMachine&lt;AppState&gt;(stack, AppState.Gameplay);
/// sm.Register(AppState.Menu, menuCtx);
/// sm.Register(AppState.Gameplay, gameplayCtx);
/// // per-frame:
/// sm.State.Value = AppState.Menu;   // どこからでも set
/// sm.Sync();   // stack を切り替え
/// </code>
///
/// state を切り替えると、旧 context は stack から Pop され新 context が Push される (常に top を占有)。
/// 追加の永続 context (常時走る global) を持つ場合は本 SM の外で Push する。
/// </summary>
public sealed class InputStateMachine<TState> where TState : struct, Enum
{
    public Signal<TState> State { get; }
    private readonly InputStack _stack;
    private readonly Dictionary<TState, InputContext> _map = new();
    private InputContext? _current;
    private TState _lastApplied;

    public InputStateMachine(InputStack stack, TState initial)
    {
        _stack = stack;
        State = new Signal<TState>(initial);
        _lastApplied = initial;
    }

    /// <summary>指定 state のときに使う context を登録。</summary>
    public InputStateMachine<TState> Register(TState state, InputContext ctx)
    {
        _map[state] = ctx;
        return this;
    }

    /// <summary>初回 Sync 前に active な状態の context を stack に push (setup 完了時に呼ぶ)。</summary>
    public void Activate()
    {
        if (_map.TryGetValue(State.Peek(), out var ctx))
        {
            _stack.Push(ctx);
            _current = ctx;
            _lastApplied = State.Peek();
        }
    }

    /// <summary>per-tick 呼び出し ─ State が変化していたら stack を切り替える。</summary>
    public void Sync()
    {
        var cur = State.Peek();
        if (EqualityComparer<TState>.Default.Equals(cur, _lastApplied)) return;
        if (_current != null)
        {
            // top から pop する前提 (state SM が管理する context は常に top)
            _stack.Pop();
            _current = null;
        }
        if (_map.TryGetValue(cur, out var ctx))
        {
            _stack.Push(ctx);
            _current = ctx;
        }
        _lastApplied = cur;
    }
}
