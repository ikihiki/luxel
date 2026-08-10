using System.Text.Json;
using Luxel.UI;
using Microsoft.Extensions.Configuration;

namespace Luxel.Settings;

/// <summary>
/// アプリ設定 (音量・画質・キーバインド等) の永続ストア。<see cref="Get{T}"/> が <see cref="Signal{T}"/> を返すのが肝 —
/// 設定画面のコントロール (Slider/Switch/Select) に直結でき、購読側 (音量 → AudioMixer など) も
/// <c>Reactive.Effect</c> で反応できる。値の変更は <see cref="Signal{T}"/> 経由で自動的に内部状態へ反映され、
/// <see cref="Save"/> (または <see cref="AutoSave"/>) でファイルへ書き出す。
///
/// <para><b>ファイル IO 非依存</b>: <see cref="IFileStore"/> 注入でテストはインメモリに。
/// <b>壊れたファイル耐性</b>: パース失敗時は既定値で起動し、破損ファイルを <c>.bak</c> に退避する
/// (設定破損でゲームが起動しないのを防ぐ)。</para>
/// </summary>
public sealed class SettingsStore
{
    private readonly IFileStore _files;                            // 書き込み先 (永続化)
    private readonly IConfiguration? _config;                      // 読み込み源 (.NET 標準: JSON + 環境変数 + cmdline)
    private readonly string _name;
    private readonly Dictionary<string, string> _values = new();   // key → 生 JSON テキスト (書き込み側の現在値)
    private readonly Dictionary<string, object> _signals = new();  // key → Signal<T> (boxed)
    private readonly List<IDisposable> _effects = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true };

    /// <summary>true なら値変更のたび即 <see cref="Save"/> する (既定 false = 明示保存)。</summary>
    public bool AutoSave { get; set; }

    private SettingsStore(IFileStore files, string name, IConfiguration? config = null)
    {
        _files = files;
        _name = name;
        _config = config;
    }

    /// <summary>ファイルから設定を読み込んでストアを作る。破損 JSON は既定値で起動 + <c>.bak</c> 退避。</summary>
    public static SettingsStore LoadFrom(IFileStore files, string name)
    {
        var store = new SettingsStore(files, name);
        string? text = files.Exists(name) ? files.Read(name) : null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(text);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                        store._values[p.Name] = p.Value.GetRawText();   // 自己完結の文字列コピー
            }
            catch (JsonException)
            {
                // 破損: 既定値で起動し、壊れたファイルを .bak に退避して上書きに備える
                files.Write(name + ".bak", text!);
                store._values.Clear();
            }
        }
        return store;
    }

    /// <summary>
    /// **読み込みは .NET 標準の <see cref="IConfiguration"/>** から行う (JSON ファイル + 環境変数 + コマンドライン
    /// のレイヤ — 後のプロバイダが優先)。<paramref name="config"/> が読み口、<paramref name="writeStore"/> が
    /// <see cref="Save"/> の書き込み先。環境変数などの上書きは config 側で解決され、UI からの変更はファイルへ書き戻る。
    /// <para><see cref="LuxelConfiguration.Build"/> で標準 config を組める。ホストでは
    /// <c>LuxelHostBuilder.WithSettings</c> が host の <see cref="IConfiguration"/> (appsettings+env+cmdline) を使う。</para>
    /// </summary>
    public static SettingsStore LoadFrom(IConfiguration config, IFileStore writeStore, string fileName = "settings.json")
        => new(writeStore, fileName, config);

    /// <summary>キーの初期値を決める: config があれば .NET 標準 config (env/cmdline 込み) から束縛、
    /// 無ければ読み込んだファイル値、どちらも無ければ <paramref name="fallback"/>。</summary>
    private T ReadInitial<T>(string key, T fallback)
    {
        if (_config is not null)
        {
            IConfigurationSection section = _config.GetSection(key);
            if (!section.Exists()) return fallback;
            try { return section.Get<T>() ?? fallback; }
            catch { return fallback; }   // 束縛失敗 (型不一致等) は既定へ
        }
        if (_values.TryGetValue(key, out string? raw))
        {
            try { return JsonSerializer.Deserialize<T>(raw, JsonOpts) ?? fallback; }
            catch (JsonException) { return fallback; }
        }
        return fallback;
    }

    /// <summary>キーに対応する <see cref="Signal{T}"/> を取得 (初回は登録、以降は同一インスタンス)。
    /// 保存済みの値があればそれで、無ければ <paramref name="fallback"/> で初期化する。
    /// 値変更は自動で内部状態に反映され、<see cref="AutoSave"/> なら即保存される。</summary>
    public Signal<T> Get<T>(string key, T fallback)
    {
        if (_signals.TryGetValue(key, out object? existing)) return (Signal<T>)existing;

        var sig = new Signal<T>(ReadInitial(key, fallback));
        // 値を読む effect: 初回で内部状態を初期化、以降は変更のたび追従 (+ AutoSave)
        _effects.Add(Reactive.Effect(() =>
        {
            T v = sig.Value;
            _values[key] = JsonSerializer.Serialize(v, JsonOpts);
            if (AutoSave) SaveInternal();
        }));
        _signals[key] = sig;
        return sig;
    }

    /// <summary>現在の全設定をファイルへ書き出す。</summary>
    public void Save() => SaveInternal();

    private void SaveInternal()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            foreach (var kv in _values)
            {
                w.WritePropertyName(kv.Key);
                w.WriteRawValue(kv.Value);   // 生 JSON をそのまま値として埋める
            }
            w.WriteEndObject();
        }
        _files.Write(_name, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
    }
}
