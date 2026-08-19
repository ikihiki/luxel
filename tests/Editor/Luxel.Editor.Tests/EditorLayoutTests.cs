using Luxel.Controls;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Tests;

public sealed class EditorLayoutTests
{
    [Fact]
    public void RestoreDoesNotWriteBackUntilMutation()
    {
        var store = new CountingSettings();
        DockTree saved = DockTree.Single("a", "b");
        store.Write(EditorLayoutService.SettingsKey, saved.Serialize());
        store.Writes = 0;
        using var service = new EditorLayoutService(store, () => DockTree.Single("a"), ["a", "b"]);
        EditorLayoutRestoreResult restored = service.Restore();
        var signal = new Signal<DockTree>(restored.Layout);
        service.Attach(signal);

        Assert.Equal(0, store.Writes);
        signal.Value = signal.Peek().ActivateTab("b");
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public void CorruptFutureAndInvalidLayoutsFallbackWithReason()
    {
        var store = new CountingSettings();
        store.Write(EditorLayoutService.SettingsKey, "{bad");
        using var service = new EditorLayoutService(store, () => DockTree.Single("a"), ["a"]);
        EditorLayoutRestoreResult result = service.Restore();
        Assert.True(result.UsedFallback);
        Assert.Contains("default", result.Reason, StringComparison.OrdinalIgnoreCase);

        store.Write(EditorLayoutService.SettingsKey, DockTree.Single("unknown").Serialize());
        Assert.True(service.Restore().UsedFallback);
    }

    [Fact]
    public void VisibilityFocusAndResetRestorePreviousLayout()
    {
        var store = new CountingSettings();
        DockTree initial = DockTree.Single("a", "b");
        using var service = new EditorLayoutService(store, () => initial, ["a", "b"]);
        var signal = new Signal<DockTree>(initial);
        service.Attach(signal);

        Assert.True(service.SetPaneVisible(signal, "b", false));
        Assert.Null(signal.Peek().GroupOf("b"));
        Assert.True(service.SetPaneVisible(signal, "b", true));
        Assert.True(service.EnterFocusMode(signal, "b"));
        Assert.Equal(["b"], signal.Peek().Groups.Single().Tabs);
        Assert.True(service.ExitFocusMode(signal));
        Assert.NotNull(signal.Peek().GroupOf("a"));
        service.Reset(signal);
        Assert.NotNull(signal.Peek().GroupOf("a"));
    }

    [Fact]
    public void FocusModeDoesNotPersistTemporaryLayoutAndDynamicDocumentsCanPersist()
    {
        var store = new CountingSettings();
        DockTree initial = DockTree.Single("a", "b");
        using var service = new EditorLayoutService(store, () => initial, ["a", "b"]);
        var signal = new Signal<DockTree>(initial);
        service.Attach(signal);

        Assert.True(service.EnterFocusMode(signal, "b"));
        Assert.Equal(0, store.Writes);
        Assert.True(service.ExitFocusMode(signal));
        Assert.Equal(1, store.Writes);

        service.RegisterItemId("dynamic");
        signal.Value = signal.Peek().AddTab(signal.Peek().Groups.First().Id, "dynamic");
        Assert.Equal(2, store.Writes);
        Assert.Null(service.LastStatus.Value);
        service.UnregisterItemId("dynamic");
    }

    private sealed class CountingSettings : IEditorSettingsStore
    {
        private readonly Dictionary<string, string> _values = [];
        public int Writes { get; set; }
        public string? Read(string key) => _values.GetValueOrDefault(key);
        public void Write(string key, string value) { _values[key] = value; Writes++; }
    }
}
