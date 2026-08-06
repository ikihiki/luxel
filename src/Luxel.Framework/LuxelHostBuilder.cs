using Luxel.AssetsGpu;
using Luxel.Audio;
using Luxel.Input;
using Luxel.Resources;
using Luxel.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Luxel.Framework;

/// <summary>
/// Luxel フレームワークの汎用ホストビルダ。<see cref="Microsoft.Extensions.Hosting"/> をベースに、
/// <c>UseVulkan / UseD3D12 / UseAudio / UseResources / AddScene</c> といったチェーン API で
/// engine のサブシステムを DI に登録し、<see cref="Build"/> で <see cref="IHost"/> を返す。
///
/// <code>
/// var host = LuxelHost.CreateBuilder(args)
///     .UseVulkan()
///     .UseAudio()
///     .UseResources("./assets")
///     .AddScene&lt;MainScene&gt;()
///     .Build();
/// await host.RunAsync();
/// </code>
///
/// <see cref="AddScene{T}"/> は初期 scene を指定 (StartupScene として singleton 登録)。
/// 実行時の scene 切替は <see cref="SceneManager"/> を DI 経由で取得して <c>SwitchAsync&lt;NextScene&gt;()</c>。
/// </summary>
public sealed class LuxelHostBuilder
{
    private readonly HostApplicationBuilder _inner;
    private Func<GpuDevice>? _deviceFactory;
    private GpuDevice? _borrowedDevice;
    private Func<CancellationToken, Task>? _frameWaiter;
    private Type? _startupScene;
    private string? _assetRoot;
    private bool _useAudio;
    private Func<IAudioBackend>? _audioFactory;
    private Luxel.Settings.IFileStore? _settingsFiles;
    private string _settingsFileName = "settings.json";
    private string? _settingsEnvPrefix;
    private readonly string[] _args;

    private LuxelHostBuilder(string[]? args)
    {
        _args = args ?? Array.Empty<string>();
        _inner = Host.CreateApplicationBuilder(_args);
    }

    /// <summary>Host builder を作る。args は Microsoft.Extensions.Hosting の設定 (env, config) にそのまま渡る。</summary>
    public static LuxelHostBuilder Create(string[]? args = null) => new(args);

    /// <summary>Vulkan バックエンドを使う。<see cref="GpuDevice"/> を DI に singleton 登録する factory を保持。</summary>
    public LuxelHostBuilder UseVulkan()
    {
        _deviceFactory = () => new GpuDevice(Luxel.Graphics.Vulkan.VulkanBackend.Create());
        return this;
    }

    /// <summary>D3D12 バックエンドを使う。</summary>
    public LuxelHostBuilder UseD3D12()
    {
        _deviceFactory = () => new GpuDevice(Luxel.Graphics.DirectX12.D3D12Backend.Create());
        return this;
    }

    /// <summary>直接 factory を注入する (テストでモックしたいとき等)。factory が作った device は
    /// host の破棄で Dispose される (所有はコンテナ)。</summary>
    public LuxelHostBuilder UseGpu(Func<GpuDevice> factory) { _deviceFactory = factory; return this; }

    /// <summary>**借用** device を使う (Storybook 等、ホストの GPU に相乗りする埋め込み実行用)。
    /// インスタンス登録なのでコンテナは Dispose しない — 所有はホスト側のまま。</summary>
    public LuxelHostBuilder UseGpuDevice(GpuDevice device) { _borrowedDevice = device; return this; }

    /// <summary>フレームペーシングを差し替える (Platform 部分の抽象 — 既定は固定ディレイ)。
    /// Storybook 等の埋め込みホストが自分の描画ティックに同期させるのに使う。
    /// 詳細は <see cref="SceneLoopServices.WaitFrame"/>。</summary>
    public LuxelHostBuilder UseFrameWaiter(Func<CancellationToken, Task> waiter) { _frameWaiter = waiter; return this; }

    /// <summary>現在の OS に対応する audio backend を DI に登録する。Windows は XAudio2、
    /// Linux/macOS は Silk.NET 経由の OpenAL Soft。未指定なら audio system は起動しない。</summary>
    public LuxelHostBuilder UseAudio() { _useAudio = true; _audioFactory = null; return this; }

    /// <summary>Audio backend factory を明示注入する。factory が作った backend は初期化され、host の破棄で Dispose される。</summary>
    public LuxelHostBuilder UseAudio(Func<IAudioBackend> factory)
    {
        _audioFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        _useAudio = true;
        return this;
    }

    /// <summary>Resources system を DI に登録し、<paramref name="assetRoot"/> を base ディレクトリにする。</summary>
    public LuxelHostBuilder UseResources(string? assetRoot = null) { _assetRoot = assetRoot ?? AppContext.BaseDirectory; return this; }

