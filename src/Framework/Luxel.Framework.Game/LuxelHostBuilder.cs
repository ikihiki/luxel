using Luxel.AssetsGpu;
using Luxel.Audio;
using Luxel.Input;
using Luxel.Resources;
using Luxel.Graphics.MessagePipe;
using Luxel.Graphics.RenderSystem;
using Luxel.UI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Luxel.Framework.Game;

/// <summary>
/// Luxel フレームワークの汎用ホストビルダ。<see cref="Microsoft.Extensions.Hosting"/> をベースに、
/// GPU、audio、resources、renderingと一つの<see cref="IGameLoop"/>をDIへ登録し、
/// <see cref="Build"/>で<see cref="IHost"/>を返す。GPU・audio backendはplatform別projectから注入する。
/// </summary>
public sealed class LuxelHostBuilder
{
    private readonly HostApplicationBuilder _inner;
    private Func<GpuDevice>? _deviceFactory;
    private Func<IGpuLifecycleSink, GpuDevice>? _lifecycleDeviceFactory;
    private GpuDevice? _borrowedDevice;
    private Func<CancellationToken, Task>? _frameWaiter;
    private Type? _gameLoop;
    private LuxelRenderingBuilder? _rendering;
    private bool _standardCadences;
    private string? _assetRoot;
    private Func<ResourceSystemBuilder, ResourceSystemDefaultHandles> _resourceCore =
        builder => ResourceSystemDefaults.AddCore(builder);
    private Action<GpuResourceInstallationOptions>? _configureGpuResources;
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

    /// <summary>直接 factory を注入する (テストでモックしたいとき等)。factory が作った device は
    /// host の破棄で Dispose される (所有はコンテナ)。</summary>
    public LuxelHostBuilder UseGpu(Func<GpuDevice> factory) { _deviceFactory = factory; _lifecycleDeviceFactory = null; return this; }

    /// <summary>Creates an owned GPU device with the framework's queued lifecycle sink.</summary>
    public LuxelHostBuilder UseGpu(Func<IGpuLifecycleSink, GpuDevice> factory)
    {
        _lifecycleDeviceFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        _deviceFactory = null;
        return this;
    }

    /// <summary>**借用** device を使う (Storybook 等、ホストの GPU に相乗りする埋め込み実行用)。
    /// インスタンス登録なのでコンテナは Dispose しない — 所有はホスト側のまま。</summary>
    public LuxelHostBuilder UseGpuDevice(GpuDevice device) { _borrowedDevice = device; return this; }

    /// <summary>フレームペーシングを差し替える。埋め込みホストが自分の描画ティックに同期させる場合に使う。</summary>
    public LuxelHostBuilder UseFrameWaiter(Func<CancellationToken, Task> waiter) { _frameWaiter = waiter; return this; }

    /// <summary>Audio backend factory を明示注入する。factory が作った backend は初期化され、host の破棄で Dispose される。</summary>
    public LuxelHostBuilder UseAudio(Func<IAudioBackend> factory)
    {
        _audioFactory = factory ?? throw new ArgumentNullException(nameof(factory));
        _useAudio = true;
        return this;
    }

    /// <summary>Resources system を DI に登録し、<paramref name="assetRoot"/> を base ディレクトリにする。</summary>
    public LuxelHostBuilder UseResources(string? assetRoot = null) { _assetRoot = assetRoot ?? AppContext.BaseDirectory; return this; }

