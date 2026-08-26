using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>VS Code-like keybinding entry: { "key", "command", "args"? }.</summary>
public sealed record EditorKeyBinding(
    [property: JsonPropertyName("command"), JsonPropertyOrder(1)] string CommandId,
    [property: JsonPropertyName("key"), JsonPropertyOrder(0)] string? Gesture)
{
    private readonly JsonElement? _arguments;

    [JsonConstructor]
    public EditorKeyBinding(string CommandId, string? Gesture, JsonElement? Arguments)
        : this(CommandId, Gesture)
    {
        _arguments = Arguments is { } value ? value.Clone() : null;
    }

    [JsonPropertyName("args"), JsonPropertyOrder(2)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Arguments
    {
        get => _arguments;
        init => _arguments = value is { } element ? element.Clone() : null;
    }

    [JsonIgnore] public string Command => CommandId;
    [JsonIgnore] public string? Key => Gesture;
    [JsonIgnore] public JsonElement? Args => Arguments;
    [JsonIgnore] public bool IsRemoval => CommandId.StartsWith('-');
    [JsonIgnore] public string TargetCommandId => IsRemoval ? CommandId[1..] : CommandId;
}

public sealed record EditorKeymapIssue(string CommandId, string Message)
{
    public string Command => CommandId;
}

public enum EditorKeyDispatchStatus { NoMatch, Pending, Cancelled, Completed }

public sealed record EditorKeyDispatchResult(EditorKeyDispatchStatus Status,
                                             CommandExecutionResult? Execution = null,
                                             bool Retried = false)
{
    public bool Handled => Status != EditorKeyDispatchStatus.NoMatch;
    public bool Pending => Status == EditorKeyDispatchStatus.Pending;
    public bool Executed => Execution?.Executed == true;
}

public sealed class EditorKeymap
{
    public const string SettingsKey = "editor.keymap.v1";
    public static readonly TimeSpan DefaultChordTimeout = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly CommandRegistry _commands;
    private readonly IEditorSettingsStore _store;
    private readonly Dictionary<string, string?> _overrides = new(StringComparer.Ordinal);
    private readonly List<EditorKeyBinding> _bindings = [];
    private readonly HashSet<string> _appliedIds = new(StringComparer.Ordinal);
    private readonly List<KeyGesture> _pending = [];
    private DateTimeOffset _pendingDeadline;
    private DateTimeOffset _pendingObservedAt;

    public EditorKeymap(CommandRegistry commands, IEditorSettingsStore store, TimeSpan? chordTimeout = null)
    {
        _commands = commands;
        _store = store;
        ChordTimeout = chordTimeout ?? DefaultChordTimeout;
        if (ChordTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(chordTimeout));
        Load();
    }

    public TimeSpan ChordTimeout { get; }
    public Signal<int> Version { get; } = new(0);
    public IReadOnlyDictionary<string, string?> Overrides { get { _ = Version.Value; return _overrides; } }
    public IReadOnlyList<EditorKeyBinding> Bindings { get { _ = Version.Value; return _bindings.ToArray(); } }
    public bool HasPendingChord => _pending.Count > 0;
    public IReadOnlyList<KeyGesture> PendingGestures => _pending.ToArray();
    public DateTimeOffset? PendingDeadline => HasPendingChord ? _pendingDeadline : null;

    public IReadOnlyList<CommandDescriptor> CommandDescriptors => _commands.Descriptors()
        .OrderBy(x => x.Title, StringComparer.Ordinal).ToArray();

    /// <summary>永続化される ordered keybinding array の snapshot。</summary>
    public IReadOnlyList<EditorKeyBinding> Get() => Bindings;

    public EditorKeyBinding? Get(string commandId)
        => _bindings.LastOrDefault(x => x.TargetCommandId == commandId);

    public string? EffectiveBinding(string commandId)
        => _commands.EffectiveGestureSequence(commandId) is { } sequence ? KeyGestures.Format(sequence) : null;

    public JsonElement? EffectiveArguments(string commandId)
    {
        EditorKeyBinding? binding = _bindings.LastOrDefault(x => x.TargetCommandId == commandId && !x.IsRemoval);
        return binding?.Arguments is { } arguments ? arguments.Clone() : null;
    }

    public IReadOnlyList<EditorKeymapIssue> Validate(IEnumerable<EditorKeyBinding> bindings)
    {
        var issues = new List<EditorKeymapIssue>();
        EditorKeyBinding[] proposed = bindings.ToArray();
        var proposedIds = proposed.Select(x => x.TargetCommandId).ToHashSet(StringComparer.Ordinal);
        var used = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Command command in _commands.Commands)
            if (!proposedIds.Contains(command.Id) && _commands.EffectiveGestureSequence(command.Id) is { } existing)
                used.TryAdd(KeyGestures.Format(existing), command.Id);

        foreach (EditorKeyBinding binding in proposed)
        {
            string commandId = binding.TargetCommandId;
            Command? command = string.IsNullOrWhiteSpace(commandId) ? null : _commands.Find(commandId);
            if (command is null)
            {
                issues.Add(new(commandId, "Unknown command."));
                continue;
            }
            bool removesBinding = binding.IsRemoval || binding.Gesture is null;
            if (removesBinding && HasArguments(binding.Arguments))
            {
                issues.Add(new(commandId, "Removal bindings cannot contain arguments."));
                continue;
            }
            if (!removesBinding && ValidateArguments(command, binding.Arguments) is { } argumentError)
            {
                issues.Add(new(commandId, argumentError));
                continue;
            }
            if (binding.Gesture is null) continue;
            KeyGestureSequence? sequence = KeyGestures.ParseSequence(binding.Gesture);
            if (sequence is null)
            {
                issues.Add(new(commandId, "Invalid key gesture or chord sequence."));
                continue;
            }
            if (binding.IsRemoval) continue;
            string canonical = KeyGestures.Format(sequence);
            if (used.TryGetValue(canonical, out string? other))
            {
                issues.Add(new(commandId, $"Gesture conflicts with {other}."));
                continue;
            }
            used[canonical] = commandId;
        }
        return issues;
    }

    public IReadOnlyList<EditorKeymapIssue> Update(EditorKeyBinding binding) => Update([binding]);
    public IReadOnlyList<EditorKeymapIssue> Update(IEnumerable<EditorKeyBinding> bindings) => Apply(bindings);

    /// <summary>互換 API。指定された command だけを ordered array 上で置換して保存する。</summary>
    public IReadOnlyList<EditorKeymapIssue> Apply(IEnumerable<EditorKeyBinding> bindings)
    {
        EditorKeyBinding[] values = bindings.ToArray();
        IReadOnlyList<EditorKeymapIssue> issues = Validate(values);
        if (issues.Count > 0) return issues;
        foreach (EditorKeyBinding value in values)
        {
            EditorKeyBinding binding = Normalize(value);
            _bindings.RemoveAll(x => x.TargetCommandId == binding.TargetCommandId);
            _bindings.Add(binding);
        }
        ResetPendingChord();
        Reapply();
        Persist();
        Version.Value++;
        return [];
    }

    public void Reset(string commandId)
    {
        _bindings.RemoveAll(x => x.TargetCommandId == commandId);
        _overrides.Remove(commandId);
        _appliedIds.Remove(commandId);
        _commands.ResetGestureOverride(commandId);
        ResetPendingChord();
        Persist();
        Version.Value++;
    }

    public void ResetAll()
    {
        foreach (string commandId in _appliedIds.ToArray()) _commands.ResetGestureOverride(commandId);
        _appliedIds.Clear();
        _bindings.Clear();
        _overrides.Clear();
        ResetPendingChord();
        Persist();
        Version.Value++;
    }

    /// <summary>
    /// Advances the deterministic chord clock. Regressing timestamps are clamped to the latest
    /// timestamp observed while the current chord is pending, so they contribute zero elapsed time.
    /// </summary>
    public bool AdvanceTime(DateTimeOffset timestamp)
    {
        if (!HasPendingChord) return false;
        timestamp = ObservePendingTimestamp(timestamp);
        if (timestamp < _pendingDeadline) return false;
        ResetPendingChord();
        return true;
    }

    public void ResetPendingChord()
    {
        if (_pending.Count == 0) return;
        _pending.Clear();
        _pendingDeadline = default;
        _pendingObservedAt = default;
        Version.Value = Version.Peek() + 1;
    }

    public EditorKeyDispatchResult HandleKey(Key key, KeyModifiers modifiers, DateTimeOffset timestamp,
        IReadOnlyList<CommandContribution>? contributions = null)
        => HandleKey(new KeyGesture(key, modifiers.HasFlag(KeyModifiers.Ctrl),
            modifiers.HasFlag(KeyModifiers.Shift), modifiers.HasFlag(KeyModifiers.Alt)), timestamp, contributions);

    public EditorKeyDispatchResult HandleKey(KeyGesture gesture, DateTimeOffset timestamp,
        IReadOnlyList<CommandContribution>? contributions = null)
    {
        AdvanceTime(timestamp);
        if (HasPendingChord) timestamp = ObservePendingTimestamp(timestamp);
        if (gesture == new KeyGesture(Key.Escape))
        {
            if (!HasPendingChord) return new(EditorKeyDispatchStatus.NoMatch);
            ResetPendingChord();
            return new(EditorKeyDispatchStatus.Cancelled);
        }

        if (!HasPendingChord) return DispatchFresh(gesture, timestamp, contributions, retried: false);

        var attempted = _pending.Append(gesture).ToArray();
        IReadOnlyList<EffectiveTarget> targets = EffectiveTargets(contributions);
        EffectiveTarget[] matches = targets.Where(x => KeyGestures.StartsWith(x.Sequence, attempted)).ToArray();
        if (matches.Length == 0)
        {
            ResetPendingChord();
            EditorKeyDispatchResult retry = DispatchFresh(gesture, timestamp, contributions, retried: true);
            return retry.Status == EditorKeyDispatchStatus.NoMatch
                ? new(EditorKeyDispatchStatus.Cancelled, Retried: true)
                : retry;
        }
        return ResolveMatches(attempted, matches, timestamp, contributions, retried: false);
    }

    public IDisposable BindShortcuts(UiHost host,
        Func<IReadOnlyList<CommandContribution>>? contributions = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        contributions ??= static () => [];
        clock ??= static () => DateTimeOffset.UtcNow;
        var bound = new List<KeyGesture>();

        void Rebind()
        {
            foreach (KeyGesture gesture in bound) host.UnregisterShortcut(gesture);
            bound.Clear();
            IReadOnlyList<CommandContribution> current = contributions();
            IEnumerable<KeyGesture> gestures = HasPendingChord
                ? AllChordInputGestures()
                : EffectiveTargets(current).Select(x => x.Sequence[0]).Distinct();
            foreach (KeyGesture gesture in gestures)
            {
                KeyGesture captured = gesture;
                host.RegisterShortcut(captured, () => HandleKey(captured, clock(), contributions()));
                bound.Add(captured);
            }
        }

        IDisposable effect = Reactive.Effect(() =>
        {
            _ = Version.Value;
            _ = _commands.Version.Value;
            _ = contributions();
            Rebind();
        });
        return new Binder(() =>
        {
            effect.Dispose();
            foreach (KeyGesture gesture in bound) host.UnregisterShortcut(gesture);
            ResetPendingChord();
        });
    }

    private EditorKeyDispatchResult DispatchFresh(KeyGesture gesture, DateTimeOffset timestamp,
        IReadOnlyList<CommandContribution>? contributions, bool retried)
    {
        EffectiveTarget[] matches = EffectiveTargets(contributions)
            .Where(x => x.Sequence[0] == gesture).ToArray();
        if (matches.Length == 0) return new(EditorKeyDispatchStatus.NoMatch, Retried: retried);
        return ResolveMatches([gesture], matches, timestamp, contributions, retried);
    }

    private EditorKeyDispatchResult ResolveMatches(IReadOnlyList<KeyGesture> attempted,
        IReadOnlyList<EffectiveTarget> matches, DateTimeOffset timestamp,
        IReadOnlyList<CommandContribution>? contributions, bool retried)
    {
        // VS Code-like precedence: a chord prefix wins over an exact shorter binding.
        if (matches.Any(x => x.Sequence.Count > attempted.Count))
        {
            _pending.Clear();
            _pending.AddRange(attempted);
            _pendingObservedAt = timestamp;
            _pendingDeadline = timestamp + ChordTimeout;
            Version.Value = Version.Peek() + 1;
            return new(EditorKeyDispatchStatus.Pending, Retried: retried);
        }

        EffectiveTarget? exact = matches.FirstOrDefault(x => x.Sequence.Count == attempted.Count);
        ResetPendingChord();
        if (exact is null) return new(EditorKeyDispatchStatus.NoMatch, Retried: retried);
        var invocation = new CommandInvocationContext(exact.CommandId,
            exact.Arguments is { } arguments ? arguments.Clone() : null);
        CommandExecutionResult execution = _commands.Execute(invocation, contributions);
        return new(EditorKeyDispatchStatus.Completed, execution, retried);
    }

    private DateTimeOffset ObservePendingTimestamp(DateTimeOffset timestamp)
    {
        if (timestamp < _pendingObservedAt) return _pendingObservedAt;
        _pendingObservedAt = timestamp;
        return timestamp;
    }

    private IReadOnlyList<EffectiveTarget> EffectiveTargets(IReadOnlyList<CommandContribution>? contributions)
    {
        var result = new List<EffectiveTarget>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (contributions is not null)
            foreach (CommandContribution contribution in contributions)
                if (contribution.Command.DefaultGestureSequence is { } sequence && ids.Add(contribution.Command.Id))
                    result.Add(new(contribution.Command.Id, sequence, null));
        foreach (Command command in _commands.Commands)
        {
            if (!ids.Add(command.Id) || _commands.EffectiveGestureSequence(command.Id) is not { } sequence) continue;
            EditorKeyBinding? binding = _bindings.LastOrDefault(x => x.TargetCommandId == command.Id && !x.IsRemoval);
            result.Add(new(command.Id, sequence,
                binding?.Arguments is { } arguments ? arguments.Clone() : null));
        }
        return result;
    }

    private static IEnumerable<KeyGesture> AllChordInputGestures()
    {
        foreach (Key key in Enum.GetValues<Key>())
        {
            if (key == Key.None) continue;
            for (int bits = 0; bits < 8; bits++)
                yield return new KeyGesture(key, (bits & 1) != 0, (bits & 2) != 0, (bits & 4) != 0);
        }
    }

    private void Load()
    {
        string? json;
        try { json = _store.Read(SettingsKey); }
        catch { return; }
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            if (json.TrimStart().StartsWith('{'))
            {
                Dictionary<string, string?>? legacy = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
                if (legacy is not null)
                    _bindings.AddRange(legacy.Select(x => new EditorKeyBinding(x.Key, x.Value))
                        .Where(IsLoadable).Select(Normalize));
            }
            else
            {
                EditorKeyBinding[]? loaded = JsonSerializer.Deserialize<EditorKeyBinding[]>(json, JsonOptions);
                if (loaded is not null)
                    _bindings.AddRange(loaded.Where(IsLoadable).Select(Normalize));
            }
            Reapply();
        }
        catch (JsonException) { }
    }

    private void Reapply()
    {
        foreach (string commandId in _appliedIds.ToArray()) _commands.ResetGestureOverride(commandId);
        _appliedIds.Clear();
        _overrides.Clear();
        foreach (EditorKeyBinding binding in _bindings)
        {
            string commandId = binding.TargetCommandId;
            KeyGestureSequence? sequence = binding.IsRemoval || binding.Gesture is null
                ? null : KeyGestures.ParseSequence(binding.Gesture);
            _overrides[commandId] = sequence is { } parsed ? KeyGestures.Format(parsed) : null;
            _commands.DefineGestureSequenceOverride(commandId, sequence);
            _appliedIds.Add(commandId);
        }
    }

    private EditorKeyBinding Normalize(EditorKeyBinding binding)
    {
        string commandId = binding.TargetCommandId;
        JsonElement? arguments = binding.Arguments is { } value ? value.Clone() : null;
        if (binding.IsRemoval || binding.Gesture is null)
        {
            string? key = binding.Gesture ?? (_commands.EffectiveGestureSequence(commandId) is { } current
                ? KeyGestures.Format(current) : null);
            return new EditorKeyBinding($"-{commandId}", key);
        }
        KeyGestureSequence sequence = KeyGestures.ParseSequence(binding.Gesture)!;
        return new EditorKeyBinding(commandId, KeyGestures.Format(sequence), arguments);
    }

    private static bool IsLoadable(EditorKeyBinding binding)
        => !string.IsNullOrWhiteSpace(binding.TargetCommandId)
           && (binding.Gesture is null || KeyGestures.ParseSequence(binding.Gesture) is not null);

    private static string? ValidateArguments(Command command, JsonElement? args)
    {
        bool hasArguments = HasArguments(args);
        CommandArgumentSchema? schema = command.ArgumentSchema;
        if (hasArguments && command.Invoke is null)
            return $"Command '{command.Id}' does not accept arguments.";
        JsonElement? effective = hasArguments ? args?.Clone()
            : schema?.DefaultValue is { } defaultValue ? defaultValue.Clone() : null;
        if (!effective.HasValue && schema is { Required: true })
            return $"Command '{command.Id}' requires keybinding arguments.";
        if (schema?.Validator is not { } validator) return null;
        try { return validator(effective); }
        catch (Exception error) { return error.GetBaseException().Message; }
    }

    private static bool HasArguments(JsonElement? args)
        => args is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };

    private void Persist() => _store.Write(SettingsKey, JsonSerializer.Serialize(_bindings, JsonOptions));

    private sealed record EffectiveTarget(string CommandId, KeyGestureSequence Sequence, JsonElement? Arguments);

    private sealed class Binder(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() { _dispose?.Invoke(); _dispose = null; }
    }
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
        rows.AddRange(Keymap.CommandDescriptors.Select(x =>
            (Widget)Text($"{x.Id}: {x.EffectiveGestureText ?? "Unbound"}")));
        rows.AddRange(Issues.Value.Select(x => (Widget)Text($"{x.CommandId}: {x.Message}")));
        return VStack(4)[rows.ToArray()];
    }
}
