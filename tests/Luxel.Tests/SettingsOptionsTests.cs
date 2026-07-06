using Luxel.Settings;
using Luxel.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Luxel.Tests;

/// <summary>
/// 設定の Microsoft.Extensions.Options / DI 対応 (タスク 15-B 拡張) のテスト:
/// IOptions&lt;T&gt; / IOptionsMonitor&lt;T&gt; で設定値を注入でき、Signal 変更が反映・OnChange 通知される。
/// </summary>
public class SettingsOptionsTests
{
    public sealed class GraphicsSettings
    {
        public int Quality { get; set; } = 1;
        public bool Vsync { get; set; } = true;
        public float RenderScale { get; set; } = 1.0f;
    }

    [Fact]
    public void IOptions_ResolvesFromStore_WithSavedValues()
    {
        var files = new InMemoryFileStore();
        files.Write("settings.json", "{ \"graphics\": { \"Quality\": 3, \"Vsync\": false, \"RenderScale\": 0.75 } }");

        var services = new ServiceCollection();
        services.AddLuxelSettings(files, "settings.json");
        services.AddSettingsOptions<GraphicsSettings>("graphics");
        using var sp = services.BuildServiceProvider();

        GraphicsSettings g = sp.GetRequiredService<IOptions<GraphicsSettings>>().Value;
        Assert.Equal(3, g.Quality);
        Assert.False(g.Vsync);
        Assert.Equal(0.75f, g.RenderScale);
    }

    [Fact]
    public void IOptions_UsesFallback_WhenSectionMissing()
    {
        var services = new ServiceCollection();
        services.AddLuxelSettings(new InMemoryFileStore(), "settings.json");
        services.AddSettingsOptions("graphics", new GraphicsSettings { Quality = 5 });
        using var sp = services.BuildServiceProvider();

        Assert.Equal(5, sp.GetRequiredService<IOptions<GraphicsSettings>>().Value.Quality);
    }

    [Fact]
    public void IOptionsMonitor_ReflectsSignalWrites_AndFiresOnChange()
    {
        var services = new ServiceCollection();
        services.AddLuxelSettings(new InMemoryFileStore(), "settings.json");
        services.AddSettingsOptions("graphics", new GraphicsSettings());
        using var sp = services.BuildServiceProvider();

        var monitor = sp.GetRequiredService<IOptionsMonitor<GraphicsSettings>>();
        var writable = sp.GetRequiredService<Signal<GraphicsSettings>>();   // 書き込み用の同じ Signal

        Assert.Equal(1, monitor.CurrentValue.Quality);   // 既定

        int changes = 0;
        GraphicsSettings? last = null;
        using IDisposable? sub = monitor.OnChange((v, _) => { changes++; last = v; });

        writable.Value = new GraphicsSettings { Quality = 4, Vsync = false };   // 新インスタンスで変更通知
        Assert.Equal(1, changes);                     // 購読後の変更で 1 回 (購読直後は呼ばれない)
        Assert.Equal(4, last!.Quality);
        Assert.Equal(4, monitor.CurrentValue.Quality); // CurrentValue も追従
    }

    [Fact]
    public void Writes_Persist_ViaStore_Save()
    {
        var files = new InMemoryFileStore();
        var services = new ServiceCollection();
        services.AddLuxelSettings(files, "settings.json");
        services.AddSettingsOptions("graphics", new GraphicsSettings());
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<Signal<GraphicsSettings>>().Value = new GraphicsSettings { Quality = 9 };
        sp.GetRequiredService<SettingsStore>().Save();

        // 別ストアで読み直しても残る
        var reloaded = SettingsStore.LoadFrom(files, "settings.json");
        Assert.Equal(9, reloaded.Get("graphics", new GraphicsSettings()).Value.Quality);
    }
}
