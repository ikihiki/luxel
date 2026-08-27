using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.ExceptionServices;
using Luxel.UI;

namespace Luxel.Workbench;

/// <summary>JSON-serializable immutable command invocation.</summary>
public sealed record CommandInvocationContext
{
    private readonly JsonElement? _arguments;

    public CommandInvocationContext(string commandId, JsonElement? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        CommandId = commandId;
        _arguments = arguments is { } value ? value.Clone() : null;
    }

    public string CommandId { get; }
    public JsonElement? Arguments => _arguments;
    public bool HasArguments => Arguments.HasValue;

    public void Deconstruct(out string commandId, out JsonElement? arguments)
        => (commandId, arguments) = (CommandId, Arguments);

    public static CommandInvocationContext FromJson(string commandId, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return new(commandId, document.RootElement);
    }
}

/// <summary>Optional argument contract and palette help. Validator returns an error message or null.</summary>
public sealed record CommandArgumentSchema
{
    private readonly JsonElement? _defaultValue;
    private readonly JsonElement? _schema;

    public CommandArgumentSchema(string? Help = null, bool Required = false,
        JsonElement? DefaultValue = null, Func<JsonElement?, string?>? Validator = null,
        JsonElement? Schema = null)
    {
        this.Help = Help;
        this.Required = Required;
        _defaultValue = DefaultValue is { } defaultValue ? defaultValue.Clone() : null;
        this.Validator = Validator;
        _schema = Schema is { } schema ? schema.Clone() : null;
    }

    public string? Help { get; init; }
    public bool Required { get; init; }
    public JsonElement? DefaultValue => _defaultValue;
    [JsonIgnore] public Func<JsonElement?, string?>? Validator { get; init; }
    public JsonElement? Schema => _schema;
    public bool HasDefaultValue => DefaultValue.HasValue;
    public bool HasSchema => Schema.HasValue;
    public bool IsPaletteExecutable => !Required || HasDefaultValue;
}

/// <summary>Immutable ordered strokes such as Ctrl+K Ctrl+S.</summary>
public sealed class KeyGestureSequence : IEquatable<KeyGestureSequence>
{
    private readonly KeyGesture[] _gestures;
    private readonly IReadOnlyList<KeyGesture> _readOnlyGestures;

    public KeyGestureSequence(IEnumerable<KeyGesture> gestures)
    {
        ArgumentNullException.ThrowIfNull(gestures);
        _gestures = gestures.ToArray();
        if (_gestures.Length == 0) throw new ArgumentException("A keybinding must contain at least one gesture.", nameof(gestures));
        _readOnlyGestures = Array.AsReadOnly(_gestures);
    }

    public IReadOnlyList<KeyGesture> Gestures => _readOnlyGestures;
    public int Count => _gestures.Length;
    public KeyGesture this[int index] => _gestures[index];
    public bool IsSingleStroke => Count == 1;
    public bool Equals(KeyGestureSequence? other) => KeyGestures.SequenceEqual(this, other);
    public override bool Equals(object? obj) => obj is KeyGestureSequence other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (KeyGesture gesture in _gestures) hash.Add(gesture);
        return hash.ToHashCode();
    }
    public override string ToString() => KeyGestures.Format(this);
}

/// <summary>コマンド 1 つ (ADR-0013 の単一の真実)。MenuBar/CommandPalette/Toolbar/Keymap は
/// すべてこの定義のビュー。従来の 5 引数 constructor / Deconstruct / parameterless Run を維持する。</summary>
public sealed record Command(string Id, string Title, Action Run,
                             Func<bool>? Enabled = null, KeyGesture? Gesture = null)
{
    public Command(string Id, string Title, Action Run,
        Action<CommandInvocationContext> Invoke, CommandArgumentSchema? ArgumentSchema = null,
        KeyGestureSequence? GestureSequence = null, Func<bool>? Enabled = null, KeyGesture? Gesture = null)
        : this(Id, Title, Run, Enabled, Gesture)
    {
        this.Invoke = Invoke;
        this.ArgumentSchema = ArgumentSchema;
        this.GestureSequence = GestureSequence;
    }

    public Command(string Id, string Title, Action Run, KeyGestureSequence GestureSequence,
        Func<bool>? Enabled = null, KeyGesture? Gesture = null)
        : this(Id, Title, Run, Enabled, Gesture)
    {
        this.GestureSequence = GestureSequence;
    }

    /// <summary>いま実行できるか (enablement)。評価は表示/実行時。</summary>
    public bool IsEnabled => Enabled?.Invoke() ?? true;
    public Action<CommandInvocationContext>? Invoke { get; init; }
    public CommandArgumentSchema? ArgumentSchema { get; init; }
    public KeyGestureSequence? GestureSequence { get; init; }
    public KeyGestureSequence? DefaultGestureSequence => GestureSequence
        ?? (Gesture is { } gesture ? new KeyGestureSequence([gesture]) : null);
}

