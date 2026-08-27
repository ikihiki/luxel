using System.Text.Json;
using System.Text.Json.Serialization;
using Luxel.Controls;
using Luxel.Workbench;

namespace Luxel.Editor.Browser;

public static class BrowserApiContract
{
    public const int Version = 1;
    public const int MacroVersion = 2;
    public const int LegacyMacroVersion = 1;
    public const int MaximumRequestLength = 1_048_576;

    public const string CommandsList = "commands.list";
    public const string CommandsRun = "commands.run";
    public const string KeybindingsGet = "keybindings.get";
    public const string KeybindingsUpdate = "keybindings.update";
    public const string KeybindingsReset = "keybindings.reset";
    public const string MacrosRun = "macros.run";
    public const string Snapshot = "snapshot";
    public const string LifecycleGet = "lifecycle.get";
    public const string Dispose = "dispose";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BrowserMacroStep
{
    private JsonElement _args;
    private bool _hasArgs;

    public required string CommandId { get; init; }
    public JsonElement Args
    {
        get => _args;
        init
        {
            _args = value.Clone();
            _hasArgs = true;
        }
    }
    [JsonIgnore] public bool HasArgs => _hasArgs;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BrowserMacro
{
    private IReadOnlyList<BrowserMacroStep> _steps = [];
    private bool _hasSteps;

    public int Version { get; init; }
    public IReadOnlyList<BrowserMacroStep> Steps
    {
        get => _steps;
        init
        {
            _steps = value;
            _hasSteps = true;
        }
    }
    public bool StopOnError { get; init; } = true;
    [JsonIgnore] public bool HasSteps => _hasSteps;
}

public sealed record BrowserCommandError(string Code, string Message);
public sealed record BrowserMacroStepResult(int Index, string CommandId, bool Ok, BrowserCommandError? Error = null);
public sealed record BrowserMacroRunResult(
    int Version,
    bool Succeeded,
    bool Stopped,
    int ExecutedCount,
    IReadOnlyList<BrowserMacroStepResult> Steps);

/// <summary>Executes a data-only ordered list of command IDs and optional JSON arguments.</summary>
public sealed class BrowserMacroExecutor
{
    private readonly CommandRegistry _commands;
    private readonly Func<IReadOnlyList<CommandContribution>?> _contributions;

    public BrowserMacroExecutor(CommandRegistry commands)
        : this(commands, static () => null)
    {
    }

    public BrowserMacroExecutor(EditorSession session)
        : this(session.Commands, () => session.ActiveCommandContributions)
    {
    }

    private BrowserMacroExecutor(CommandRegistry commands,
        Func<IReadOnlyList<CommandContribution>?> contributions)
    {
        _commands = commands;
        _contributions = contributions;
    }

    public BrowserMacroRunResult Run(BrowserMacro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        if (macro.Version is not BrowserApiContract.LegacyMacroVersion and not BrowserApiContract.MacroVersion)
            throw new BrowserApiException("unsupported_macro_version",
                $"Unsupported macro version {macro.Version}; expected {BrowserApiContract.LegacyMacroVersion} or {BrowserApiContract.MacroVersion}.");
        if (!macro.HasSteps || macro.Steps is null)
            throw new BrowserApiException("invalid_macro", "Macro steps must be present and must be an array.");

        var results = new List<BrowserMacroStepResult>(macro.Steps.Count);
        bool stopped = false;
        for (int index = 0; index < macro.Steps.Count; index++)
        {
            BrowserMacroStep? step = macro.Steps[index];
            BrowserCommandError? error = step is null
                ? new("invalid_step", "Macro step must be an object.")
                : macro.Version == BrowserApiContract.LegacyMacroVersion && step.HasArgs
                    ? new("invalid_arguments", "Macro version 1 steps do not support args; use macro version 2.")
                    : Execute(step.CommandId, step.Args);
            string commandId = step?.CommandId ?? "";
            results.Add(new(index, commandId, error is null, error));
            if (error is not null && macro.StopOnError)
            {
                stopped = index + 1 < macro.Steps.Count;
                break;
            }
        }

        return new(macro.Version, results.All(x => x.Ok), stopped, results.Count, results);
    }

    private BrowserCommandError? Execute(string commandId, JsonElement args)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            return new("invalid_command_id", "Command ID must not be empty.");
        IReadOnlyList<CommandContribution>? contributions = _contributions();
        CommandExecutionResult result = HasArguments(args)
            ? _commands.Execute(commandId, args, contributions)
            : _commands.Execute(commandId, contributions);
        return BrowserCommandResults.Error(commandId, result);
    }

    private static bool HasArguments(JsonElement args)
        => args.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;
}

internal static class BrowserCommandResults
{
    public static BrowserCommandError? Error(string commandId, CommandExecutionResult result)
        => result.Status switch
        {
            CommandExecutionStatus.Executed => null,
            CommandExecutionStatus.NotFound => new("unknown_command", $"Unknown command ID '{commandId}'."),
            CommandExecutionStatus.Disabled => new("command_disabled", $"Command '{commandId}' is disabled."),
            CommandExecutionStatus.InvalidArguments => new("invalid_arguments",
                result.Error ?? $"Invalid arguments for command '{commandId}'."),
            CommandExecutionStatus.Failed => new("command_failed",
                result.Exception?.GetBaseException().Message ?? "Command failed."),
            _ => new("command_failed", result.Message ?? "Command failed.")
        };
}

public sealed class BrowserApiException(string code, string message, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}

/// <summary>Structured JSON browser/DevTools API. Calls are serialized to preserve editor mutation order.</summary>
public sealed class BrowserApiBackend(
    Func<EditorApplication?> application,
    Func<string>? snapshot = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    private readonly SemaphoreSlim _serial = new(1, 1);
    private bool _disposeRequested;

    public async Task<string> InvokeAsync(string requestJson)
    {
        if (requestJson is null)
            return Failure("", new BrowserApiException("invalid_json", "Request JSON must not be null."));
        if (requestJson.Length > BrowserApiContract.MaximumRequestLength)
            return Failure("", new BrowserApiException("invalid_json", "Request JSON exceeds the maximum supported length."));
        await _serial.WaitAsync();
        try
        {
            BrowserApiRequest request;
            try
            {
                request = JsonSerializer.Deserialize<BrowserApiRequest>(requestJson, Json)
                    ?? throw new BrowserApiException("invalid_request", "Request must be a JSON object.");
            }
            catch (BrowserApiException error)
            {
                return Failure("", error);
            }
            catch (ArgumentException error)
            {
                return Failure("", new BrowserApiException("invalid_json", error.Message));
            }
            catch (JsonException error)
            {
                return Failure("", new BrowserApiException("invalid_json", error.Message));
            }

            if (request.Version != BrowserApiContract.Version)
                return Failure(request.Operation ?? "", new BrowserApiException("unsupported_version",
                    $"Unsupported browser API version {request.Version}; expected {BrowserApiContract.Version}."));
            if (string.IsNullOrWhiteSpace(request.Operation))
                return Failure("", new BrowserApiException("invalid_operation", "Operation must not be empty."));

            try
            {
                object result = await DispatchAsync(request);
                return Success(request.Operation, result);
            }
            catch (BrowserApiException error)
            {
                return Failure(request.Operation, error);
            }
            catch (Exception error)
            {
                Exception reason = error.GetBaseException();
                return Failure(request.Operation, new BrowserApiException("operation_failed", reason.Message));
            }
        }
        finally
        {
            _serial.Release();
        }
    }

    private Task<object> DispatchAsync(BrowserApiRequest request)
    {
        EditorApplication? app = application();
        return request.Operation switch
        {
            BrowserApiContract.CommandsList => Result(CommandsList(RequireSession(app))),
            BrowserApiContract.CommandsRun => Result(CommandsRun(RequireSession(app), Arguments<CommandRunArguments>(request))),
            BrowserApiContract.KeybindingsGet => Result(KeybindingsGet(RequireSession(app))),
            BrowserApiContract.KeybindingsUpdate => Result(KeybindingsUpdate(RequireSession(app), Arguments<KeybindingsUpdateArguments>(request))),
            BrowserApiContract.KeybindingsReset => Result(KeybindingsReset(RequireSession(app), Arguments<KeybindingsResetArguments>(request))),
            BrowserApiContract.MacrosRun => Result(MacrosRun(RequireSession(app), Arguments<MacrosRunArguments>(request))),
            BrowserApiContract.Snapshot => Result(ReadSnapshot()),
            BrowserApiContract.LifecycleGet => Result(Lifecycle(app)),
            BrowserApiContract.Dispose => Result(Dispose(app)),
            _ => throw new BrowserApiException("unknown_operation", $"Unknown browser API operation '{request.Operation}'.")
        };
    }

    private static Task<object> Result(object value) => Task.FromResult(value);

    private static EditorSession RequireSession(EditorApplication? app)
        => app?.Session ?? throw new BrowserApiException("editor_not_ready", "Editor session is not ready.");

    private object CommandsList(EditorSession session)
        => new
        {
            commands = session.CommandDescriptors
                .OrderBy(command => command.Id, StringComparer.Ordinal)
                .Select(command => new
                {
                    id = command.Id,
                    title = command.Title,
                    enabled = command.Enabled,
                    key = command.EffectiveGestureText,
                    menuPaths = command.MenuPaths,
                    toolbar = command.Toolbar,
                    arguments = command.ArgumentSchema is { } schema ? new
                    {
                        required = schema.Required,
                        help = schema.Help,
                        schema = schema.Schema,
                        hasDefaultValue = schema.HasDefaultValue,
                        defaultValue = schema.DefaultValue,
                        paletteExecutable = schema.IsPaletteExecutable
                    } : null
                }).ToArray()
        };

    private object CommandsRun(EditorSession session, CommandRunArguments arguments)
    {
        string commandId = RequiredCommandId(arguments.CommandId);
        CommandExecutionResult result = HasArguments(arguments.Args)
            ? session.ExecuteCommand(commandId, arguments.Args)
            : session.ExecuteCommand(commandId);
        BrowserCommandError? error = BrowserCommandResults.Error(commandId, result);
        if (error is null) return new { commandId, executed = true };
        throw new BrowserApiException(error.Code, error.Message);
    }

    private static object KeybindingsGet(EditorSession session)
    {
        IReadOnlyList<EditorKeyBinding> bindings = session.Keymap.Get();
        return new
        {
            json = JsonSerializer.Serialize(bindings, Json),
            bindings,
            commands = session.Commands.Descriptors()
                .OrderBy(command => command.Id, StringComparer.Ordinal)
                .Select(command => new
                {
                    command = command.Id,
                    key = command.EffectiveGestureText,
                    defaultKey = session.Commands.Find(command.Id)?.DefaultGestureSequence is { } original
                        ? KeyGestures.Format(original) : null,
                    isOverride = session.Commands.HasGestureOverride(command.Id),
                    args = session.Keymap.EffectiveArguments(command.Id)
                }).ToArray()
        };
    }

    private static object KeybindingsUpdate(EditorSession session, KeybindingsUpdateArguments arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments.Json))
            throw new BrowserApiException("invalid_keybindings", "Keybindings JSON must not be empty.");

