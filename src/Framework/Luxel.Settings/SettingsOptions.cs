using Luxel.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Luxel.Settings;

/// <summary>
/// <see cref="SettingsStore"/> の <see cref="Signal{T}"/> を <see cref="Microsoft.Extensions.Options"/> の
/// <see cref="IOptions{T}"/> / <see cref="IOptionsMonitor{T}"/> に橋渡しするアダプタ。
/// DI から <c>IOptions&lt;GraphicsSettings&gt;</c> 等で設定値を注入でき、値変更 (Signal) が
/// <see cref="IOptionsMonitor{T}.OnChange"/> に伝播する。書き込みたい側は <see cref="Signal{T}"/> を直接注入する。
/// </summary>
public sealed class SignalOptionsMonitor<T> : IOptionsMonitor<T>, IOptions<T> where T : class
{
    private readonly Signal<T> _signal;

    public SignalOptionsMonitor(Signal<T> signal) => _signal = signal;

    /// <summary>現在の設定値 (依存追跡なしで読む)。</summary>
    public T CurrentValue => _signal.Peek();

    /// <summary><see cref="IOptions{T}.Value"/> — 現在値。</summary>
    public T Value => _signal.Peek();

    /// <summary>名前付きオプションは非対応 (単一インスタンス)。name によらず現在値を返す。</summary>
    public T Get(string? name) => _signal.Peek();

    /// <summary>値が変わったら <paramref name="listener"/> を呼ぶ。購読直後は呼ばない (変化時のみ)。
    /// Dispose で解除。</summary>
    public IDisposable? OnChange(Action<T, string?> listener)
    {
        bool first = true;
        return Reactive.Effect(() =>
        {
            T v = _signal.Value;    // 依存追跡 (変化で再実行)
            if (first) { first = false; return; }   // 購読時の初回は通知しない
            listener(v, Options.DefaultName);
        });
    }
}

/// <summary>設定 (<see cref="SettingsStore"/> + Options) の DI 登録拡張。</summary>
public static class SettingsServiceCollectionExtensions
{
    /// <summary><see cref="SettingsStore"/> を singleton 登録する (指定 <see cref="IFileStore"/> のファイルから読み込み)。
    /// 環境変数などの .NET 標準 config を効かせたい場合は <see cref="AddLuxelSettings(IServiceCollection, IConfiguration, IFileStore, string)"/> を使う。</summary>
    public static IServiceCollection AddLuxelSettings(
        this IServiceCollection services, IFileStore files, string fileName = "settings.json")
    {
        services.AddSingleton(_ => SettingsStore.LoadFrom(files, fileName));
        return services;
    }

    /// <summary><see cref="SettingsStore"/> を singleton 登録する。**読み込みは .NET 標準の <see cref="IConfiguration"/>**
    /// (JSON + 環境変数 + cmdline)、書き込みは <paramref name="writeStore"/>。<see cref="LuxelConfiguration.Build"/> や
    /// ホストの <c>builder.Configuration</c> を <paramref name="config"/> に渡す。</summary>
    public static IServiceCollection AddLuxelSettings(
        this IServiceCollection services, IConfiguration config, IFileStore writeStore, string fileName = "settings.json")
    {
        services.AddSingleton(_ => SettingsStore.LoadFrom(config, writeStore, fileName));
        return services;
    }

    /// <summary>
    /// 設定セクション <paramref name="key"/> を POCO <typeparamref name="T"/> に束ね、
    /// <see cref="IOptions{T}"/> / <see cref="IOptionsMonitor{T}"/> と、書き込み用の <see cref="Signal{T}"/> を DI に登録する。
    /// 事前に <see cref="AddLuxelSettings"/> で <see cref="SettingsStore"/> を登録しておくこと。
    /// </summary>
    public static IServiceCollection AddSettingsOptions<T>(
        this IServiceCollection services, string key, T? fallback = null) where T : class, new()
    {
        // 書き込み用: SettingsStore から key の Signal<T> (同一インスタンスがキャッシュされる)
        services.AddSingleton(sp => sp.GetRequiredService<SettingsStore>().Get(key, fallback ?? new T()));
        // 読み取り用: Signal を Options に橋渡し
        services.AddSingleton(sp => new SignalOptionsMonitor<T>(sp.GetRequiredService<Signal<T>>()));
        services.AddSingleton<IOptionsMonitor<T>>(sp => sp.GetRequiredService<SignalOptionsMonitor<T>>());
        services.AddSingleton<IOptions<T>>(sp => sp.GetRequiredService<SignalOptionsMonitor<T>>());
        return services;
    }
}