/// <summary>アクティブドキュメントからの寄与 1 件 (ADR-0013)。コマンド + メニューパス
/// (null = メニューに出さない) + ツールバー掲載。シェルがアクティブ doc の
/// <see cref="IEditorDocument.Contributions"/> を集めて各サーフェスへ合成する。</summary>
public sealed record CommandContribution(Command Command, string? MenuPath = null,
                                         bool Toolbar = false, int Order = 0);

/// <summary>発見 UI / host bridge が利用する、実行関数を含まないコマンドのスナップショット。</summary>
public sealed record CommandDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required bool Enabled { get; init; }
    public bool IsEnabled => Enabled;
    public KeyGesture? EffectiveGesture { get; init; }
    public KeyGestureSequence? EffectiveGestureSequence { get; init; }
    public string? EffectiveGestureText => EffectiveGestureSequence is { } sequence ? KeyGestures.Format(sequence)
        : EffectiveGesture is { } gesture ? KeyGestures.Format(gesture) : null;
    public IReadOnlyList<string> MenuPaths { get; init; } = [];
    public string? MenuPath => MenuPaths.FirstOrDefault();
    public bool Toolbar { get; init; }
    public CommandArgumentSchema? ArgumentSchema { get; init; }
    public string? ArgumentHelp => ArgumentSchema?.Help;
    public bool RequiresArguments => ArgumentSchema is { Required: true, HasDefaultValue: false };
    public bool PaletteExecutable { get; init; } = true;
}

/// <summary>構造化されたコマンド実行結果。</summary>
public enum CommandExecutionStatus
{
    Executed,
    Succeeded = Executed,
    NotFound,
    Disabled,
    InvalidArguments,
    Failed,
}

public sealed record CommandExecutionResult(string CommandId, CommandExecutionStatus Status,
                                            Exception? Exception = null, string? Error = null,
                                            CommandInvocationContext? Invocation = null)
{
    public bool Executed => Status == CommandExecutionStatus.Executed;
    public bool Succeeded => Executed;
    public bool Success => Executed;
    public string Code => Status switch
    {
        CommandExecutionStatus.Executed => "executed",
        CommandExecutionStatus.NotFound => "not_found",
        CommandExecutionStatus.Disabled => "disabled",
        CommandExecutionStatus.InvalidArguments => "invalid_arguments",
        CommandExecutionStatus.Failed => "failed",
        _ => Status.ToString().ToLowerInvariant(),
    };
    public string? Message => Exception?.Message ?? Error ?? Status switch
    {
        CommandExecutionStatus.NotFound => $"Unknown command id: {CommandId}",
        CommandExecutionStatus.Disabled => $"Command is disabled: {CommandId}",
        CommandExecutionStatus.InvalidArguments => $"Invalid arguments for command: {CommandId}",
        _ => null,
    };
}

/// <summary>メニュー階層のノード (BuildMenu の結果)。Command が null = フォルダ。</summary>
public sealed record MenuNode(string Label, Command? Command, IReadOnlyList<MenuNode> Children);