        BrowserKeybindingEntry[] entries;
        try
        {
            entries = JsonSerializer.Deserialize<BrowserKeybindingEntry[]>(arguments.Json, Json)
                ?? throw new BrowserApiException("invalid_keybindings", "Keybindings JSON must be an array.");
        }
        catch (BrowserApiException)
        {
            throw;
        }
        catch (JsonException error)
        {
            throw new BrowserApiException("invalid_keybindings_json", error.Message);
        }

        var bindings = new List<EditorKeyBinding>(entries.Length);
        var usedCommands = new HashSet<string>(StringComparer.Ordinal);
        foreach (BrowserKeybindingEntry? entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Command))
                throw new BrowserApiException("invalid_keybinding", "Each keybinding entry requires a command.");
            bool remove = entry.Command.StartsWith("-", StringComparison.Ordinal);
            string commandId = RequiredCommandId(remove ? entry.Command[1..] : entry.Command);
            if (!usedCommands.Add(commandId))
                throw new BrowserApiException("duplicate_keybinding", $"Command '{commandId}' occurs more than once.");
            Command command = session.Commands.Find(commandId)
                ?? throw new BrowserApiException("unknown_command", $"Unknown command ID '{commandId}'.");
            if (!remove && ValidateKeybindingArguments(command, entry.Args) is { } argumentError)
                throw new BrowserApiException("invalid_arguments", argumentError);
            if (!remove && string.IsNullOrWhiteSpace(entry.Key))
                throw new BrowserApiException("invalid_keybinding", $"Keybinding for '{commandId}' requires a key or chord.");
            if (remove && entry.HasArgs)
                throw new BrowserApiException("invalid_keybinding", $"Removal entry for '{commandId}' cannot contain args.");
            bindings.Add(new(entry.Command, entry.Key, entry.Args?.Clone()));
        }

        IReadOnlyList<EditorKeymapIssue> issues = session.Keymap.Update(bindings);
        if (issues.Count > 0)
            throw new BrowserApiException("keybinding_validation_failed", "Keybindings were not updated.",
                issues.Select(issue => new { command = issue.CommandId, message = issue.Message }).ToArray());
        return KeybindingsGet(session);
    }

    private static object KeybindingsReset(EditorSession session, KeybindingsResetArguments arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments.CommandId))
        {
            session.Keymap.ResetAll();
            return KeybindingsGet(session);
        }
        if (session.Commands.Find(arguments.CommandId) is null)
            throw new BrowserApiException("unknown_command", $"Unknown command ID '{arguments.CommandId}'.");
        session.Keymap.Reset(arguments.CommandId);
        return KeybindingsGet(session);
    }

    private object MacrosRun(EditorSession session, MacrosRunArguments arguments)
    {
        if (arguments.Macro is null)
            throw new BrowserApiException("invalid_macro", "macros.run requires a macro object.");
        return new BrowserMacroExecutor(session).Run(arguments.Macro);
    }

    private object ReadSnapshot()
    {
        string value = snapshot?.Invoke() ?? "{}";
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw new BrowserApiException("invalid_snapshot", error.Message);
        }
    }

    /// <summary>
    /// Performs a real browser-runtime shutdown. Unsaved changes are never discarded: callers must save or
    /// explicitly resolve them before retrying. Closing the clean session first bypasses interactive exit confirmation.
    /// </summary>
    private object Dispose(EditorApplication? app)
    {
        if (app?.Session?.Workspace.AnyDirty.Value == true)
            throw new BrowserApiException("unsaved_changes",
                "Editor has unsaved changes; save or close them before disposing the browser runtime.");
        _disposeRequested = true;
        app?.CloseProjectDiscardingChanges();
        app?.RequestExit();
        return Lifecycle(app);
    }

    private object Lifecycle(EditorApplication? app)
        => new
        {
            state = app is null ? "notStarted" : app.ExitRequested ? "disposed" : _disposeRequested ? "disposeRequested" : "running",
            ready = app?.Session is not null,
            projectId = app?.ProjectId ?? "",
            exitRequested = app?.ExitRequested == true,
            disposeRequested = _disposeRequested
        };

    private static string? ValidateKeybindingArguments(Command command, JsonElement? args)
    {
        bool hasArguments = args is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };
        CommandArgumentSchema? schema = command.ArgumentSchema;
        if (hasArguments && command.Invoke is null)
            return $"Command '{command.Id}' does not accept arguments.";
        JsonElement? effective = hasArguments ? args?.Clone()
            : schema?.DefaultValue is { } defaultValue ? defaultValue.Clone() : null;
        if (!effective.HasValue && schema is { Required: true })
            return $"Command '{command.Id}' requires keybinding args.";
        if (schema?.Validator is not { } validator) return null;
        try { return validator(effective); }
        catch (Exception error) { return error.GetBaseException().Message; }
    }

    private static bool HasArguments(JsonElement args)
        => args.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;

    private static string RequiredCommandId(string? commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            throw new BrowserApiException("invalid_command_id", "Command ID must not be empty.");
        return commandId;
    }

    private static T Arguments<T>(BrowserApiRequest request) where T : new()
    {
        if (request.Arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return new T();
        if (request.Arguments.ValueKind != JsonValueKind.Object)
            throw new BrowserApiException("invalid_arguments", "Operation arguments must be a JSON object.");
        try
        {
            return request.Arguments.Deserialize<T>(Json) ?? new T();
        }
        catch (JsonException error)
        {
            throw new BrowserApiException("invalid_arguments", error.Message);
        }
    }

    private static string Success(string operation, object result)
        => JsonSerializer.Serialize(new { version = BrowserApiContract.Version, operation, ok = true, result }, Json);

    private static string Failure(string operation, BrowserApiException error)
        => JsonSerializer.Serialize(new
        {
            version = BrowserApiContract.Version,
            operation,
            ok = false,
            error = new { code = error.Code, message = error.Message, details = error.Details }
        }, Json);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record BrowserApiRequest
    {
        public int Version { get; init; }
        public string? Operation { get; init; }
        public JsonElement Arguments { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record CommandRunArguments
    {
        public JsonElement Args { get; init; }
        public string? CommandId { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record KeybindingsUpdateArguments
    {
        public string? Json { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record KeybindingsResetArguments
    {
        public string? CommandId { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record MacrosRunArguments
    {
        public BrowserMacro? Macro { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record BrowserKeybindingEntry
    {
        private JsonElement? _args;
        private bool _hasArgs;

        public string? Key { get; init; }
        public string? Command { get; init; }
        public JsonElement? Args
        {
            get => _args;
            init
            {
                _args = value is { } element ? element.Clone() : null;
                _hasArgs = true;
            }
        }
        [JsonIgnore] public bool HasArgs => _hasArgs;
    }
}
