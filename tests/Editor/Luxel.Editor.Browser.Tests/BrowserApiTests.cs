using System.Text.Json;
using Luxel.Controls;
using Luxel.Editor.Browser;
using Luxel.Workbench;

namespace Luxel.Editor.Browser.Tests;

public sealed class BrowserApiTests
{
    [Fact]
    public void MacroExecutesInOrderAndStopsOnDisabledCommandByDefault()
    {
        var registry = new CommandRegistry();
        var order = new List<string>();
        registry.Register("first", "First", () => order.Add("first"));
        registry.Register("disabled", "Disabled", () => order.Add("disabled"), enabled: () => false);
        registry.Register("last", "Last", () => order.Add("last"));

        BrowserMacroRunResult result = new BrowserMacroExecutor(registry).Run(new BrowserMacro
        {
            Version = BrowserApiContract.Version,
            Steps = [new() { CommandId = "first" }, new() { CommandId = "disabled" }, new() { CommandId = "last" }]
        });

        Assert.Equal(["first"], order);
        Assert.False(result.Succeeded);
        Assert.True(result.Stopped);
        Assert.Equal(2, result.ExecutedCount);
        Assert.Collection(result.Steps,
            first => { Assert.Equal(0, first.Index); Assert.True(first.Ok); },
            disabled =>
            {
                Assert.Equal(1, disabled.Index);
                Assert.False(disabled.Ok);
                Assert.Equal("command_disabled", disabled.Error?.Code);
            });
    }

    [Fact]
    public void MacroCanContinueAfterUnknownAndFailedCommandsWithStableIndexes()
    {
        var registry = new CommandRegistry();
        var order = new List<string>();
        registry.Register("throws", "Throws", () => throw new InvalidOperationException("boom"));
        registry.Register("last", "Last", () => order.Add("last"));

        BrowserMacroRunResult result = new BrowserMacroExecutor(registry).Run(new BrowserMacro
        {
            Version = BrowserApiContract.Version,
            StopOnError = false,
            Steps =
            [
                new() { CommandId = "missing" },
                new() { CommandId = "throws" },
                new() { CommandId = "last" }
            ]
        });

        Assert.Equal(["last"], order);
        Assert.False(result.Succeeded);
        Assert.False(result.Stopped);
        Assert.Equal([0, 1, 2], result.Steps.Select(step => step.Index));
        Assert.Equal("unknown_command", result.Steps[0].Error?.Code);
        Assert.Equal("command_failed", result.Steps[1].Error?.Code);
        Assert.True(result.Steps[2].Ok);
    }

    [Fact]
    public void MacroRejectsUnsupportedVersions()
    {
        BrowserApiException error = Assert.Throws<BrowserApiException>(() =>
            new BrowserMacroExecutor(new CommandRegistry()).Run(new BrowserMacro { Version = 99 }));
        Assert.Equal("unsupported_macro_version", error.Code);
    }

    [Fact]
    public async Task MacroAndKeybindingSchemasRejectMissingOrExplicitlyUnsupportedFields()
    {
        using TestApplication test = TestApplication.Create();
        test.Session.Commands.Register("plain", "Plain", () => { }, key: "Ctrl+P");
        var api = new BrowserApiBackend(() => test.Application);

        using JsonDocument missingSteps = await Invoke(api, "macros.run", new
        {
            macro = new { version = BrowserApiContract.MacroVersion }
        });
        Assert.Equal("invalid_macro", ErrorCode(missingSteps));

        using JsonDocument legacyNullArgs = await Invoke(api, "macros.run", new
        {
            macro = new
            {
                version = BrowserApiContract.LegacyMacroVersion,
                steps = new object[] { new { commandId = "plain", args = (object?)null } }
            }
        });
        Assert.Equal("invalid_arguments", legacyNullArgs.RootElement.GetProperty("result")
            .GetProperty("steps")[0].GetProperty("error").GetProperty("code").GetString());

        using JsonDocument removalNullArgs = await Invoke(api, "keybindings.update", new
        {
            json = "[{\"key\":\"Ctrl+P\",\"command\":\"-plain\",\"args\":null}]"
        });
        Assert.Equal("invalid_keybinding", ErrorCode(removalNullArgs));
    }