    /// <summary>設定ストア (<see cref="Luxel.Settings.SettingsStore"/>) を DI に登録する。保存先は
    /// <c>%APPDATA%/<paramref name="appName"/></c> (実ファイル)。**読み込みは .NET 標準 config** —
    /// 保存済み JSON &lt; 環境変数 (<paramref name="envPrefix"/> 付き) &lt; コマンドライン の順で上書きされる。
    /// 後は <c>ConfigureServices</c> で <c>services.AddSettingsOptions&lt;T&gt;("key")</c> すると
    /// <c>IOptions&lt;T&gt;</c>/<c>IOptionsMonitor&lt;T&gt;</c> で注入できる。</summary>
    public LuxelHostBuilder WithSettings(string appName, string fileName = "settings.json", string? envPrefix = null)
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName);
        _settingsFiles = new Luxel.Settings.PhysicalFileStore(root);
        _settingsFileName = fileName;
        _settingsEnvPrefix = envPrefix;
        return this;
    }

    /// <summary>設定ストアを指定 <see cref="Luxel.Settings.IFileStore"/> で登録する (テストのインメモリ等)。
    /// 読み込みは同様に .NET 標準 config (ファイル &lt; 環境変数 &lt; cmdline)。</summary>
    public LuxelHostBuilder WithSettings(Luxel.Settings.IFileStore files, string fileName = "settings.json", string? envPrefix = null)
    {
        _settingsFiles = files;
        _settingsFileName = fileName;
        _settingsEnvPrefix = envPrefix;
        return this;
    }

    /// <summary>起動時に自動 Load する scene 型。SceneManager が最初に SwitchAsync{T}() する。</summary>
    public LuxelHostBuilder AddScene<TScene>() where TScene : GameScene
    {
        _startupScene = typeof(TScene);
        _inner.Services.TryAddTransient<TScene>();
        return this;
    }

    /// <summary>DI に追加サービスを登録するためのフック。</summary>
    public LuxelHostBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        configure(_inner.Services);
        return this;
    }

    internal static IAudioBackend CreatePlatformAudioBackend()
    {
        if (OperatingSystem.IsWindows()) return new Luxel.Audio.Windows.XAudio2Backend();
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) return new Luxel.Audio.Silk.OpenAlAudioBackend();
        throw new PlatformNotSupportedException("UseAudio() supports Windows (XAudio2) and Linux/macOS (OpenAL Soft via Silk.NET). Use UseAudio(factory) for another platform.");
    }

    public IHost Build()
    {
        if (_deviceFactory is null && _borrowedDevice is null)
            throw new InvalidOperationException("UseVulkan / UseD3D12 / UseGpu / UseGpuDevice のいずれかを指定してください。");

        // GPU device (singleton)。借用 (UseGpuDevice) はインスタンス登録 = コンテナが Dispose しない
        if (_borrowedDevice is not null) _inner.Services.AddSingleton(_borrowedDevice);
        else _inner.Services.AddSingleton(sp => _deviceFactory!());

        // ECS world
        _inner.Services.AddSingleton<Luxel.Ecs.World>();

        // EngineCommands (DevTools → engine の操作経路。Scene が engine.pause/resume/step を Register する)
        _inner.Services.AddSingleton<Luxel.Diagnostics.EngineCommands>();

        // Input (常時 available)
        _inner.Services.AddSingleton<InputBus>();
        _inner.Services.AddSingleton<InputStack>();

        // Resources
        if (_assetRoot is not null)
        {
            _inner.Services.AddSingleton(sp =>
            {
                var device = sp.GetRequiredService<GpuDevice>();
                var res = new ResourceSystem(
                    sources: ResourceSystemDefaults.BuiltinSources(assetRoot: _assetRoot),
                    steps: ResourceSystemDefaults.BuiltinSteps());
                res.InstallAssetGpu(device);  // GPU Step 一式を追加登録
                return res;
            });
        }

        // Audio
        if (_useAudio)
        {
            _inner.Services.AddSingleton<IAudioBackend>(_ =>
            {
                IAudioBackend backend = _audioFactory?.Invoke() ?? CreatePlatformAudioBackend();
                backend.Initialize();
                return backend;
            });
            _inner.Services.AddSingleton<AudioMixer>();
        }
        // AudioRegistry は audio 有無に関わらず提供 (Bus のみ使いたい場合もあり)
        _inner.Services.AddSingleton<AudioRegistry>(sp => new AudioRegistry
        {
            Mixer = sp.GetService<AudioMixer>(),
        });

        // UiRegistry — 複数 UiHost を DevTools が一括で見られるよう登録簿を提供
        _inner.Services.AddSingleton<UiRegistry>();

        // 設定ストア (指定時) — 読み込みは .NET 標準 config (ファイル < 環境変数 < cmdline)。
        // 書き込みは指定 IFileStore。値は ConfigureServices の AddSettingsOptions<T> で IOptions 化できる。
        if (_settingsFiles is not null)
        {
            var settingsConfig = Luxel.Settings.LuxelConfiguration.Build(
                _settingsFiles, _settingsFileName, _settingsEnvPrefix, _args);
            Luxel.Settings.SettingsServiceCollectionExtensions.AddLuxelSettings(
                _inner.Services, settingsConfig, _settingsFiles, _settingsFileName);
        }

        // SceneLoopServices — Scene の ctor に渡す (device / resources / audio / input の束)
        _inner.Services.AddSingleton(sp => new SceneLoopServices(
            Device: sp.GetRequiredService<GpuDevice>(),
            Resources: sp.GetService<ResourceSystem>(),
            Mixer: sp.GetService<AudioMixer>(),
            InputBus: sp.GetService<InputBus>(),
            InputStack: sp.GetService<InputStack>(),
            InputSources: sp.GetServices<IInputSource>().ToArray(),
            Commands: sp.GetService<Luxel.Diagnostics.EngineCommands>(),
            AudioRegistry: sp.GetService<AudioRegistry>(),
            UiRegistry: sp.GetService<UiRegistry>(),
            WaitFrame: _frameWaiter));

        // SceneManager (singleton)
        _inner.Services.AddSingleton<SceneManager>();

        // Startup scene 情報を DI に (GameLoop が読む)
        if (_startupScene is not null)
            _inner.Services.AddSingleton(new StartupScene(_startupScene));

        // GameLoop 本体 (BackgroundService として登録、Host.RunAsync で自動起動)
        _inner.Services.AddHostedService<GameLoop>();

        return _inner.Build();
    }
}

/// <summary>DI 経由で GameLoop に渡す起動 scene の型情報。</summary>
public sealed record StartupScene(Type SceneType);
