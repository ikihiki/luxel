using System.Text.Json;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

public sealed record EditorKeyBinding(string CommandId, string? Gesture);
public sealed record EditorKeymapIssue(string CommandId, string Message);

public sealed class EditorKeymap
{
    public const string SettingsKey = "editor.keymap.v1";
    private readonly CommandRegistry _commands;
    private readonly IEditorSettingsStore _store;
    private readonly Dictionary<string, string?> _overrides = new(StringComparer.Ordinal);

    public EditorKeymap(CommandRegistry commands, IEditorSettingsStore store)
    {
        _commands = commands;
        _store = store;
        Load();
    }

    public Signal<int> Version { get; } = new(0);
    public IReadOnlyDictionary<string, string?> Overrides { get { _ = Version.Value; return _overrides; } }

    public IReadOnlyList<EditorKeymapIssue> Validate(IEnumerable<EditorKeyBinding> bindings)
    {
        var issues = new List<EditorKeymapIssue>();
        EditorKeyBinding[] proposed = bindings.ToArray();
        var proposedIds = proposed.Select(x => x.CommandId).ToHashSet(StringComparer.Ordinal);
        var used = new Dictionary<KeyGesture, string>();
        foreach (Command command in _commands.Commands)
            if (!proposedIds.Contains(command.Id) && _commands.EffectiveGesture(command.Id) is { } existing)
                used.TryAdd(existing, command.Id);
        foreach (EditorKeyBinding binding in proposed)
        {
            if (_commands.Find(binding.CommandId) is null)
            {
                issues.Add(new(binding.CommandId, "Unknown command."));
                continue;
            }
            if (binding.Gesture is null) continue;
            KeyGesture? gesture = KeyGestures.Parse(binding.Gesture);
            if (gesture is null)
            {
                issues.Add(new(binding.CommandId, "Invalid gesture."));
                continue;
            }
            if (used.TryGetValue(gesture.Value, out string? other))
            {
                issues.Add(new(binding.CommandId, $"Gesture conflicts with {other}."));
                continue;
            }
            used[gesture.Value] = binding.CommandId;
        }
        return issues;
    }

    public IReadOnlyList<EditorKeymapIssue> Apply(IEnumerable<EditorKeyBinding> bindings)
    {
        EditorKeyBinding[] values = bindings.ToArray();
        IReadOnlyList<EditorKeymapIssue> issues = Validate(values);
        if (issues.Count > 0) return issues;
        foreach (EditorKeyBinding binding in values)
        {
            _overrides[binding.CommandId] = binding.Gesture;
            _commands.SetGestureOverride(binding.CommandId,
                binding.Gesture is null ? null : KeyGestures.Parse(binding.Gesture));
        }
        Persist();
        Version.Value++;
        return [];
    }

    public void Reset(string commandId)
    {
        _overrides.Remove(commandId);
        _commands.ResetGestureOverride(commandId);
        Persist();
        Version.Value++;
    }

    public void ResetAll()
    {
        _overrides.Clear();
        _commands.ResetGestureOverrides();
        Persist();
        Version.Value++;
    }

    private void Load()
    {
        string? json = _store.Read(SettingsKey);
        if (string.IsNullOrWhiteSpace(json)) return;
        Dictionary<string, string?>? loaded = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
        if (loaded is null) return;
        foreach ((string commandId, string? gesture) in loaded)
        {
            if (_commands.Find(commandId) is null) continue;
            if (gesture is not null && KeyGestures.Parse(gesture) is null) continue;
            _overrides[commandId] = gesture;
            _commands.SetGestureOverride(commandId, gesture is null ? null : KeyGestures.Parse(gesture));
        }
    }

    private void Persist() => _store.Write(SettingsKey, JsonSerializer.Serialize(_overrides));
}

public sealed class KeyBindingsView : CompositeControl
{
    private readonly Signal<string> _command = new("");
    private readonly Signal<string> _gesture = new("");

    public KeyBindingsView(EditorKeymap keymap) => Keymap = keymap;
    public EditorKeymap Keymap { get; }
    public Signal<IReadOnlyList<EditorKeymapIssue>> Issues { get; } = new([]);

    public bool Apply(string commandId, string? gesture)
    {
        IReadOnlyList<EditorKeymapIssue> issues = Keymap.Apply([new(commandId, gesture)]);
        Issues.Value = issues;
        return issues.Count == 0;
    }

    public bool Apply()
    {
        return Apply(_command.Peek(), string.IsNullOrWhiteSpace(_gesture.Peek()) ? null : _gesture.Peek());
    }

    public void ResetBinding()
    {
        if (!string.IsNullOrWhiteSpace(_command.Peek())) Keymap.Reset(_command.Peek());
        Issues.Value = [];
    }

    public void ResetAll()
    {
        Keymap.ResetAll();
        Issues.Value = [];
    }

    protected override Widget Build()
    {
        _ = Keymap.Version.Value;
        var rows = new List<Widget>
        {
            Text("Key Bindings"),
            HStack(6)[TextField(_command, placeholder: "command id", width: 200), TextField(_gesture, placeholder: "Ctrl+K", width: 140)],
            HStack(6)[Button(_ => Apply(), "Apply"), Button(_ => ResetBinding(), "Reset Binding"), Button(_ => ResetAll(), "Reset All")],
        };
        if (Keymap.Overrides.Count == 0) rows.Add(Muted("Default key bindings"));
        else rows.AddRange(Keymap.Overrides.Select(x => (Widget)Text($"{x.Key}: {x.Value ?? "Unbound"}")));
        rows.AddRange(Issues.Value.Select(x => (Widget)Text($"{x.CommandId}: {x.Message}")));
        return VStack(4)[rows.ToArray()];
    }
}