    [Fact]
    public async Task CommandsListAndRunReturnStructuredResultsAndErrors()
    {
        using TestApplication test = TestApplication.Create();
        int runs = 0;
        test.Session.Commands.Register("z.command", "Zed", () => runs++, menuPath: "Tools/Zed", toolbar: true);
        test.Session.Commands.Register("a.disabled", "Disabled", () => runs++, enabled: () => false);
        var api = new BrowserApiBackend(() => test.Application);

        using JsonDocument listed = await Invoke(api, "commands.list");
        Assert.True(listed.RootElement.GetProperty("ok").GetBoolean());
        JsonElement commands = listed.RootElement.GetProperty("result").GetProperty("commands");
        string[] ids = commands.EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        Assert.Contains("z.command", ids);
        JsonElement zed = commands.EnumerateArray().Single(item => item.GetProperty("id").GetString() == "z.command");
        Assert.Equal("Tools/Zed", zed.GetProperty("menuPaths")[0].GetString());
        Assert.True(zed.GetProperty("toolbar").GetBoolean());

        using JsonDocument ran = await Invoke(api, "commands.run", new { commandId = "z.command" });
        Assert.True(ran.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, runs);

        using JsonDocument disabled = await Invoke(api, "commands.run", new { commandId = "a.disabled" });
        Assert.False(disabled.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("command_disabled", ErrorCode(disabled));

        using JsonDocument missing = await Invoke(api, "commands.run", new { commandId = "missing" });
        Assert.Equal("unknown_command", ErrorCode(missing));
    }

    [Fact]
    public async Task CommandsRunValidatesArgumentsAndDescriptorsExposeMetadata()
    {
        using TestApplication test = TestApplication.Create();
        int? received = null;
        test.Session.Commands.Register("arg.command", "Argument Command", context =>
            received = context.Arguments!.Value.GetProperty("value").GetInt32(),
            new CommandArgumentSchema(
                Help: "Args: { value: positive integer }",
                Required: true,
                DefaultValue: JsonSerializer.SerializeToElement(new { value = 1 }),
                Validator: args => args is { ValueKind: JsonValueKind.Object }
                    && args.Value.TryGetProperty("value", out JsonElement value)
                    && value.TryGetInt32(out int number) && number > 0
                        ? null : "value must be a positive integer."));
        test.Session.Commands.Register("plain.command", "Plain", () => { });
        var api = new BrowserApiBackend(() => test.Application);

        using JsonDocument listed = await Invoke(api, "commands.list");
        JsonElement descriptor = listed.RootElement.GetProperty("result").GetProperty("commands")
            .EnumerateArray().Single(command => command.GetProperty("id").GetString() == "arg.command");
        JsonElement metadata = descriptor.GetProperty("arguments");
        Assert.True(metadata.GetProperty("required").GetBoolean());
        Assert.True(metadata.GetProperty("hasDefaultValue").GetBoolean());
        Assert.Contains("positive integer", metadata.GetProperty("help").GetString());
        Assert.Equal(1, metadata.GetProperty("defaultValue").GetProperty("value").GetInt32());

        using JsonDocument defaulted = await Invoke(api, "commands.run", new { commandId = "arg.command" });
        Assert.True(defaulted.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, received);

        using JsonDocument valid = await Invoke(api, "commands.run",
            new { commandId = "arg.command", args = new { value = 7 } });
        Assert.True(valid.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(7, received);

        using JsonDocument invalid = await Invoke(api, "commands.run",
            new { commandId = "arg.command", args = new { value = 0 } });
        Assert.False(invalid.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_arguments", ErrorCode(invalid));
        Assert.Contains("positive integer", invalid.RootElement.GetProperty("error").GetProperty("message").GetString());

        using JsonDocument unexpected = await Invoke(api, "commands.run",
            new { commandId = "plain.command", args = new { value = 1 } });
        Assert.Equal("invalid_arguments", ErrorCode(unexpected));
    }

    [Fact]
    public async Task CommandsAndMacrosIncludeActiveDocumentContributions()
    {
        var received = new List<int>();
        var command = new Command(
            "document.command",
            "Document Command",
            static () => { },
            Invoke: context => received.Add(context.Arguments!.Value.GetProperty("value").GetInt32()),
            ArgumentSchema: new CommandArgumentSchema(
                "Args: { value: positive integer }",
                Required: true,
                Validator: args => args is { ValueKind: JsonValueKind.Object }
                    && args.Value.TryGetProperty("value", out JsonElement value)
                    && value.TryGetInt32(out int number) && number > 0
                        ? null : "value must be a positive integer."));
        using TestApplication test = TestApplication.Create(
            contributions: [new CommandContribution(command, "Document/Run", Toolbar: true)]);
        var api = new BrowserApiBackend(() => test.Application);

        using JsonDocument listed = await Invoke(api, "commands.list");
        JsonElement descriptor = listed.RootElement.GetProperty("result").GetProperty("commands")
            .EnumerateArray().Single(item => item.GetProperty("id").GetString() == "document.command");
        Assert.Equal("Document/Run", descriptor.GetProperty("menuPaths")[0].GetString());
        Assert.True(descriptor.GetProperty("toolbar").GetBoolean());
        Assert.True(descriptor.GetProperty("arguments").GetProperty("required").GetBoolean());

        using JsonDocument ran = await Invoke(api, "commands.run", new
        {
            commandId = "document.command",
            args = new { value = 5 }
        });
        Assert.True(ran.RootElement.GetProperty("ok").GetBoolean());

        using JsonDocument macro = await Invoke(api, "macros.run", new
        {
            macro = new
            {
                version = BrowserApiContract.MacroVersion,
                steps = new[] { new { commandId = "document.command", args = new { value = 9 } } }
            }
        });
        Assert.True(macro.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(macro.RootElement.GetProperty("result").GetProperty("succeeded").GetBoolean());
        Assert.Equal([5, 9], received);
    }

    [Fact]
    public async Task KeybindingsRoundTripChordArgumentsAndRemovalEntries()
    {
        using TestApplication test = TestApplication.Create();
        int? received = null;
        test.Session.Commands.Register("test.command", "Test",
            context => received = context.Arguments!.Value.GetProperty("value").GetInt32(),
            new CommandArgumentSchema("Args: { value: integer }", Required: true,
                Validator: args => args is { ValueKind: JsonValueKind.Object }
                    && args.Value.TryGetProperty("value", out JsonElement value)
                    && value.ValueKind == JsonValueKind.Number ? null : "value must be an integer."),
            key: "Ctrl+A");
        var api = new BrowserApiBackend(() => test.Application);

        using JsonDocument updated = await Invoke(api, "keybindings.update", new
        {
            json = "[{\"key\":\"Ctrl+K Ctrl+B\",\"command\":\"test.command\",\"args\":{\"value\":3}}]"
        });
        Assert.True(updated.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Ctrl+K Ctrl+B", KeyOf(updated, "test.command"));
        JsonElement binding = updated.RootElement.GetProperty("result").GetProperty("bindings")[0];
        Assert.Equal(3, binding.GetProperty("args").GetProperty("value").GetInt32());
        string roundTripJson = updated.RootElement.GetProperty("result").GetProperty("json").GetString()!;
        using JsonDocument roundTrip = JsonDocument.Parse(roundTripJson);
        Assert.Equal("Ctrl+K Ctrl+B", roundTrip.RootElement[0].GetProperty("key").GetString());
        Assert.Equal(3, roundTrip.RootElement[0].GetProperty("args").GetProperty("value").GetInt32());

        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        EditorKeyDispatchResult firstStroke = test.Session.DispatchKey(
            Luxel.UI.Key.K, Luxel.UI.KeyModifiers.Ctrl, timestamp);
        Assert.True(firstStroke.Pending);
        EditorKeyDispatchResult secondStroke = test.Session.DispatchKey(
            Luxel.UI.Key.B, Luxel.UI.KeyModifiers.Ctrl, timestamp.AddMilliseconds(10));
        Assert.True(secondStroke.Executed);
        Assert.Equal(3, received);

        using JsonDocument removed = await Invoke(api, "keybindings.update", new
        {
            json = "[{\"key\":\"Ctrl+K Ctrl+B\",\"command\":\"-test.command\"}]"
        });
        Assert.True(removed.RootElement.GetProperty("ok").GetBoolean());
        Assert.Null(KeyOf(removed, "test.command"));

        using JsonDocument invalidChord = await Invoke(api, "keybindings.update", new
        {
            json = "[{\"key\":\"Ctrl+K Nope\",\"command\":\"test.command\",\"args\":{\"value\":3}}]"
        });
        Assert.False(invalidChord.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("keybinding_validation_failed", ErrorCode(invalidChord));
    }

    [Fact]
    public async Task KeybindingsRejectArgumentsThatDoNotMatchCommandContracts()
    {
        using TestApplication test = TestApplication.Create();
        test.Session.Commands.Register("plain.command", "Plain", () => { });
        test.Session.Commands.Register("required.command", "Required", _ => { },
            new CommandArgumentSchema("Args: { name: non-empty string }", Required: true,
                Validator: args => args is { ValueKind: JsonValueKind.Object }
                    && args.Value.TryGetProperty("name", out JsonElement name)
                    && name.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(name.GetString()) ? null : "name is required."));
        int? defaultReceived = null;
        test.Session.Commands.Register("default.command", "Default",
            context => defaultReceived = context.Arguments!.Value.GetProperty("value").GetInt32(),
            new CommandArgumentSchema("Args: { value: integer }", Required: true,
                DefaultValue: JsonSerializer.SerializeToElement(new { value = 12 }),
                Validator: args => args is { ValueKind: JsonValueKind.Object }
                    && args.Value.TryGetProperty("value", out JsonElement value)
                    && value.TryGetInt32(out _) ? null : "value must be an integer."));
        var api = new BrowserApiBackend(() => test.Application);

        using JsonDocument defaulted = await Invoke(api, "keybindings.update", new
        {
            json = "[{\"key\":\"Ctrl+D\",\"command\":\"default.command\"}]"
        });
        Assert.True(defaulted.RootElement.GetProperty("ok").GetBoolean());
        EditorKeyDispatchResult defaultDispatch = test.Session.DispatchKey(
            Luxel.UI.Key.D, Luxel.UI.KeyModifiers.Ctrl, DateTimeOffset.UtcNow);
        Assert.True(defaultDispatch.Executed);
        Assert.Equal(12, defaultReceived);

        using JsonDocument nullEntry = await Invoke(api, "keybindings.update", new { json = "[null]" });
        Assert.Equal("invalid_keybinding", ErrorCode(nullEntry));

        using JsonDocument unexpected = await Invoke(api, "keybindings.update", new
        {
            json = "[{\"key\":\"Ctrl+P\",\"command\":\"plain.command\",\"args\":{\"value\":1}}]"
        });
        Assert.Equal("invalid_arguments", ErrorCode(unexpected));

        using JsonDocument missing = await Invoke(api, "keybindings.update", new
        {
            json = "[{\"key\":\"Ctrl+R\",\"command\":\"required.command\"}]"
        });
        Assert.Equal("invalid_arguments", ErrorCode(missing));

        using JsonDocument invalid = await Invoke(api, "keybindings.update", new
        {
            json = "[{\"key\":\"Ctrl+R\",\"command\":\"required.command\",\"args\":{\"name\":\"\"}}]"
        });
        Assert.Equal("invalid_arguments", ErrorCode(invalid));
    }

    [Fact]
    public async Task KeybindingValidationIsAtomicAndResetCanTargetOneCommandOrAll()
    {
        using TestApplication test = TestApplication.Create();
        test.Session.Commands.Register("first", "First", () => { }, key: "Ctrl+A");
        test.Session.Commands.Register("second", "Second", () => { }, key: "Ctrl+B");
        var api = new BrowserApiBackend(() => test.Application);

        using JsonDocument invalid = await Invoke(api, "keybindings.update", new
        {
            json = "[{\"key\":\"Ctrl+C\",\"command\":\"first\"},{\"key\":\"Ctrl+C\",\"command\":\"second\"}]"
        });
        Assert.Equal("keybinding_validation_failed", ErrorCode(invalid));
        Assert.Equal("Ctrl+A", KeyGestures.Format(test.Session.Commands.EffectiveGesture("first")!.Value));
        Assert.Equal("Ctrl+B", KeyGestures.Format(test.Session.Commands.EffectiveGesture("second")!.Value));

        using JsonDocument applied = await Invoke(api, "keybindings.update", new
        {
            json = "[{\"key\":\"Ctrl+C\",\"command\":\"first\"}]"
        });
        Assert.Equal("Ctrl+C", KeyOf(applied, "first"));

        using JsonDocument targeted = await Invoke(api, "keybindings.reset", new { commandId = "first" });
        Assert.Equal("Ctrl+A", KeyOf(targeted, "first"));

        using JsonDocument reapplied = await Invoke(api, "keybindings.update", new
        {
            json = "[{\"key\":\"Ctrl+C\",\"command\":\"first\"},{\"key\":\"Ctrl+D\",\"command\":\"second\"}]"
        });
        Assert.True(reapplied.RootElement.GetProperty("ok").GetBoolean());
        using JsonDocument resetAll = await Invoke(api, "keybindings.reset");
        Assert.Equal("Ctrl+A", KeyOf(resetAll, "first"));
        Assert.Equal("Ctrl+B", KeyOf(resetAll, "second"));
    }

    [Fact]
    public async Task MacrosRunUsesDataOnlySchemaAndReportsIndexedFailures()
    {
        using TestApplication test = TestApplication.Create();
        var order = new List<string>();
        test.Session.Commands.Register("first", "First", () => order.Add("first"));
        test.Session.Commands.Register("last", "Last", () => order.Add("last"));
        var api = new BrowserApiBackend(() => test.Application);

        using JsonDocument result = await Invoke(api, "macros.run", new
        {
            macro = new
            {
                version = 1,
                stopOnError = false,
                steps = new[] { new { commandId = "first" }, new { commandId = "unknown" }, new { commandId = "last" } }
            }
        });
        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        JsonElement run = result.RootElement.GetProperty("result");
        Assert.False(run.GetProperty("succeeded").GetBoolean());
        Assert.Equal(["first", "last"], order);
        Assert.Equal([0, 1, 2], run.GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("index").GetInt32()));

        string arbitraryCode = "{\"version\":1,\"operation\":\"macros.run\",\"arguments\":{\"macro\":{\"version\":1,\"steps\":[{\"commandId\":\"first\",\"script\":\"alert(1)\"}]}}}";
        using JsonDocument rejected = JsonDocument.Parse(await api.InvokeAsync(arbitraryCode));
        Assert.False(rejected.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_arguments", ErrorCode(rejected));
    }

    [Fact]
    public async Task MacroVersionTwoExecutesArgumentsAndReportsInvalidArgumentsPerStep()
    {
        using TestApplication test = TestApplication.Create();
        var values = new List<int>();
        test.Session.Commands.Register("arg.command", "Argument Command",
            context => values.Add(context.Arguments!.Value.GetProperty("value").GetInt32()),
            new CommandArgumentSchema("Args: { value: positive integer }", Required: true,
                Validator: args => args is { ValueKind: JsonValueKind.Object }
                    && args.Value.TryGetProperty("value", out JsonElement value)
                    && value.TryGetInt32(out int number) && number > 0
                        ? null : "value must be a positive integer."));
        test.Session.Commands.Register("plain.command", "Plain", () => values.Add(99));
        var api = new BrowserApiBackend(() => test.Application);

        using JsonDocument result = await Invoke(api, "macros.run", new
        {
            macro = new
            {
                version = BrowserApiContract.MacroVersion,
                stopOnError = false,
                steps = new object[]
                {
                    new { commandId = "arg.command", args = new { value = 4 } },
                    new { commandId = "arg.command", args = new { value = 0 } },
                    new { commandId = "plain.command", args = new { ignored = true } },
                    new { commandId = "arg.command", args = new { value = 8 } }
                }
            }
        });

        Assert.True(result.RootElement.GetProperty("ok").GetBoolean());
        JsonElement run = result.RootElement.GetProperty("result");
        Assert.Equal(BrowserApiContract.MacroVersion, run.GetProperty("version").GetInt32());
        Assert.False(run.GetProperty("succeeded").GetBoolean());
        Assert.Equal([4, 8], values);
        JsonElement[] steps = run.GetProperty("steps").EnumerateArray().ToArray();
        Assert.True(steps[0].GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_arguments", steps[1].GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("invalid_arguments", steps[2].GetProperty("error").GetProperty("code").GetString());
        Assert.True(steps[3].GetProperty("ok").GetBoolean());

        using JsonDocument legacy = await Invoke(api, "macros.run", new
        {
            macro = new
            {
                version = BrowserApiContract.LegacyMacroVersion,
                steps = new[] { new { commandId = "arg.command", args = new { value = 2 } } }
            }
        });
        JsonElement legacyStep = legacy.RootElement.GetProperty("result").GetProperty("steps")[0];
        Assert.Equal("invalid_arguments", legacyStep.GetProperty("error").GetProperty("code").GetString());
    }

    [System.Runtime.Versioning.SupportedOSPlatform("browser")]
    [Fact]
    public async Task DemoArgumentCommandsAreDiscoverableValidatedAndAcceptanceCompatible()
    {
        using TestApplication test = TestApplication.Create(textDocument: true);
        var automation = new BrowserDemoAutomation(test.Projects, new TestDemoProvider());
        automation.Attach(test.Application);
        automation.EnsureCommandsRegistered();
        var api = new BrowserApiBackend(() => test.Application, automation.Snapshot);

        using JsonDocument listed = await Invoke(api, "commands.list");
        JsonElement[] descriptors = listed.RootElement.GetProperty("result").GetProperty("commands")
            .EnumerateArray().Where(command => command.GetProperty("id").GetString() is { } id
                && id.StartsWith("browser.demo.", StringComparison.Ordinal)).ToArray();
        Assert.Contains(descriptors, command => command.GetProperty("id").GetString() == BrowserDemoCommandIds.SelectEntity);
        Assert.Contains(descriptors, command => command.GetProperty("id").GetString() == BrowserDemoCommandIds.OpenPath);
        JsonElement editDescriptor = descriptors.Single(command =>
            command.GetProperty("id").GetString() == BrowserDemoCommandIds.EditActiveText);
        Assert.Contains("65536", editDescriptor.GetProperty("arguments").GetProperty("help").GetString());

        using JsonDocument edited = await Invoke(api, "commands.run", new
        {
            commandId = BrowserDemoCommandIds.EditActiveText,
            args = new { text = " browser" }
        });
        Assert.True(edited.RootElement.GetProperty("ok").GetBoolean());
        var text = Assert.IsType<TextDocument>(test.Session.ActiveDocument);
        Assert.Equal("seed browser", text.Text.Peek());

        using JsonDocument strict = await Invoke(api, "commands.run", new
        {
            commandId = BrowserDemoCommandIds.EditActiveText,
            args = new { text = "ignored", script = "alert(1)" }
        });
        Assert.Equal("invalid_arguments", ErrorCode(strict));
        Assert.Equal("seed browser", text.Text.Peek());

        using JsonDocument traversal = await Invoke(api, "commands.run", new
        {
            commandId = BrowserDemoCommandIds.OpenPath,
            args = new { path = "../secret.txt" }
        });
        Assert.Equal("invalid_arguments", ErrorCode(traversal));

        CommandArgumentSchema selectSchema = test.Session.Commands.Find(BrowserDemoCommandIds.SelectEntity)!.ArgumentSchema!;
        Assert.Contains("non-negative", selectSchema.Validator!(JsonSerializer.SerializeToElement(new { entityId = -1 })));

        automation.Invoke("edit-active", " legacy");
        Assert.Equal("seed browser legacy", text.Text.Peek());
    }

    [Fact]
    public async Task VersionSnapshotLifecycleAndDisposeAreStructured()
    {
        using TestApplication test = TestApplication.Create();
        var api = new BrowserApiBackend(() => test.Application, () => "{\"contractVersion\":7,\"value\":\"demo\"}");

        using JsonDocument unsupported = JsonDocument.Parse(await api.InvokeAsync("{\"version\":2,\"operation\":\"snapshot\"}"));
        Assert.Equal("unsupported_version", ErrorCode(unsupported));

        using JsonDocument snapshot = await Invoke(api, "snapshot");
        Assert.Equal("demo", snapshot.RootElement.GetProperty("result").GetProperty("value").GetString());

        using JsonDocument lifecycle = await Invoke(api, "lifecycle.get");
        Assert.Equal("running", lifecycle.RootElement.GetProperty("result").GetProperty("state").GetString());
        Assert.True(lifecycle.RootElement.GetProperty("result").GetProperty("ready").GetBoolean());

        using JsonDocument disposed = await Invoke(api, "dispose");
        Assert.True(disposed.RootElement.GetProperty("result").GetProperty("exitRequested").GetBoolean());
        Assert.Equal("disposed", disposed.RootElement.GetProperty("result").GetProperty("state").GetString());
    }

    [Fact]
    public async Task DisposeRejectsUnsavedChangesWithoutStoppingTheApplication()
    {
        using TestApplication test = TestApplication.Create();
        test.Session.ActiveDocument!.Dirty.Value = true;
        var api = new BrowserApiBackend(() => test.Application);

        using JsonDocument result = await Invoke(api, "dispose");

        Assert.False(result.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("unsaved_changes", ErrorCode(result));
        Assert.False(test.Application.ExitRequested);
        using JsonDocument lifecycle = await Invoke(api, "lifecycle.get");
        Assert.Equal("running", lifecycle.RootElement.GetProperty("result").GetProperty("state").GetString());
        Assert.Same(test.Session, test.Application.Session);
    }

    private static async Task<JsonDocument> Invoke(BrowserApiBackend api, string operation, object? arguments = null)
    {
        string json = JsonSerializer.Serialize(new { version = BrowserApiContract.Version, operation, arguments });
        return JsonDocument.Parse(await api.InvokeAsync(json));
    }

    private static string ErrorCode(JsonDocument document)
        => document.RootElement.GetProperty("error").GetProperty("code").GetString()!;

    private static string? KeyOf(JsonDocument document, string command)
    {
        JsonElement entry = document.RootElement.GetProperty("result").GetProperty("commands").EnumerateArray()
            .Single(item => item.GetProperty("command").GetString() == command);
        return entry.GetProperty("key").ValueKind == JsonValueKind.Null ? null : entry.GetProperty("key").GetString();
    }

    private sealed class TestApplication : IDisposable
    {
        private TestApplication(EditorApplication application, BrowserProjectStorageProvider projects)
        {
            Application = application;
            Projects = projects;
            Session = application.Session!;
        }

        public EditorApplication Application { get; }
        public BrowserProjectStorageProvider Projects { get; }
        public EditorSession Session { get; }

        public static TestApplication Create(bool textDocument = false,
            IReadOnlyList<CommandContribution>? contributions = null)
        {
            var projects = new BrowserProjectStorageProvider();
            projects.Register("test", () => new MemoryFileStorage());
            var host = new TestHost(projects);
            var app = new EditorApplication(host, files =>
            {
                IEditorDocument document = textDocument
                    ? new TextDocument("text", "Test", _ => throw new InvalidOperationException("View is not used."), "seed")
                    : new TestDocument(contributions);
                return new EditorSession(files,
                    new Dictionary<string, IEditorDocument> { ["doc"] = document }, DockTree.Single("doc"));
            });
            Assert.True(app.OpenProject("test"));
            return new(app, projects);
        }

        public void Dispose() => Application.Dispose();
    }

    private sealed class TestDemoProvider : IBrowserDemoProjectProvider
    {
        public IFileStorage Storage { get; } = new MemoryFileStorage();
        public Task InitializeAsync() => Task.CompletedTask;
        public Task ResetAsync() => Task.CompletedTask;
    }

    private sealed class TestHost(BrowserProjectStorageProvider projects) : IEditorHost
    {
        public IFileStorage Files { get; } = new MemoryFileStorage();
        public IProjectPicker Projects { get; } = new NullProjectPicker();
        public IEditorSettingsStore Settings { get; } = new MemoryEditorSettingsStore();
        public IBuildService Builds { get; } = new NullBuildService();
        public IHostCapabilities Capabilities { get; } = new EditorHostCapabilities(false);
        public IEditorProjectStorageProvider ProjectStorage => projects;
        public IEditorProjectBackend ProjectBackend => projects;
    }

    private sealed class NullProjectPicker : IProjectPicker
    {
        public bool IsAvailable => false;
        public string? PickProject() => null;
    }

    private sealed class NullBuildService : IBuildService
    {
        public bool IsAvailable => false;
        public void Build() { }
    }

    private sealed class TestDocument(IReadOnlyList<CommandContribution>? contributions = null) : IEditorDocument
    {
        public string Kind => "test";
        public string Title => "Test";
        public IReadOnlyList<CommandContribution> Contributions { get; } = contributions ?? [];
        public Luxel.UI.Signal<bool> Dirty { get; } = new(false);
        public bool CanUndo => false;
        public bool CanRedo => false;
        public Luxel.UI.Widget CreateView() => Kit.Spacer();
        public void Undo() { }
        public void Redo() { }
        public string Serialize() => "";
        public void LoadFrom(string content) { }
    }
}
