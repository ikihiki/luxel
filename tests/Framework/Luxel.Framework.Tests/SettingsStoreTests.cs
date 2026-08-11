using Luxel.Settings;
using Luxel.UI;

namespace Luxel.Tests;

/// <summary>
/// 設定ストア (タスク 15-B) の GPU/file 非依存テスト: Get/Save 往復、既定値、破損 JSON フォールバック + .bak、
/// Signal 変更が Save に反映、AutoSave。
/// </summary>
public class SettingsStoreTests
{
    [Fact]
    public void Get_ReturnsFallback_WhenMissing_AndSameSignalInstance()
    {
        var files = new InMemoryFileStore();
        var store = SettingsStore.LoadFrom(files, "settings.json");
        Signal<float> vol = store.Get("volume", 0.8f);
        Assert.Equal(0.8f, vol.Value);
        Assert.Same(vol, store.Get("volume", 0.1f));   // 同一インスタンス (2 度目の fallback は無視)
    }

    [Fact]
    public void Save_Then_Load_RoundTrips()
    {
        var files = new InMemoryFileStore();
        var s1 = SettingsStore.LoadFrom(files, "settings.json");
        s1.Get("volume", 0.5f).Value = 0.3f;
        s1.Get("fullscreen", false).Value = true;
        s1.Get("name", "player").Value = "Alice";
        s1.Save();

        var s2 = SettingsStore.LoadFrom(files, "settings.json");
        Assert.Equal(0.3f, s2.Get("volume", 0f).Value);
        Assert.True(s2.Get("fullscreen", false).Value);
        Assert.Equal("Alice", s2.Get("name", "").Value);
    }

    [Fact]
    public void SignalChange_ReflectedIn_Save()
    {
        var files = new InMemoryFileStore();
        var store = SettingsStore.LoadFrom(files, "settings.json");
        Signal<int> quality = store.Get("quality", 1);
        quality.Value = 3;                     // Signal を書き換え
        store.Save();
        Assert.Contains("\"quality\": 3", files.Read("settings.json"));
    }

    [Fact]
    public void AutoSave_PersistsOnEveryChange()
    {
        var files = new InMemoryFileStore();
        var store = SettingsStore.LoadFrom(files, "settings.json");
        store.AutoSave = true;
        store.Get("volume", 0.5f).Value = 0.9f;   // Save() を呼ばずとも保存される

        var reloaded = SettingsStore.LoadFrom(files, "settings.json");
        Assert.Equal(0.9f, reloaded.Get("volume", 0f).Value);
    }

    [Fact]
    public void CorruptFile_FallsBackToDefaults_AndBacksUp()
    {
        var files = new InMemoryFileStore();
        files.Write("settings.json", "{ this is not valid json ]");
        var store = SettingsStore.LoadFrom(files, "settings.json");

        Assert.Equal(0.7f, store.Get("volume", 0.7f).Value);   // 既定値で起動
        Assert.True(files.Exists("settings.json.bak"));         // 破損ファイルを退避
        Assert.Equal("{ this is not valid json ]", files.Read("settings.json.bak"));
    }

    [Fact]
    public void PhysicalFileStore_WritesAndReads()
    {
        string dir = Path.Combine(Path.GetTempPath(), "luxel-settings-test-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var files = new PhysicalFileStore(dir);
            Assert.False(files.Exists("s.json"));
            var store = SettingsStore.LoadFrom(files, "s.json");
            store.Get("v", 1).Value = 42;
            store.Save();
            Assert.True(files.Exists("s.json"));

            var reloaded = SettingsStore.LoadFrom(new PhysicalFileStore(dir), "s.json");
            Assert.Equal(42, reloaded.Get("v", 0).Value);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
