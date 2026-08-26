using Luxel.Controls;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Tests;

public sealed class EditorSettingsTests
{
    [Fact]
    public void SettingsValidatePersistReactAndReset()
    {
        var store = new MemoryEditorSettingsStore();
        var service = new EditorSettingsService(store);
        int changes = 0;
        using IDisposable effect = Reactive.Effect(() => { _ = service.Current.Value; changes++; });

        Assert.False(service.Apply(EditorSettings.Defaults with { UiScale = 9 }));
        Assert.True(service.Apply(EditorSettings.Defaults with { Theme = EditorThemePreference.Dark, UiScale = 1.25f }));
        Assert.Equal(EditorThemePreference.Dark, new EditorSettingsService(store).Current.Value.Theme);
        service.Reset();
        Assert.Equal(EditorSettings.Defaults, service.Current.Value);
        Assert.True(changes >= 2);
    }

    [Fact]
    public void KeymapRejectsInvalidUnknownAndConflictingBindingsThenResets()
    {
        var commands = new CommandRegistry();
        commands.Register("a", "A", () => { }, key: "Ctrl+A");
        commands.Register("b", "B", () => { }, key: "Ctrl+B");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore());

        Assert.Equal(3, keymap.Validate([
            new("missing", "Ctrl+M"), new("a", "not-a-key"), new("a", "Ctrl+X"), new("b", "Ctrl+X")]).Count);
        Assert.Empty(keymap.Apply([new("a", "Ctrl+K")]));
        Assert.Equal("Ctrl+K", KeyGestures.Format(commands.EffectiveGesture("a")!.Value));
        keymap.Reset("a");
        Assert.Equal("Ctrl+A", KeyGestures.Format(commands.EffectiveGesture("a")!.Value));
    }

    [Fact]
    public void KeymapRejectsConflictWithExistingEffectiveBinding()
    {
        var commands = new CommandRegistry();
        commands.Register("a", "A", () => { }, key: "Ctrl+A");
        commands.Register("b", "B", () => { }, key: "Ctrl+B");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore());

        IReadOnlyList<EditorKeymapIssue> issues = keymap.Apply([new("b", "Ctrl+A")]);

        Assert.Single(issues);
        Assert.Contains("a", issues[0].Message);
        Assert.Equal("Ctrl+B", KeyGestures.Format(commands.EffectiveGesture("b")!.Value));
    }

    [Fact]
    public void KeymapPersistsVsCodeArrayRemovalAndReset()
    {
        var store = new MemoryEditorSettingsStore();
        var commands = new CommandRegistry();
        commands.Register("a", "A", () => { }, key: "Ctrl+A");
        var keymap = new EditorKeymap(commands, store);

        Assert.Empty(keymap.Update(new EditorKeyBinding("a", null)));
        Assert.Null(commands.EffectiveGesture("a"));
        string json = Assert.IsType<string>(store.Read(EditorKeymap.SettingsKey));
        Assert.StartsWith("[", json.TrimStart());
        Assert.Contains("\"key\": \"Ctrl+A\"", json);
        Assert.Contains("\"command\": \"-a\"", json);
        Assert.Equal("-a", Assert.Single(keymap.Get()).CommandId);

        var restoredCommands = new CommandRegistry();
        restoredCommands.Register("a", "A", () => { }, key: "Ctrl+A");
        var restored = new EditorKeymap(restoredCommands, store);
        Assert.Null(restoredCommands.EffectiveGesture("a"));
        restored.Reset("a");
        Assert.Equal("Ctrl+A", restored.EffectiveBinding("a"));
        Assert.Empty(restored.Get());
    }

    [Fact]
    public void KeymapAppliesPersistedOverridesToLateRegisteredCommands()
    {
        var store = new MemoryEditorSettingsStore();
        store.Write(EditorKeymap.SettingsKey, """
            [
              { "key": "Ctrl+K Ctrl+L", "command": "extension.late", "args": { "source": "persisted" } }
            ]
            """);
        var commands = new CommandRegistry();
        var keymap = new EditorKeymap(commands, store);
        string? source = null;

        commands.Register("extension.late", "Late", invocation =>
                source = invocation.Arguments?.GetProperty("source").GetString(),
            new CommandArgumentSchema(Required: true), key: "Ctrl+L");

        Assert.Equal("Ctrl+K Ctrl+L", keymap.EffectiveBinding("extension.late"));
        Assert.Contains(keymap.CommandDescriptors,
            x => x.Id == "extension.late" && x.EffectiveGestureText == "Ctrl+K Ctrl+L");
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Pending);
        Assert.True(keymap.HandleKey(new KeyGesture(Key.L, Ctrl: true), now.AddMilliseconds(10)).Executed);
        Assert.Equal("persisted", source);
    }

    [Fact]
    public void KeymapPersistsChordsAndArgumentsAndInvokesWithArguments()
    {
        var store = new MemoryEditorSettingsStore();
        var commands = new CommandRegistry();
        string? received = null;
        commands.Register("save.special", "Special Save", invocation => received = invocation.Arguments?.GetProperty("mode").GetString(),
            new CommandArgumentSchema(Required: true));
        var keymap = new EditorKeymap(commands, store);
        using var args = System.Text.Json.JsonDocument.Parse("{\"mode\":\"all\"}");

        Assert.Empty(keymap.Update(new EditorKeyBinding(
            "save.special", "Ctrl+K Ctrl+S", args.RootElement.Clone())));
        Assert.Equal("Ctrl+K Ctrl+S", keymap.EffectiveBinding("save.special"));
        string json = store.Read(EditorKeymap.SettingsKey)!;
        Assert.Contains("Ctrl+K Ctrl+S", json);
        Assert.Contains("\"args\"", json);

        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(EditorKeyDispatchStatus.Pending,
            keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Status);
        EditorKeyDispatchResult completed = keymap.HandleKey(new KeyGesture(Key.S, Ctrl: true), now.AddMilliseconds(10));
        Assert.True(completed.Executed);
        Assert.Equal("all", received);
    }

    [Fact]
    public void KeymapPrefixWinsOverExactAndTimeoutOrEscapeCancels()
    {
        var commands = new CommandRegistry();
        int exact = 0, chord = 0;
        commands.Register("exact", "Exact", () => exact++, key: "Ctrl+K");
        commands.Register("chord", "Chord", () => chord++, key: "Ctrl+K Ctrl+S");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore(), TimeSpan.FromMilliseconds(500));
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(EditorKeyDispatchStatus.Pending,
            keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Status);
        Assert.Equal(0, exact);
        Assert.True(keymap.AdvanceTime(now.AddMilliseconds(501)));
        Assert.False(keymap.HasPendingChord);
        Assert.Equal(0, exact);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now.AddSeconds(1)).Pending);
        Assert.Equal(EditorKeyDispatchStatus.Cancelled,
            keymap.HandleKey(new KeyGesture(Key.Escape), now.AddSeconds(1.1)).Status);
        Assert.Equal(0, exact);
        Assert.Equal(0, chord);
    }

    [Fact]
    public void KeymapMismatchCancelsAndRetriesStrokeAsFreshBinding()
    {
        var commands = new CommandRegistry();
        int chord = 0, retry = 0;
        commands.Register("chord", "Chord", () => chord++, key: "Ctrl+K Ctrl+S");
        commands.Register("retry", "Retry", () => retry++, key: "Ctrl+X");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore());
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Pending);
        EditorKeyDispatchResult result = keymap.HandleKey(new KeyGesture(Key.X, Ctrl: true), now.AddMilliseconds(10));

        Assert.True(result.Retried);
        Assert.True(result.Executed);
        Assert.Equal(0, chord);
        Assert.Equal(1, retry);
        Assert.False(keymap.HasPendingChord);
    }

    [Fact]
    public void KeymapAllowsPrefixRelationshipsButRejectsExactSequenceConflicts()
    {
        var commands = new CommandRegistry();
        commands.Register("short", "Short", () => { }, key: "Ctrl+K");
        commands.Register("long", "Long", () => { }, key: "Ctrl+L");
        commands.Register("duplicate", "Duplicate", () => { }, key: "Ctrl+D");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore());

        Assert.Empty(keymap.Validate([new("long", "Ctrl+K Ctrl+S")]));
        EditorKeymapIssue conflict = Assert.Single(keymap.Validate([new("duplicate", "Ctrl+K")]));
        Assert.Contains("short", conflict.Message);
    }

    [Fact]
    public void KeymapTimeoutClearsPrefixAndProcessesIncomingStrokeFresh()
    {
        var commands = new CommandRegistry();
        int exact = 0, chord = 0, fresh = 0;
        commands.Register("exact", "Exact", () => exact++, key: "Ctrl+K");
        commands.Register("chord", "Chord", () => chord++, key: "Ctrl+K Ctrl+S");
        commands.Register("fresh", "Fresh", () => fresh++, key: "Ctrl+X");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore(), TimeSpan.FromMilliseconds(500));
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Pending);
        EditorKeyDispatchResult result = keymap.HandleKey(
            new KeyGesture(Key.X, Ctrl: true), now.AddMilliseconds(501));

        Assert.True(result.Executed);
        Assert.False(result.Retried);
        Assert.Equal(0, exact);
        Assert.Equal(0, chord);
        Assert.Equal(1, fresh);
        Assert.False(keymap.HasPendingChord);
    }

    [Fact]
    public void KeymapTimestampRegressionContributesNoElapsedTime()
    {
        var commands = new CommandRegistry();
        int chord = 0;
        commands.Register("chord", "Chord", () => chord++, key: "Ctrl+K Ctrl+S");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore(), TimeSpan.FromMilliseconds(500));
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 1, TimeSpan.Zero);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Pending);
        Assert.False(keymap.AdvanceTime(now.AddSeconds(-10)));
        Assert.Equal(now.AddMilliseconds(500), keymap.PendingDeadline);
        EditorKeyDispatchResult result = keymap.HandleKey(
            new KeyGesture(Key.S, Ctrl: true), now.AddSeconds(-5));

        Assert.True(result.Executed);
        Assert.Equal(1, chord);
        Assert.False(keymap.HasPendingChord);
    }

    [Fact]
    public void KeymapDisabledCompletionIsConsumedWithoutExecutionAndClearsPending()
    {
        var commands = new CommandRegistry();
        int chord = 0;
        commands.Register("chord", "Chord", () => chord++, enabled: () => false,
            key: "Ctrl+K Ctrl+S");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore());
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Pending);
        EditorKeyDispatchResult result = keymap.HandleKey(
            new KeyGesture(Key.S, Ctrl: true), now.AddMilliseconds(10));

        Assert.Equal(EditorKeyDispatchStatus.Completed, result.Status);
        Assert.Equal(CommandExecutionStatus.Disabled, result.Execution?.Status);
        Assert.False(result.Executed);
        Assert.Equal(0, chord);
        Assert.False(keymap.HasPendingChord);
    }

    [Fact]
    public void RemovingBindingClearsPendingAndPreventsCompletion()
    {
        var commands = new CommandRegistry();
        int chord = 0;
        commands.Register("chord", "Chord", () => chord++, key: "Ctrl+K Ctrl+S");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore());
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Pending);
        Assert.Empty(keymap.Update(new EditorKeyBinding("-chord", "Ctrl+K Ctrl+S")));
        Assert.False(keymap.HasPendingChord);
        Assert.Equal(EditorKeyDispatchStatus.NoMatch,
            keymap.HandleKey(new KeyGesture(Key.S, Ctrl: true), now.AddMilliseconds(10)).Status);
        Assert.Equal(0, chord);
    }

    [Fact]
    public void SessionDisposeClearsPendingChord()
    {
        var session = new EditorSession(new MemoryFileStorage(), [],
            DockTree.Single(EditorPaneIds.Settings), keyChordTimeout: TimeSpan.FromSeconds(1));
        session.Commands.Register("chord", "Chord", () => { }, key: "Ctrl+K Ctrl+S");
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        Assert.True(session.DispatchKey(Key.K, KeyModifiers.Ctrl, now).Pending);

        session.Dispose();

        Assert.False(session.Keymap.HasPendingChord);
    }

    [Fact]
    public void KeymapRemovalUnbindsChordsAndDisabledChordCompletesWithoutRunning()
    {
        var commands = new CommandRegistry();
        int removedRuns = 0, disabledRuns = 0;
        commands.Register("removed", "Removed", () => removedRuns++, key: "Ctrl+K Ctrl+R");
        commands.Register("disabled", "Disabled", () => disabledRuns++, enabled: () => false,
            key: "Ctrl+K Ctrl+D");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore());
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.Empty(keymap.Update(new EditorKeyBinding("removed", null)));
        Assert.Null(keymap.EffectiveBinding("removed"));
        Assert.Equal(EditorKeyDispatchStatus.NoMatch,
            keymap.HandleKey(new KeyGesture(Key.R, Ctrl: true), now).Status);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now.AddSeconds(1)).Pending);
        EditorKeyDispatchResult disabled = keymap.HandleKey(
            new KeyGesture(Key.D, Ctrl: true), now.AddSeconds(1).AddMilliseconds(10));
        Assert.Equal(EditorKeyDispatchStatus.Completed, disabled.Status);
        Assert.Equal(CommandExecutionStatus.Disabled, disabled.Execution?.Status);
        Assert.False(disabled.Executed);
        Assert.Equal(0, removedRuns);
        Assert.Equal(0, disabledRuns);
    }

    [Fact]
    public void KeymapClampsRegressingInputTimestampsDuringPendingChord()
    {
        var commands = new CommandRegistry();
        int runs = 0;
        commands.Register("chord", "Chord", () => runs++, key: "Ctrl+K Ctrl+S");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore(), TimeSpan.FromMilliseconds(500));
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Pending);
        Assert.False(keymap.AdvanceTime(now.AddMilliseconds(-50)));
        Assert.True(keymap.HandleKey(new KeyGesture(Key.S, Ctrl: true), now.AddMilliseconds(-25)).Executed);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void KeymapTimeoutProcessesIncomingStrokeFreshWithoutRunningShadowedExact()
    {
        var commands = new CommandRegistry();
        int shadowed = 0, chord = 0, fresh = 0;
        commands.Register("shadowed", "Shadowed", () => shadowed++, key: "Ctrl+K");
        commands.Register("chord", "Chord", () => chord++, key: "Ctrl+K Ctrl+S");
        commands.Register("fresh", "Fresh", () => fresh++, key: "Ctrl+X");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore(), TimeSpan.FromMilliseconds(500));
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Pending);
        EditorKeyDispatchResult result = keymap.HandleKey(
            new KeyGesture(Key.X, Ctrl: true), now.AddMilliseconds(501));

        Assert.True(result.Executed);
        Assert.False(result.Retried);
        Assert.Equal(0, shadowed);
        Assert.Equal(0, chord);
        Assert.Equal(1, fresh);
        Assert.False(keymap.HasPendingChord);
    }

    [Fact]
    public void KeymapNegativeTimestampDeltaNeverExpiresPendingChord()
    {
        var commands = new CommandRegistry();
        int runs = 0;
        commands.Register("chord", "Chord", () => runs++, key: "Ctrl+K Ctrl+S");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore(), TimeSpan.FromMilliseconds(500));
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Pending);
        Assert.False(keymap.AdvanceTime(now.AddSeconds(-10)));
        Assert.True(keymap.HasPendingChord);
        EditorKeyDispatchResult result = keymap.HandleKey(
            new KeyGesture(Key.S, Ctrl: true), now.AddMilliseconds(10));

        Assert.True(result.Executed);
        Assert.Equal(1, runs);
    }

    [Fact]
    public void KeymapEscapeConsumesPendingChordAndMismatchWithoutFreshMatchCancelsOnce()
    {
        var commands = new CommandRegistry();
        int runs = 0;
        commands.Register("chord", "Chord", () => runs++, key: "Ctrl+K Ctrl+S");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore());
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Pending);
        EditorKeyDispatchResult escaped = keymap.HandleKey(
            new KeyGesture(Key.Escape), now.AddMilliseconds(10));
        Assert.Equal(EditorKeyDispatchStatus.Cancelled, escaped.Status);
        Assert.True(escaped.Handled);
        Assert.False(keymap.HasPendingChord);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now.AddMilliseconds(20)).Pending);
        EditorKeyDispatchResult mismatch = keymap.HandleKey(
            new KeyGesture(Key.X, Ctrl: true), now.AddMilliseconds(30));
        Assert.Equal(EditorKeyDispatchStatus.Cancelled, mismatch.Status);
        Assert.True(mismatch.Retried);
        Assert.Equal(0, runs);
        Assert.False(keymap.HasPendingChord);
    }

    [Fact]
    public void KeymapDisabledOrRemovedChordClearsWithoutExecution()
    {
        var commands = new CommandRegistry();
        bool enabled = true;
        int runs = 0;
        commands.Register("chord", "Chord", () => runs++, () => enabled, "Ctrl+K Ctrl+S");
        var keymap = new EditorKeymap(commands, new MemoryEditorSettingsStore());
        DateTimeOffset now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now).Pending);
        enabled = false;
        EditorKeyDispatchResult disabled = keymap.HandleKey(
            new KeyGesture(Key.S, Ctrl: true), now.AddMilliseconds(10));
        Assert.Equal(CommandExecutionStatus.Disabled, disabled.Execution?.Status);
        Assert.Equal(0, runs);
        Assert.False(keymap.HasPendingChord);

        enabled = true;
        Assert.True(keymap.HandleKey(new KeyGesture(Key.K, Ctrl: true), now.AddMilliseconds(20)).Pending);
        Assert.Empty(keymap.Update(new EditorKeyBinding("chord", null)));
        Assert.False(keymap.HasPendingChord);
        Assert.Equal(EditorKeyDispatchStatus.NoMatch,
            keymap.HandleKey(new KeyGesture(Key.S, Ctrl: true), now.AddMilliseconds(30)).Status);
        Assert.Equal(0, runs);
    }

    [Fact]
    public void KeymapRemovalShorthandRejectsAndNeverPersistsArguments()
    {
        var store = new MemoryEditorSettingsStore();
        var commands = new CommandRegistry();
        commands.Register("a", "A", _ => { }, new CommandArgumentSchema(), key: "Ctrl+A");
        var keymap = new EditorKeymap(commands, store);
        using var args = System.Text.Json.JsonDocument.Parse("{\"unexpected\":true}");

        EditorKeymapIssue issue = Assert.Single(keymap.Update(
            new EditorKeyBinding("a", null, args.RootElement)));

        Assert.Contains("cannot contain arguments", issue.Message);
        Assert.Empty(keymap.Get());
        Assert.Equal("Ctrl+A", keymap.EffectiveBinding("a"));
        Assert.Null(store.Read(EditorKeymap.SettingsKey));
    }

    [Fact]
    public void AutosaveOnlySavesDirtyBoundDocumentsAndKeepsDirtyOnFailure()
    {
        var files = new MemoryFileStorage();
        var good = new Doc("good");
        var unbound = new Doc("unbound");
        var bad = new BadDoc();
        using var session = new EditorSession(files,
            new Dictionary<string, IEditorDocument> { ["good"] = good, ["unbound"] = unbound, ["bad"] = bad },
            DockTree.Single("good", "unbound", "bad"));
        session.Documents.SaveAs(good, "good.txt");
        session.Documents.SaveAs(bad, "bad.txt");
        good.Dirty.Value = unbound.Dirty.Value = bad.Dirty.Value = true;

        Assert.Equal(1, session.Autosave());
        Assert.False(good.Dirty.Value);
        Assert.True(unbound.Dirty.Value);
        Assert.True(bad.Dirty.Value);
        Assert.Contains(session.DiagnosticsService.Items, x => x.Source == "autosave");
    }

    [Fact]
    public void SettingsReportReadWriteFailuresAndReloadPersistedValues()
    {
        var failingRead = new ThrowingSettingsStore { ThrowOnRead = true };
        var service = new EditorSettingsService(failingRead);
        Assert.NotNull(service.Error.Peek());
        Assert.Equal(EditorSettings.Defaults, service.Current.Peek());

        var store = new ThrowingSettingsStore();
        service = new EditorSettingsService(store);
        store.ThrowOnWrite = true;
        Assert.False(service.Apply(EditorSettings.Defaults with { UiScale = 1.5f }));
        Assert.Equal(EditorSettings.Defaults, service.Current.Peek());
        Assert.NotNull(service.Error.Peek());

        store.ThrowOnWrite = false;
        Assert.True(service.Apply(EditorSettings.Defaults with { Theme = EditorThemePreference.Dark, UiScale = 1.5f }));
        store.Value = System.Text.Json.JsonSerializer.Serialize(EditorSettings.Defaults with { Theme = EditorThemePreference.Light });
        Assert.True(service.Reload());
        Assert.Equal(EditorThemePreference.Light, service.Current.Peek().Theme);
    }

    [Fact]
    public void AutosaveSchedulerReschedulesAndCancelsIntervals()
    {
        var store = new MemoryEditorSettingsStore();
        var settings = new EditorSettingsService(store);
        var scheduler = new FakeIntervalScheduler { Settings = settings };
        int saves = 0;
        using var service = new EditorAutosaveScheduler(settings, scheduler, () => saves++);
        Assert.Single(scheduler.Active);
        Assert.Equal(TimeSpan.FromMinutes(2), scheduler.Active[0].Interval);

        scheduler.Active[0].Fire();
        Assert.Equal(1, saves);
        Assert.True(scheduler.Settings.Apply(EditorSettings.Defaults with { AutosaveInterval = TimeSpan.FromSeconds(30) }));
        Assert.True(scheduler.All[0].Disposed);
        Assert.Single(scheduler.Active);
        Assert.Equal(TimeSpan.FromSeconds(30), scheduler.Active[0].Interval);

        Assert.True(scheduler.Settings.Apply(EditorSettings.Defaults with { AutosaveEnabled = false }));
        Assert.Empty(scheduler.Active);
    }

    [Fact]
    public void SerializedIntervalPumpCoalescesMissedIntervalsAndDoesNotReenterCallbacks()
    {
        using var pump = new SerializedEditorIntervalPump();
        int calls = 0, depth = 0, maxDepth = 0;
        using IDisposable scheduled = pump.Schedule(TimeSpan.FromSeconds(10), () =>
        {
            calls++;
            maxDepth = Math.Max(maxDepth, ++depth);
            if (calls == 1) pump.Pump(TimeSpan.FromSeconds(10));
            depth--;
        });

        pump.Pump(TimeSpan.FromSeconds(30));

        Assert.Equal(2, calls);
        Assert.Equal(1, maxDepth);
        scheduled.Dispose();
        pump.Pump(TimeSpan.FromSeconds(30));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task AutosaveSchedulerDisposalWaitsForInFlightCallbackAndGuardsLateCallbacks()
    {
        var settings = new EditorSettingsService(new MemoryEditorSettingsStore());
        var scheduler = new FakeIntervalScheduler();
        using var entered = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int saves = 0;
        var autosave = new EditorAutosaveScheduler(settings, scheduler, () =>
        {
            entered.Set();
            release.Wait();
            saves++;
        });
        FakeIntervalScheduler.Entry entry = scheduler.Active.Single();
        Task callback = Task.Run(entry.Fire);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        Task disposing = Task.Run(() =>
        {
            disposeStarted.Set();
            autosave.Dispose();
        });
        Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotSame(disposing, await Task.WhenAny(disposing, Task.Delay(100)));
        release.Set();
        await Task.WhenAll(callback, disposing);
        Assert.Equal(1, saves);

        entry.FireEvenIfDisposed();
        Assert.Equal(1, saves);
    }

    [Fact]
    public void SessionSchedulerAutosavesOnlyDirtyBoundDocuments()
    {
        var files = new MemoryFileStorage();
        var doc = new Doc("bound");
        var scheduler = new FakeIntervalScheduler();
        using var session = new EditorSession(files, new Dictionary<string, IEditorDocument> { ["bound"] = doc },
            DockTree.Single("bound"), autosaveScheduler: scheduler);
        scheduler.Settings = session.Settings;
        session.Documents.SaveAs(doc, "bound.txt");
        doc.Dirty.Value = true;

        scheduler.Active.Single().Fire();

        Assert.False(doc.Dirty.Peek());
        Assert.Equal("bound", files.Read("bound.txt"));
    }

    [Fact]
    public void SessionDefaultAutosavePumpRunsSynchronouslyOnPumpingThread()
    {
        var files = new MemoryFileStorage();
        var doc = new Doc("bound");
        using var session = new EditorSession(files, new Dictionary<string, IEditorDocument> { ["bound"] = doc },
            DockTree.Single("bound"));
        session.Documents.SaveAs(doc, "bound.txt");
        Assert.True(session.Settings.Apply(EditorSettings.Defaults with
        {
            AutosaveInterval = TimeSpan.FromSeconds(10),
        }));
        doc.Dirty.Value = true;
        int pumpingThread = Environment.CurrentManagedThreadId;

        session.PumpAutosave(TimeSpan.FromSeconds(10));

        Assert.False(doc.Dirty.Peek());
        Assert.Equal(pumpingThread, doc.LastSerializeThreadId);
    }

    [Fact]
    public void ProductionShellPumpsAutosaveOnUiHostTick()
    {
        using var font = Luxel.Typography.VectorFont.LoadSystem();
        var files = new MemoryFileStorage();
        var doc = new Doc("shell-bound");
        using var session = new EditorSession(files,
            new Dictionary<string, IEditorDocument> { ["doc"] = doc }, DockTree.Single("doc"));
        session.Documents.SaveAs(doc, "shell-bound.txt");
        Assert.True(session.Settings.Apply(EditorSettings.Defaults with
        {
            AutosaveInterval = TimeSpan.FromSeconds(10),
        }));
        doc.Dirty.Value = true;
        using var host = new UiHost(new Luxel.Graphics.TwoD.RetainedCanvas(), font, 640, 480);
        host.SetRoot(EditorKit.EditorShell(session));

        host.Tick(10f);

        Assert.False(doc.Dirty.Peek());
        Assert.Equal("shell-bound", files.Read("shell-bound.txt"));
    }

    [Fact]
    public void ThemePreferenceAndUiScaleApplyToActiveShellHost()
    {
        using var font = Luxel.Typography.VectorFont.LoadSystem();
        using var session = new EditorSession([], DockTree.Single(EditorPaneIds.Settings));
        Assert.True(session.Settings.Apply(EditorSettings.Defaults with
        {
            Theme = EditorThemePreference.Dark,
            UiScale = 1.5f,
        }));
        using var host = new UiHost(new Luxel.Graphics.TwoD.RetainedCanvas(), font, 640, 480,
            new Signal<Theme>(Theme.Light));
        host.SetRoot(EditorKit.EditorShell(session));
        host.Tick(0);

        Assert.Equal(Theme.Dark.Background, host.Theme.Peek().Background);
        Assert.Equal(Theme.Dark.Font * 1.5f, host.Theme.Peek().Font, 3);
        Assert.Equal(Theme.Dark.ControlH * 1.5f, host.Theme.Peek().ControlH, 3);
    }

    private sealed class ThrowingSettingsStore : IEditorSettingsStore
    {
        public bool ThrowOnRead { get; set; }
        public bool ThrowOnWrite { get; set; }
        public string? Value { get; set; }
        public string? Read(string key) => ThrowOnRead ? throw new IOException("read failed") : Value;
        public void Write(string key, string value)
        {
            if (ThrowOnWrite) throw new IOException("write failed");
            Value = value;
        }
    }

    private sealed class FakeIntervalScheduler : IEditorIntervalScheduler
    {
        public EditorSettingsService Settings { get; set; } = null!;
        public List<Entry> All { get; } = [];
        public IReadOnlyList<Entry> Active => All.Where(x => !x.Disposed).ToArray();
        public IDisposable Schedule(TimeSpan interval, Action callback)
        {
            var entry = new Entry(interval, callback);
            All.Add(entry);
            return entry;
        }

        public sealed class Entry(TimeSpan interval, Action callback) : IDisposable
        {
            public TimeSpan Interval { get; } = interval;
            public bool Disposed { get; private set; }
            public void Fire() { if (!Disposed) callback(); }
            public void FireEvenIfDisposed() => callback();
            public void Dispose() => Disposed = true;
        }
    }

    private class Doc(string title) : IEditorDocument
    {
        public string Kind => "text"; public string Title => title; public Signal<bool> Dirty { get; } = new(false);
        public int LastSerializeThreadId { get; private set; }
        public bool CanUndo => false; public bool CanRedo => false; public Widget CreateView() => new Spacer();
        public void Undo() { } public void Redo() { }
        public virtual string Serialize()
        {
            LastSerializeThreadId = Environment.CurrentManagedThreadId;
            Dirty.Value = false;
            return title;
        }
        public void LoadFrom(string content) { Dirty.Value = false; }
    }
    private sealed class BadDoc : Doc
    {
        public BadDoc() : base("bad") { }
        private int _writes;
        public override string Serialize() => ++_writes == 1 ? base.Serialize() : throw new IOException("autosave failed");
    }
}
