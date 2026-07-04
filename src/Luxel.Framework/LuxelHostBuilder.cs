using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Luxel.Audio;
using Luxel.AssetsGpu;
using Luxel.Input;
using Luxel.Resources;
using Luxel.UI;

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
    private Type? _startupScene;
    private string? _assetRoot;
    private bool _useAudio;

    private LuxelHostBuilder(string[]? args)
    {
        _inner = Host.CreateApplicationBuilder(args ?? Array.Empty<string>());
    }

    /// <summary>Host builder を作る。args は Microsoft.Extensions.Hosting の設定 (env, config) にそのまま渡る。</summary>
    public static LuxelHostBuilder Create(string[]? args = null) => new(args);

    /// <summary>Vulkan バックエンドを使う。<see cref="GpuDevice"/> を DI に singleton 登録する factory を保持。</summary>
    public LuxelHostBuilder UseVulkan()
    {
        _deviceFactory = () => new GpuDevice(Luxel.Vulkan.VulkanBackend.Create());
        return this;
    }

    /// <summary>D3D12 バックエンドを使う。</summary>
    public LuxelHostBuilder UseD3D12()
    {
        _deviceFactory = () => new GpuDevice(Luxel.D3D12.D3D12Backend.Create());
        return this;
    }

    /// <summary>直接 factory を注入する (テストでモックしたいとき等)。</summary>
    public LuxelHostBuilder UseGpu(Func<GpuDevice> factory) { _deviceFactory = factory; return this; }

    /// <summary>Audio (XAudio2Backend) を DI に登録する。未指定なら audio system は起動しない。</summary>
    public LuxelHostBuilder UseAudio() { _useAudio = true; return this; }

    /// <summary>Resources system を DI に登録し、<paramref name="assetRoot"/> を base ディレクトリにする。</summary>
    public LuxelHostBuilder UseResources(string? assetRoot = null) { _assetRoot = assetRoot ?? AppContext.BaseDirectory; return this; }

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

    public IHost Build()
    {
        if (_deviceFactory is null) throw new InvalidOperationException("UseVulkan / UseD3D12 / UseGpu のいずれかを指定してください。");

        // GPU device (singleton)
        _inner.Services.AddSingleton(sp => _deviceFactory());

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
                    steps:   ResourceSystemDefaults.BuiltinSteps());
                res.InstallAssetGpu(device);  // GPU Step 一式を追加登録
                return res;
            });
        }

        // Audio
        if (_useAudio)
        {
            _inner.Services.AddSingleton<IAudioBackend>(sp =>
            {
                var xa2 = new Luxel.Platform.XAudio2Backend();
                xa2.Initialize();
                return xa2;
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
            UiRegistry: sp.GetService<UiRegistry>()));

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