/// <summary>キーバインド文字列 ⇄ <see cref="KeyGesture"/> ("Ctrl+Shift+P" / "F3" / "Ctrl+1")。</summary>
public static class KeyGestures
{
    /// <summary>解析。不明トークンは null。</summary>
    public static KeyGesture? Parse(string text)
    {
        Key key = Key.None;
        bool ctrl = false, shift = false, alt = false;
        foreach (string token in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default:
                    string name = token.Length == 1 && char.IsAsciiDigit(token[0]) ? $"D{token}" : token;
                    if (!Enum.TryParse(name, ignoreCase: true, out key)) return null;
                    break;
            }
        }
        return key == Key.None ? null : new KeyGesture(key, ctrl, shift, alt);
    }

    /// <summary>空白区切りの ordered chord sequence を解析する。</summary>
    public static KeyGestureSequence? ParseSequence(string text)
    {
        string normalized = text.Replace(" +", "+", StringComparison.Ordinal)
            .Replace("+ ", "+", StringComparison.Ordinal).Trim();
        if (normalized.Length == 0) return null;
        var gestures = new List<KeyGesture>();
        foreach (string stroke in normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Parse(stroke) is not { } gesture) return null;
            gestures.Add(gesture);
        }
        return gestures.Count == 0 ? null : new KeyGestureSequence(gestures);
    }

    public static string Format(KeyGestureSequence sequence)
        => string.Join(' ', sequence.Gestures.Select(Format));

    public static bool SequenceEqual(KeyGestureSequence? left, KeyGestureSequence? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null || left.Count != right.Count) return false;
        for (int i = 0; i < left.Count; i++) if (left[i] != right[i]) return false;
        return true;
    }

    public static bool StartsWith(KeyGestureSequence sequence, IReadOnlyList<KeyGesture> prefix)
    {
        if (prefix.Count > sequence.Count) return false;
        for (int i = 0; i < prefix.Count; i++) if (sequence[i] != prefix[i]) return false;
        return true;
    }

    /// <summary>表示用文字列 ("Ctrl+Shift+P")。</summary>
    public static string Format(KeyGesture g)
    {
        string key = g.Key is >= Key.D0 and <= Key.D9 ? ((int)(g.Key - Key.D0)).ToString() : g.Key.ToString();
        return $"{(g.Ctrl ? "Ctrl+" : "")}{(g.Shift ? "Shift+" : "")}{(g.Alt ? "Alt+" : "")}{key}";
    }
}

