using Luxel.Diagnostics;
using Luxel.DevTools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Luxel.Framework.DevTools;

/// <summary>
/// <see cref="LuxelHostBuilder"/> に DevTools を結線する拡張 (Q05 E)。Gallery の Program.cs で手書き
/// していた定型 (listener 生成 + DebugServer 起動 + EngineCommands 配線 + 内蔵版起動) を 1 行に集約する。
/// </summary>
public static class LuxelHostDevToolsExtensions
{
    /// <summary>
    /// DevTools をオプトインで結線する。<paramref name="options"/> でブラウザ版/内蔵版を選択 (既定は両方 off)。
    /// <para>結線されるもの: <see cref="DevToolsListener"/> (エンジン診断を購読 → 以降 emit が有効化)、
    /// 指定時 <see cref="DebugServer"/> (ブラウザ) / <see cref="DevToolsApp"/> (内蔵ウィンドウ)、
    /// および <see cref="IFramePublisher"/> (提示ループが呼ぶフレーム配信口)。</para>
    /// ライフサイクルは <see cref="IHostedService"/> として host に載るので、<c>host.Start()</c> で起動し
    /// <c>host.StopAsync()</c> で破棄される。
    /// </summary>
    public static LuxelHostBuilder WithDevTools(this LuxelHostBuilder builder, DevToolsOptions options)
    {
        return builder.ConfigureServices(services =>
        {
            services.AddSingleton(options);
            services.AddSingleton<IFramePublisher, FramePublisher>();
            services.AddSingleton<DevToolsRuntime>();
            services.AddHostedService(sp => sp.GetRequiredService<DevToolsRuntime>());
        });
    }

    /// <summary>コマンドライン引数から <see cref="DevToolsOptions.Parse"/> して結線する糖衣。</summary>
    public static LuxelHostBuilder WithDevTools(this LuxelHostBuilder builder, string[] args,
        Func<Luxel.GpuDevice>? nativeDeviceFactory = null)
        => builder.WithDevTools(DevToolsOptions.Parse(args, nativeDeviceFactory));
}

/// <summary>
/// DevTools フロントエンドのライフサイクルを host に載せる hosted service。listener を先に作って
/// エンジン診断を購読させ (これで <see cref="EngineDiagnostics.IsEnabled"/> が true になる)、
/// 選択に応じて DebugServer / DevToolsApp を起動する。
/// </summary>
public sealed class DevToolsRuntime : IHostedService, IDisposable
{
    private readonly EngineCommands _commands;
    private readonly DevToolsOptions _options;
    private DevToolsListener? _listener;
    private DebugServer? _server;
    private DevToolsApp? _native;

    public DevToolsRuntime(EngineCommands commands, DevToolsOptions options)
    {
        _commands = commands;
        _options = options;
    }

    /// <summary>ブラウザ版 DebugServer の URL (起動時のみ、なければ null)。ゲームがログに出すのに使う。</summary>
    public string? BrowserUrl => _server?.Url;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.IsDisabled) return Task.CompletedTask;

        // listener は両フロントエンド共有の読み側ハブ。これを作ると "Luxel" 診断が購読されて emit が有効化される。
        _listener = new DevToolsListener(_commands);

        if (_options.BrowserPort is int port)
        {
            _server = new DebugServer(_listener, port);
            _server.Start();
            Console.WriteLine($"DevTools (browser): {_server.Url}");
        }

        if (_options.Native)
        {
            if (_options.NativeDeviceFactory is null)
                Console.Error.WriteLine("DevTools (native): NativeDeviceFactory 未指定のため内蔵版は起動しません。");
            else
            {
                _native = DevToolsApp.Launch(_options.NativeDeviceFactory, _listener, _commands);
                Console.WriteLine("DevTools (native): ネイティブウィンドウを起動しました。");
            }
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _native?.Dispose(); _native = null;
        _server?.Dispose(); _server = null;
        _listener?.Dispose(); _listener = null;
    }
}