    /// <summary>Overrides the domain/manager foundation used when building the ResourceSystem.</summary>
    public LuxelHostBuilder UseResourceCore(Func<ResourceSystemBuilder, ResourceSystemDefaultHandles> configure)
    {
        _resourceCore = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

    /// <summary>Configures the GPU resource manager and its execution domain.</summary>
    public LuxelHostBuilder ConfigureGpuResources(Action<GpuResourceInstallationOptions> configure)
    {
        _configureGpuResources = configure ?? throw new ArgumentNullException(nameof(configure));
        return this;
    }

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

    /// <summary>Registers the single application game loop.</summary>
    public LuxelHostBuilder AddGameLoop<TLoop>() where TLoop : class, IGameLoop
    {
        RegisterGameLoop(typeof(TLoop));
        return this;
    }

    public LuxelHostBuilder ConfigureRendering(Action<LuxelRenderingBuilder> configure)
        => ConfigureRendering(configure, standardCadences: false);

    internal LuxelHostBuilder ConfigureRendering(
        Action<LuxelRenderingBuilder> configure,
        bool standardCadences)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (standardCadences && _standardCadences)
            throw new InvalidOperationException("UseStandardCadences() can only be called once.");
        _rendering ??= new LuxelRenderingBuilder();
        configure(_rendering);
        if (standardCadences) _standardCadences = true;
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
        if (_deviceFactory is null && _lifecycleDeviceFactory is null && _borrowedDevice is null)
            throw new InvalidOperationException("UseGpu / UseGpuDevice のいずれかを指定してください。");
        if (_gameLoop is null)
            throw new InvalidOperationException("AddGameLoop<TLoop>() を一度だけ指定してください。");

        // Graphics lifecycle callbacks are queued and published through MessagePipe from the frame thread.
        _inner.Services.AddGpuLifecycleMessagePipe();

        // GPU device (singleton)。借用 (UseGpuDevice) はインスタンス登録 = コンテナが Dispose しない
        if (_borrowedDevice is not null) _inner.Services.AddSingleton(_borrowedDevice);
        else if (_lifecycleDeviceFactory is not null)
            _inner.Services.AddSingleton(sp => _lifecycleDeviceFactory(sp.GetRequiredService<IGpuLifecycleSink>()));
        else _inner.Services.AddSingleton(_ => _deviceFactory!());

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
                var builder = new ResourceSystemBuilder();
                ResourceSystemDefaultHandles core = _resourceCore(builder);
                ResourceSystemDefaults.AddBuiltinSources(builder, core, assetRoot: _assetRoot);
                ResourceSystemDefaults.AddBuiltinSteps(builder, core);
                AssetGpuResourceSystemRegistration gpu = builder.AddAssetGpu(
                    sp.GetRequiredService<GpuDevice>(), _configureGpuResources);
                return new FrameworkGpuResources(builder.Build(), gpu.Gpu);
            });
            _inner.Services.AddSingleton(sp => sp.GetRequiredService<FrameworkGpuResources>().Resources);
            _inner.Services.AddSingleton(sp => sp.GetRequiredService<FrameworkGpuResources>().Gpu);
            _inner.Services.AddSingleton(sp => new GpuDeviceLifecycleCoordinator(
                sp.GetRequiredService<ResourceSystem>(),
                sp.GetRequiredService<GpuResourceManagerHandle>(),
                new GpuDeviceLifecycleCoordinatorOptions
                {
                    Ownership = _borrowedDevice is null ? GpuDeviceOwnership.Owned : GpuDeviceOwnership.Borrowed,
                    OwnedDeviceFactory = _borrowedDevice is null
                        ? (_, sink, _) => ValueTask.FromResult(_lifecycleDeviceFactory is not null
                            ? _lifecycleDeviceFactory(sink)
                            : _deviceFactory!())
                        : null,
                }));
        }

        // Audio
        if (_useAudio)
        {
            _inner.Services.AddSingleton<IAudioBackend>(_ =>
            {
                IAudioBackend backend = _audioFactory!.Invoke();
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

        _inner.Services.AddSingleton<IGameLoopIterationPump, EngineCommandIterationPump>();
        _inner.Services.AddSingleton<IGameLoopIterationPump, GpuLifecycleIterationPump>();

        RenderSystemConfiguration rendering = (_rendering ?? new LuxelRenderingBuilder()).Build();
        _inner.Services.AddSingleton(rendering);
        _inner.Services.TryAddSingleton<RenderFeatureSetStateRegistry>();
        _inner.Services.TryAddSingleton<RenderManualTriggerRegistry>();
        _inner.Services.TryAddSingleton<IRenderFrameScheduler>(
            _ => new DelegateRenderFrameScheduler(_frameWaiter));
        _inner.Services.TryAddSingleton<IGameSceneBootstrap, EmptyGameSceneBootstrap>();
        _inner.Services.TryAddSingleton<IGameSceneSystem>(sp =>
            new GameSceneSystem(sp, sp.GetRequiredService<RenderSystemConfiguration>()));
        _inner.Services.TryAddSingleton<RenderGraphCadenceRunner>();
        _inner.Services.TryAddSingleton<ICadenceExecutionCoordinator>(sp =>
        {
            var runners = new Dictionary<RenderCadenceRunnerId, IRenderCadenceRunner>
            {
                [RenderCadenceRunners.RenderGraph] = sp.GetRequiredService<RenderGraphCadenceRunner>(),
            };
            if (sp.GetService<IPresentationScheduler>() is { } scheduler)
                runners[RenderCadenceRunners.Presentation] = new PresentationRunner(
                    sp.GetRequiredService<GpuDevice>(), scheduler);
            return new CadenceExecutionCoordinator(
                rendering.Cadences.Items,
                rendering.FeatureSets.Order.ToArray(),
                runners,
                sp.GetRequiredService<RenderFeatureSetStateRegistry>(),
                sp.GetRequiredService<RenderManualTriggerRegistry>());
        });

        // Bind the concrete loop and IGameLoop to the same singleton instance.
        _inner.Services.AddSingleton(_gameLoop!);
        _inner.Services.AddSingleton(typeof(IGameLoop), sp => sp.GetRequiredService(_gameLoop!));
        _inner.Services.AddHostedService<GameLoopHostedService>();

        return _inner.Build();
    }

    private void RegisterGameLoop(Type gameLoop)
    {
        ArgumentNullException.ThrowIfNull(gameLoop);
        if (_gameLoop is not null)
            throw new InvalidOperationException("Game loop は一つだけ登録できます。");
        _gameLoop = gameLoop;
    }

    private sealed record FrameworkGpuResources(ResourceSystem Resources, GpuResourceManagerHandle Gpu);
}