/// <summary>
/// コマンドの単一の真実 (ADR-0013)。コマンド { id, タイトル, キーバインド, enablement, run } を
/// 登録し、メニュー項目は**パス文字列** ("File/保存") + コマンド id の寄与で足す (Unity 流)。
/// MenuBar / CommandPalette / Toolbar / Keymap はここから生成される純粋ビュー —
/// アクティブ doc の <see cref="CommandContribution"/> は各ビューの生成時に合成する (Unreal 流)。
/// 変更は <see cref="Version"/> が進む (UI の TrackBuild 再構築フック)。
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, Command> _commands = new();
    private readonly Dictionary<string, KeyGestureSequence?> _gestureOverrides = new(StringComparer.Ordinal);
    private readonly List<(string Path, string CommandId, int Order, int Seq)> _menu = new();
    private readonly List<(string CommandId, int Order, int Seq)> _toolbar = new();
    private int _seq;

    /// <summary>登録の世代 (UI が Build で読むと登録変更で自動再構築)。</summary>
    public Signal<int> Version { get; } = new(0);

    /// <summary>コマンドを登録する (同 id は上書き)。menuPath / toolbar で同時に掲載できる。</summary>
    public void Register(Command command, string? menuPath = null, int order = 0, bool toolbar = false)
    {
        _commands[command.Id] = command;
        if (menuPath is not null) _menu.Add((menuPath, command.Id, order, _seq++));
        if (toolbar) _toolbar.Add((command.Id, order, _seq++));
        Version.Value++;
    }

    /// <summary>省略形: キーは "Ctrl+S" 形式の文字列で。</summary>
    public Command Register(string id, string title, Action run, Func<bool>? enabled = null,
                            string? key = null, string? menuPath = null, int order = 0, bool toolbar = false)
    {
        KeyGestureSequence? sequence = key is null ? null : KeyGestures.ParseSequence(key);
        var command = new Command(id, title, run, enabled,
            sequence is { IsSingleStroke: true } ? sequence[0] : null)
        {
            GestureSequence = sequence,
        };
        Register(command, menuPath, order, toolbar);
        return command;
    }

    /// <summary>Argument-aware registration. Existing parameterless overload remains source-compatible.</summary>
    public Command Register(string id, string title, Action<CommandInvocationContext> run,
                            CommandArgumentSchema? arguments = null, Func<bool>? enabled = null,
                            string? key = null, string? menuPath = null, int order = 0, bool toolbar = false)
    {
        ArgumentNullException.ThrowIfNull(run);
        KeyGestureSequence? sequence = key is null ? null : KeyGestures.ParseSequence(key);
        var command = new Command(id, title, static () => { }, enabled,
            sequence is { IsSingleStroke: true } ? sequence[0] : null)
        {
            Invoke = run,
            ArgumentSchema = arguments,
            GestureSequence = sequence,
        };
        Register(command, menuPath, order, toolbar);
        return command;
    }

    public Command? Find(string id) => _commands.GetValueOrDefault(id);

    /// <summary>keymap override を考慮した実効 single gesture。Chord の場合は null。</summary>
    public KeyGesture? EffectiveGesture(string id)
        => EffectiveGestureSequence(id) is { IsSingleStroke: true } sequence ? sequence[0] : null;

    public KeyGestureSequence? EffectiveGestureSequence(string id)
        => _gestureOverrides.TryGetValue(id, out KeyGestureSequence? sequence)
            ? sequence : Find(id)?.DefaultGestureSequence;

    /// <summary>コマンドの gesture を差し替える。null は binding を無効化する。</summary>
    public void SetGestureOverride(string id, KeyGesture? gesture)
    {
        if (!_commands.ContainsKey(id)) throw new KeyNotFoundException($"Unknown command id: {id}");
        DefineGestureSequenceOverride(id, gesture is { } value ? new KeyGestureSequence([value]) : null);
    }

    public void SetGestureSequenceOverride(string id, KeyGestureSequence? sequence)
    {
        if (!_commands.ContainsKey(id)) throw new KeyNotFoundException($"Unknown command id: {id}");
        DefineGestureSequenceOverride(id, sequence);
    }

    /// <summary>未登録 id も含めて override を定義する。extension command が後から登録された場合にも有効。</summary>
    public void DefineGestureOverride(string id, KeyGesture? gesture)
        => DefineGestureSequenceOverride(id, gesture is { } value ? new KeyGestureSequence([value]) : null);

    public void DefineGestureSequenceOverride(string id, KeyGestureSequence? sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_gestureOverrides.TryGetValue(id, out KeyGestureSequence? current)
            && KeyGestures.SequenceEqual(current, sequence)) return;
        _gestureOverrides[id] = sequence;
        Version.Value++;
    }

    public bool HasGestureOverride(string id) => _gestureOverrides.ContainsKey(id);

    /// <summary>registry command は override 込み、寄与 command は宣言 gesture を返す。</summary>
    public KeyGesture? GestureFor(Command command)
        => GestureSequenceFor(command) is { IsSingleStroke: true } sequence ? sequence[0] : null;

    public KeyGestureSequence? GestureSequenceFor(Command command)
        => _commands.ContainsKey(command.Id) ? EffectiveGestureSequence(command.Id) : command.DefaultGestureSequence;

    public void ResetGestureOverride(string id)
    {
        if (_gestureOverrides.Remove(id)) Version.Value++;
    }

    public void ResetGestureOverrides()
    {
        if (_gestureOverrides.Count == 0) return;
        _gestureOverrides.Clear();
        Version.Value++;
    }

    /// <summary>全コマンド (パレット用は <see cref="PaletteCommands"/>)。</summary>
    public IReadOnlyCollection<Command> Commands => _commands.Values;

    /// <summary>登録分 + 寄与を、現在の enablement / 実効 keybinding / surface metadata 付きで列挙する。</summary>
    public IReadOnlyList<CommandDescriptor> Descriptors(IReadOnlyList<CommandContribution>? extra = null)
    {
        _ = Version.Value;
        var descriptors = new List<CommandDescriptor>();
        foreach (Command command in _commands.Values)
        {
            string[] menuPaths = _menu.Where(x => x.CommandId == command.Id).Select(x => x.Path)
                .Distinct(StringComparer.Ordinal).ToArray();
            descriptors.Add(new CommandDescriptor
            {
                Id = command.Id,
                Title = command.Title,
                Enabled = command.IsEnabled,
                EffectiveGesture = EffectiveGesture(command.Id),
                EffectiveGestureSequence = EffectiveGestureSequence(command.Id),
                MenuPaths = menuPaths,
                Toolbar = _toolbar.Any(x => x.CommandId == command.Id),
                ArgumentSchema = command.ArgumentSchema,
                PaletteExecutable = CanExecuteWithoutArguments(command),
            });
        }
        if (extra is not null)
            foreach (CommandContribution contribution in extra)
            {
                if (descriptors.Any(x => x.Id == contribution.Command.Id)) continue;
                descriptors.Add(new CommandDescriptor
                {
                    Id = contribution.Command.Id,
                    Title = contribution.Command.Title,
                    Enabled = contribution.Command.IsEnabled,
                    EffectiveGesture = contribution.Command.Gesture,
                    EffectiveGestureSequence = contribution.Command.DefaultGestureSequence,
                    MenuPaths = contribution.MenuPath is null ? [] : [contribution.MenuPath],
                    Toolbar = contribution.Toolbar,
                    ArgumentSchema = contribution.Command.ArgumentSchema,
                    PaletteExecutable = CanExecuteWithoutArguments(contribution.Command),
                });
            }
        return descriptors;
    }

    public CommandDescriptor? Describe(string id, IReadOnlyList<CommandContribution>? extra = null)
        => Descriptors(extra).FirstOrDefault(x => x.Id == id);

    private static bool CanExecuteWithoutArguments(Command command)
    {
        CommandArgumentSchema? schema = command.ArgumentSchema;
        JsonElement? arguments = schema?.DefaultValue is { } defaultValue ? defaultValue.Clone() : null;
        if (arguments.HasValue && command.Invoke is null) return false;
        if (!arguments.HasValue && schema?.Required == true) return false;
        if (schema?.Validator is not { } validate) return true;
        try { return string.IsNullOrWhiteSpace(validate(arguments)); }
        catch { return false; }
    }

    /// <summary>id のコマンドを実行し、未登録/disabled/引数不正/例外を構造化して返す。</summary>
    public CommandExecutionResult Execute(string id) => Execute(new CommandInvocationContext(id));

    /// <summary>registry command または active contribution を id で実行する。</summary>
    public CommandExecutionResult Execute(string id, IReadOnlyList<CommandContribution>? extra)
        => Execute(new CommandInvocationContext(id), extra);

    public CommandExecutionResult Execute(string id, JsonElement arguments,
        IReadOnlyList<CommandContribution>? extra = null)
        => Execute(new CommandInvocationContext(id, arguments.Clone()), extra);

    public CommandExecutionResult Execute(CommandInvocationContext invocation,
        IReadOnlyList<CommandContribution>? extra = null)
    {
        if (Find(invocation.CommandId) is { } command) return ExecuteCommand(command, invocation);
        Command? contribution = extra?.FirstOrDefault(x => x.Command.Id == invocation.CommandId)?.Command;
        return contribution is not null
            ? ExecuteCommand(contribution, invocation)
            : new(invocation.CommandId, CommandExecutionStatus.NotFound, Invocation: invocation);
    }

    /// <summary>id のコマンドを実行する (enabled のときだけ)。実行したら true。従来どおり実行例外は送出する。</summary>
    public bool Run(string id) => ThrowOnFailure(Execute(id));

    /// <summary>gesture を寄与優先 → 登録順で構造化実行する。</summary>
    public CommandExecutionResult ExecuteGesture(KeyGesture gesture,
        IReadOnlyList<CommandContribution>? extra = null)
    {
        Command? disabled = null;
        if (extra is not null)
            foreach (CommandContribution contribution in extra)
            {
                Command command = contribution.Command;
                if (command.DefaultGestureSequence is not { IsSingleStroke: true } sequence || sequence[0] != gesture) continue;
                if (command.IsEnabled) return ExecuteCommand(command, new(command.Id));
                disabled ??= command;
            }
        foreach (Command command in _commands.Values)
        {
            if (EffectiveGesture(command.Id) != gesture) continue;
            if (command.IsEnabled) return ExecuteCommand(command, new(command.Id));
            disabled ??= command;
        }
        return disabled is null
            ? new("", CommandExecutionStatus.NotFound)
            : new(disabled.Id, CommandExecutionStatus.Disabled);
    }

    /// <summary>キー入力をコマンドへ配送する (寄与優先 → 登録順)。実行したら true。
    /// UiHost へ常時結線するなら <see cref="BindShortcuts(UiHost)"/>。</summary>
    public bool HandleKey(Key keyValue, KeyModifiers mods, IReadOnlyList<CommandContribution>? extra = null)
    {
        var gesture = new KeyGesture(keyValue, mods.HasFlag(KeyModifiers.Ctrl), mods.HasFlag(KeyModifiers.Shift), mods.HasFlag(KeyModifiers.Alt));
        return ThrowOnFailure(ExecuteGesture(gesture, extra));
    }

    /// <summary>登録済みキーバインドを UiHost の全域ショートカットへ結線する (登録変更に追従)。</summary>
    public IDisposable BindShortcuts(UiHost host) => BindShortcuts(host, static () => []);

    /// <summary>登録分と現在の active contribution を UiHost へ結線する。registry 変更と、
    /// contributions delegate が読む signal の変更に追従する。</summary>
    public IDisposable BindShortcuts(UiHost host, Func<IReadOnlyList<CommandContribution>> contributions)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(contributions);
        var bound = new List<KeyGesture>();
        void Rebind(IReadOnlyList<CommandContribution> current)
        {
            foreach (KeyGesture gesture in bound) host.UnregisterShortcut(gesture);
            bound.Clear();
            IEnumerable<KeyGesture> gestures = current.Select(x => x.Command.Gesture).OfType<KeyGesture>()
                .Concat(_commands.Values.Select(x => EffectiveGesture(x.Id)).OfType<KeyGesture>())
                .Distinct();
            foreach (KeyGesture gesture in gestures)
            {
                KeyGesture captured = gesture;
                host.RegisterShortcut(captured, () => ThrowOnFailure(ExecuteGesture(captured, contributions())));
                bound.Add(captured);
            }
        }
        IDisposable effect = Reactive.Effect(() =>
        {
            _ = Version.Value;
            Rebind(contributions());
        });
        return new Binder(() =>
        {
            effect.Dispose();
            foreach (KeyGesture gesture in bound) host.UnregisterShortcut(gesture);
        });
    }

    private static CommandExecutionResult ExecuteCommand(Command command, CommandInvocationContext requested)
    {
        if (!command.IsEnabled)
            return new(command.Id, CommandExecutionStatus.Disabled, Invocation: requested);

        JsonElement? arguments = requested.Arguments is { } supplied ? supplied.Clone() : null;
        CommandArgumentSchema? schema = command.ArgumentSchema;
        if (arguments.HasValue && command.Invoke is null)
            return new(command.Id, CommandExecutionStatus.InvalidArguments,
                Error: $"Command '{command.Id}' does not accept arguments.", Invocation: requested);
        if (!arguments.HasValue && schema?.DefaultValue is { } defaultValue) arguments = defaultValue.Clone();
        var invocation = new CommandInvocationContext(command.Id, arguments);
        if (!arguments.HasValue && schema?.Required == true)
            return new(command.Id, CommandExecutionStatus.InvalidArguments,
                Error: schema.Help ?? "Arguments are required.", Invocation: invocation);
        if (schema?.Validator is { } validate)
        {
            string? error;
            try { error = validate(arguments); }
            catch (Exception exception)
            {
                return new(command.Id, CommandExecutionStatus.Failed, exception, Invocation: invocation);
            }
            if (!string.IsNullOrWhiteSpace(error))
                return new(command.Id, CommandExecutionStatus.InvalidArguments, Error: error, Invocation: invocation);
        }

        try
        {
            if (command.Invoke is { } invoke) invoke(invocation);
            else command.Run();
            return new(command.Id, CommandExecutionStatus.Executed, Invocation: invocation);
        }
        catch (Exception exception)
        {
            return new(command.Id, CommandExecutionStatus.Failed, exception, Invocation: invocation);
        }
    }

    private static bool ThrowOnFailure(CommandExecutionResult result)
    {
        if (result.Exception is { } exception) ExceptionDispatchInfo.Capture(exception).Throw();
        return result.Executed;
    }

    private sealed class Binder(Action dispose) : IDisposable
    {
        private Action? _d = dispose;
        public void Dispose() { _d?.Invoke(); _d = null; }
    }

    /// <summary>メニュー階層を組む (登録分 + 寄与、パスの各セグメントがフォルダ)。
    /// 並びは (Order, 登録順)。同一パス末端は後勝ち。</summary>
    public IReadOnlyList<MenuNode> BuildMenu(IReadOnlyList<CommandContribution>? extra = null)
    {
        var entries = new List<(string Path, Command Cmd, int Order, int Seq)>();
        foreach ((string path, string id, int order, int seq) in _menu)
            if (Find(id) is { } c) entries.Add((path, c, order, seq));
        if (extra is not null)
        {
            int seq = _seq;
            foreach (CommandContribution c in extra)
                if (c.MenuPath is not null) entries.Add((c.MenuPath, c.Command, c.Order, seq++));
        }

        var root = new Builder("");
        foreach ((string path, Command cmd, int order, int seq) in entries)
        {
            string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            Builder cur = root;
            for (int i = 0; i < parts.Length - 1; i++) cur = cur.Child(parts[i], order, seq);
            Builder leaf = cur.Child(parts[^1], order, seq);
            leaf.Command = cmd;
        }
        return root.ToNodes();
    }

    private sealed class Builder(string label)
    {
        public readonly string Label = label;
        public Command? Command;
        public int Order = int.MaxValue;
        public int Seq = int.MaxValue;
        private readonly List<Builder> _kids = new();

        public Builder Child(string name, int order, int seq)
        {
            Builder? b = _kids.FirstOrDefault(k => k.Label == name);
            if (b is null) _kids.Add(b = new Builder(name));
            b.Order = Math.Min(b.Order, order);
            b.Seq = Math.Min(b.Seq, seq);
            return b;
        }

        public IReadOnlyList<MenuNode> ToNodes()
            => _kids.OrderBy(k => k.Order).ThenBy(k => k.Seq)
                    .Select(k => new MenuNode(k.Label, k.Command, k.ToNodes())).ToArray();
    }

    /// <summary>ツールバー掲載コマンド (登録分 + 寄与、(Order, 登録順))。</summary>
    public IReadOnlyList<Command> ToolbarCommands(IReadOnlyList<CommandContribution>? extra = null)
    {
        var items = new List<(Command Cmd, int Order, int Seq)>();
        foreach ((string id, int order, int seq) in _toolbar)
            if (Find(id) is { } c) items.Add((c, order, seq));
        if (extra is not null)
        {
            int seq = _seq;
            foreach (CommandContribution c in extra)
                if (c.Toolbar) items.Add((c.Command, c.Order, seq++));
        }
        return items.OrderBy(x => x.Order).ThenBy(x => x.Seq).Select(x => x.Cmd).ToArray();
    }

    /// <summary>パレット用 descriptor (実効 binding を含み、タイトル順)。</summary>
    public IReadOnlyList<CommandDescriptor> PaletteDescriptors(IReadOnlyList<CommandContribution>? extra = null)
        => Descriptors(extra).OrderBy(x => x.Title, StringComparer.Ordinal).ToArray();

    /// <summary>パレットに出す全コマンド (登録分 + 寄与、タイトル順)。</summary>
    public IReadOnlyList<Command> PaletteCommands(IReadOnlyList<CommandContribution>? extra = null)
    {
        IEnumerable<Command> all = _commands.Values;
        if (extra is not null) all = all.Concat(extra.Select(c => c.Command)).DistinctBy(c => c.Id);
        return all.OrderBy(c => c.Title, StringComparer.Ordinal).ToArray();
    }
}
