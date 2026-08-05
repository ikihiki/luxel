using Luxel.Input;

namespace Luxel.Gallery;

/// <summary>Gallery storyへ物理入力を渡すhost capability。</summary>
public interface IStoryInputRuntime
{
    bool IsFocused { get; }
    void Poll(InputBus bus);
}

/// <summary>
/// host windowの入力をpreview focusでgateする共有adapter。inactive中もsourceをdrainし、
/// previewからfocusが外れたときはstoryへ渡したheld keys/buttonsをreleaseする。
/// </summary>
public sealed class StoryInputRuntime : IStoryInputRuntime, IDisposable
{
    private readonly InputBus _sourceBus = new();
    private readonly List<InputEvent> _pending = new();
    private readonly HashSet<KeyCode> _forwardedHeld = new();
    private IInputSource? _source;
    private IDisposable? _ownedSource;
    private bool _focused;
    private bool _releasePending;
    private bool _disposed;

    public bool IsFocused => _focused;

    public void Attach(IInputSource source, bool ownsSource = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        _ownedSource?.Dispose();
        _source = source;
        _ownedSource = ownsSource ? source as IDisposable : null;
        _forwardedHeld.Clear();
        _pending.Clear();
        _sourceBus.Clear();
    }

    public void SetFocused(bool focused)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_focused == focused) return;
        _focused = focused;
        if (!focused)
        {
            _pending.Clear();
            if (_forwardedHeld.Count > 0) _releasePending = true;
        }
    }

    /// <summary>host frameごとにsourceをdrainし、focused story向けeventだけを保持する。</summary>
    public void Update()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _source?.Poll(_sourceBus);
        foreach (InputEvent input in _sourceBus.Events)
        {
            if (!_focused) continue;
            if (input.Kind == InputEventKind.KeyDown) _forwardedHeld.Add(input.Key);
            else if (input.Kind == InputEventKind.KeyUp) _forwardedHeld.Remove(input.Key);
            _pending.Add(input);
        }
        _sourceBus.Clear();
    }

    public void Poll(InputBus bus)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(bus);

        if (_releasePending)
        {
            foreach (KeyCode key in _forwardedHeld.Order())
                bus.Enqueue(new(InputEventKind.KeyUp, key, AxisCode.None, 0f, 0f, 0));
            _forwardedHeld.Clear();
            _releasePending = false;
        }

        foreach (InputEvent input in _pending) bus.Enqueue(input);
        _pending.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ownedSource?.Dispose();
        _ownedSource = null;
        _source = null;
        _sourceBus.Clear();
        _pending.Clear();
        _forwardedHeld.Clear();
    }
}
