using Luxel.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Luxel.Tests;

/// <summary>
/// 設定の読み込みを .NET 標準 <see cref="IConfiguration"/> (JSON + 環境変数 + コマンドライン) にした対応のテスト。
/// レイヤ優先順 (ファイル &lt; 環境変数 &lt; cmdline) と、SettingsStore/Options がそれを読むことを確認。
/// </summary>
public class SettingsConfigTests
{
    public sealed class Graphics
    {
        public int Quality { get; set; } = 1;
        public bool Vsync { get; set; } = true;
    }

    [Fact]
    public void Store_ReadsInitialValues_FromConfiguration()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["graphics:Quality"] = "3",
                ["graphics:Vsync"] = "false",
                ["volume"] = "0.42",
            })
            .Build();

        var store = SettingsStore.LoadFrom(config, new InMemoryFileStore(), "settings.json");
        Graphics g = store.Get("graphics", new Graphics()).Value;
        Assert.Equal(3, g.Quality);
        Assert.False(g.Vsync);
        Assert.Equal(0.42f, store.Get("volume", 0f).Value);   // スカラーも束縛できる
    }

    [Fact]
    public void CommandLine_Overrides_JsonFile()
    {
        var files = new InMemoryFileStore();
        files.Write("settings.json", "{ \"graphics\": { \"Quality\": 1 } }");
        // cmdline が JSON を上書き (後のプロバイダが勝つ)
        IConfiguration config = LuxelConfiguration.Build(
            files, "settings.json", envPrefix: null, commandLineArgs: new[] { "--graphics:Quality=5" });

        var store = SettingsStore.LoadFrom(config, files, "settings.json");
        Assert.Equal(5, store.Get("graphics", new Graphics()).Value.Quality);
    }

    [Fact]
    public void EnvironmentVariable_Overrides_JsonFile()
    {
        // 一意プレフィックスでプロセス env を汚さないよう分離
        string prefix = "LUXTEST" + System.Guid.NewGuid().ToString("N")[..8] + "_";
        System.Environment.SetEnvironmentVariable(prefix + "graphics__Quality", "7");
        try
        {
            var files = new InMemoryFileStore();
            files.Write("settings.json", "{ \"graphics\": { \"Quality\": 1 } }");
            IConfiguration config = LuxelConfiguration.Build(files, "settings.json", envPrefix: prefix);

            var store = SettingsStore.LoadFrom(config, files, "settings.json");
            Assert.Equal(7, store.Get("graphics", new Graphics()).Value.Quality);   // 環境変数が勝つ
        }
        finally { System.Environment.SetEnvironmentVariable(prefix + "graphics__Quality", null); }
    }

    [Fact]
    public void Precedence_Env_Beats_File_But_CmdLine_Beats_Env()
    {
        string prefix = "LUXTEST" + System.Guid.NewGuid().ToString("N")[..8] + "_";
        System.Environment.SetEnvironmentVariable(prefix + "volume", "0.5");
        try
        {
            var files = new InMemoryFileStore();
            files.Write("settings.json", "{ \"volume\": 0.1 }");
            IConfiguration config = LuxelConfiguration.Build(
                files, "settings.json", envPrefix: prefix, commandLineArgs: new[] { "--volume=0.9" });

            var store = SettingsStore.LoadFrom(config, files, "settings.json");
            Assert.Equal(0.9f, store.Get("volume", 0f).Value);   // cmdline > env > file
        }
        finally { System.Environment.SetEnvironmentVariable(prefix + "volume", null); }
    }

    [Fact]
    public void IOptions_FromConfiguration_ViaDI()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["graphics:Quality"] = "8" })
            .Build();

        var services = new ServiceCollection();
        services.AddLuxelSettings(config, new InMemoryFileStore(), "settings.json");
        services.AddSettingsOptions("graphics", new Graphics());
        using var sp = services.BuildServiceProvider();

        Assert.Equal(8, sp.GetRequiredService<IOptions<Graphics>>().Value.Quality);
    }

    [Fact]
    public void MissingSection_UsesFallback()
    {
        IConfiguration config = new ConfigurationBuilder().Build();   // 空
        var store = SettingsStore.LoadFrom(config, new InMemoryFileStore(), "settings.json");
        Assert.Equal(2, store.Get("graphics", new Graphics { Quality = 2 }).Value.Quality);
    }

    [Fact]
    public void Writes_StillPersist_ToWriteStore()
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["volume"] = "0.3" }).Build();
        var files = new InMemoryFileStore();
        var store = SettingsStore.LoadFrom(config, files, "settings.json");

        Assert.Equal(0.3f, store.Get("volume", 0f).Value);
        store.Get("volume", 0f).Value = 0.8f;   // UI で変更
        store.Save();
        Assert.Contains("0.8", files.Read("settings.json"));   // 書き込み先へ永続化
    }
}
