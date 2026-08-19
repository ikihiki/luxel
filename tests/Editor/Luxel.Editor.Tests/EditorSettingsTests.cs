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
