using System.Text.Json;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>
/// ギャラリー (Storybook 風カタログ) のストーリー定義。
/// <c>[StoryMeta("Controls/Button")]</c> をクラスに、<c>[Story]</c> を static メソッドに付けると
/// ソースジェネレーターが収集して <see cref="StoryRegistry"/> に登録する (reflection なし)。
/// 署名は <c>static StoryResult M()</c> または <c>static StoryResult M(StoryContext ctx)</c>。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class Story : Attribute
{
    /// <summary>true = 実ウィンドウ専用 (音声再生・実デバイス入力など)。snap 回帰は SKIP し、
    /// Gallery アプリでは通常どおり表示される。golden は作らない。</summary>
    public bool RealWindowOnly { get; set; }
    /// <summary>Optional canonical route override. When omitted, <c>StoryMeta/MethodName</c> is used.</summary>
    public string? Path { get; set; }
    /// <summary>Human-readable deterministic fixture/capability note exported with runtime descriptors.</summary>
    public string? CapabilityNote { get; set; }
    /// <summary>Whether this authored story exposes an Args panel. Set to <see langword="false"/> to suppress inherited generated Args.</summary>
    public bool ArgsEnabled { get; set; } = true;
    /// <summary>Optional static schema provider method on the declaring story type.</summary>
    public string? Args { get; set; }

    /// <summary>Embeds another registered story in a Markdown story result.</summary>
    public static StoryReference StoryRef(string path, bool knobs = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new StoryReference(path, StoryArgs.Empty, knobs);
    }

    /// <summary>Markdown story のこの位置へ H2/H3 の目次を埋め込む。</summary>
    public static StoryToc Toc() => default;
}

/// <summary>Storybook の <c>title</c> に相当する、クラス単位のストーリー階層。</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StoryMeta(string title) : Attribute
{
    public string Title { get; } = title;
}

/// <summary>
/// ストーリー構築時の文脈。<see cref="Signal{T}"/> で作った signal は自動的に knob
/// (ブラウザから編集できるパラメータ) として公開される。Storybook の args 相当。
/// </summary>
public sealed class StoryContext : IDisposable
{
    private static long _nextResourceOwnerId;
    private readonly List<StoryKnob> _knobs = new();
    private readonly object _logGate = new();
    private readonly List<StoryLogEntry> _log = new();
    private readonly Luxel.Resources.ResourceSystem? _resources;
    private readonly Luxel.Resources.ResourceScope? _scopedResources;
    private long _logSeq;
    private const int LogCapacity = 200;
    private StoryArgs _args;
    private readonly List<StoryArgDefinition> _argDefinitions = new();
    private readonly HashSet<string> _argNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StoryKnob> _argKnobs = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _resourceSubscriptions = [];
    private readonly HashSet<Luxel.Resources.ResourceSystem> _observedResourceSystems =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>Raised with the full canonical snapshot whenever a declared arg changes.</summary>
    public event Action<StoryArgs>? ArgsChanged;
    /// <summary>Raised whenever an action/event entry is appended to this story's Output log.</summary>
    public event Action<StoryLogEntry>? Logged;

    public StoryContext(Luxel.Resources.ResourceSystem? resources = null, StoryArgs? args = null)
    {
        _resources = resources;
        _scopedResources = resources?.CreateScope($"story-context-{Interlocked.Increment(ref _nextResourceOwnerId)}");
        _args = args ?? StoryArgs.Empty;
    }

    /// <summary>この story instance に seed された canonical args。</summary>
    public StoryArgs Args => _args;

    /// <summary>build 中に宣言された public args schema。</summary>
    public IReadOnlyList<StoryArgDefinition> ArgDefinitions => _argDefinitions;

    /// <summary>Applies a full canonical snapshot to already-declared args without rebuilding the iframe.</summary>
    public IReadOnlyList<string> ApplyArgs(StoryArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var errors = new List<string>();
        foreach ((string name, JsonElement value) in args.Values)
        {
            if (!_argKnobs.TryGetValue(name, out StoryKnob? knob))
            {
                errors.Add($"Unknown story arg '{name}'.");
                continue;
            }
            try { knob.Set(value); }
            catch (Exception error) when (error is FormatException or InvalidCastException or JsonException)
            { errors.Add($"{name}: {error.Message}"); }
        }
        return errors;
    }

    private IServiceProvider? _services;

    /// <summary>ホストが結線する DI コンテナ (共有サービス — ScriptHost / 言語サービス等の重い
    /// シングルトンをストーリー横断で使い回す口)。ASP.NET minimal API と同じく、ストーリー関数の
    /// 引数に置いた非 <see cref="StoryContext"/> 型はここから <see cref="Require{T}"/> で注入される。</summary>
    public IServiceProvider? Services => _services;

    /// <summary>DI コンテナを結線する (ホスト/テストハーネスが呼ぶ)。</summary>
    public void SetServices(IServiceProvider services) => _services = services;

    /// <summary>サービスを解決する (未登録/未結線は例外)。ストーリー引数注入の実体。</summary>
    public T Require<T>() where T : notnull
        => _services is null
            ? throw new InvalidOperationException($"DI 未結線です (SetServices) — {typeof(T).Name} を注入できません")
            : (T?)_services.GetService(typeof(T))
                ?? throw new InvalidOperationException($"サービス {typeof(T).Name} が未登録です");

    /// <summary>サービスを解決する (無ければ default)。</summary>
    public T? Get<T>() => _services is null ? default : (T?)_services.GetService(typeof(T));

    /// <summary>ホスト所有の ResourceSystem (画像/テクスチャ等のロード窓口 — knob/Log と同じく
    /// 「ストーリーがホスト設備を借りる」窓口)。キャッシュはストーリー横断で共有され、
    /// ハンドルは取得側 (シーン等) が Dispose する (refcount)。Pump はホストの毎フレームループが叩き、
    /// <c>Observe&lt;T&gt;</c> のsignalへ初回完了・reload・failureをUI thread上で反映する。</summary>
    public Luxel.Resources.ResourceSystem Resources
        => _resources ?? throw new InvalidOperationException("ホストが ResourceSystem を設定していません (StoryContext ctor で渡す)");

    /// <summary><see cref="Resources"/> の nullable 版 — 画像配線など「あれば使う」任意機能用。</summary>
    public Luxel.Resources.ResourceSystem? ResourcesOrNull => _resources;

    /// <summary>この story instance が所有する resource lease のスコープ。Context の破棄時に一括解放される。</summary>
    public Luxel.Resources.ResourceScope ScopedResources
        => _scopedResources ?? throw new InvalidOperationException("ホストが ResourceSystem を設定していません (StoryContext ctor で渡す)");

    /// <summary><see cref="ScopedResources"/> の nullable 版。</summary>
    public Luxel.Resources.ResourceScope? ScopedResourcesOrNull => _scopedResources;

    /// <summary>resource状態をUI signalとして観測する。通知はResourceSystem.Pump() threadで反映される。</summary>
    public Signal<Luxel.Resources.ResourceState> Observe<T>(Luxel.Resources.ResourceHandle<T> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        var signal = new Signal<Luxel.Resources.ResourceState>(handle.State);
        _resourceSubscriptions.Add(handle.SubscribeState(state => signal.Value = state));
        return signal;
    }

    /// <summary>
    /// storyが所有する別のResourceSystem上のhandleをUI signalへ変換する。
    /// 登録したsystemはhostのframe/Pump threadで進行し、同じsystemは1フレームに1回だけPumpされる。
    /// </summary>
    public Signal<Luxel.Resources.ResourceState> Observe<T>(
        Luxel.Resources.ResourceSystem resources,
        Luxel.Resources.ResourceHandle<T> handle)
    {
        ArgumentNullException.ThrowIfNull(resources);
        Signal<Luxel.Resources.ResourceState> signal = Observe(handle);
        if (!ReferenceEquals(resources, _resources)) _observedResourceSystems.Add(resources);
        return signal;
    }

    /// <summary>story所有ResourceSystemの通知、reload、retirementをhost threadで進行する。</summary>
    public void PumpObservedResources()
    {
        foreach (Luxel.Resources.ResourceSystem resources in _observedResourceSystems.ToArray())
        {
            try { resources.Pump(); }
            catch (ObjectDisposedException) { _observedResourceSystems.Remove(resources); }
        }
    }

    /// <summary>story所有ResourceSystemを非同期host loopから進行する。</summary>
    public async ValueTask PumpObservedResourcesAsync(CancellationToken cancellationToken = default)
    {
        foreach (Luxel.Resources.ResourceSystem resources in _observedResourceSystems.ToArray())
        {
            try { await resources.PumpAsync(cancellationToken).ConfigureAwait(false); }
            catch (ObjectDisposedException) { _observedResourceSystems.Remove(resources); }
        }
    }

    /// <summary>この story instance が subscription と scoped resource lease を解放する。複数回呼び出しても安全。</summary>
    public void Dispose()
    {
        foreach (IDisposable subscription in _resourceSubscriptions) subscription.Dispose();
        _resourceSubscriptions.Clear();
        _observedResourceSystems.Clear();
        _scopedResources?.Dispose();
    }

    private Luxel.Graphics.GpuDevice? _device;
    private Luxel.Typography.VectorFont? _font;

    /// <summary>ホスト所有の GPU デバイスとフォントを結線する (Resources と同じ「借りる」窓口)。
    /// 実窓ホストだけが呼ぶ — 実窓専用ストーリー (追加ウィンドウの生成等) が使う。</summary>
    public void SetGpuHost(Luxel.Graphics.GpuDevice device, Luxel.Typography.VectorFont font)
    {
        _device = device;
        _font = font;
    }

    /// <summary><see cref="Device"/> の nullable 版。GPU resource stepが利用可能なhostかをbuild中に判定する用途。</summary>
    public Luxel.Graphics.GpuDevice? DeviceOrNull => _device;

    /// <summary>ホスト所有の GpuDevice (実窓専用ストーリー用 — 第 2 ウィンドウの生成等)。</summary>
    public Luxel.Graphics.GpuDevice Device
        => _device ?? throw new InvalidOperationException("ホストが GpuDevice を結線していません (SetGpuHost)");

    /// <summary>ホスト所有の VectorFont (実窓専用ストーリー用)。</summary>
    public Luxel.Typography.VectorFont Font
        => _font ?? throw new InvalidOperationException("ホストが VectorFont を結線していません (SetGpuHost)");

    private Action<string>? _navigator;

    /// <summary>ストーリー遷移サービスをホストが結線する (docs の <c>story:</c> リンク等が使う)。
    /// 入力ディスパッチ中に呼ばれても安全なこと — 即時のホスト破棄はせずキュー/コマンド経由を推奨。</summary>
    public void SetNavigator(Action<string> navigate) => _navigator = navigate;

    /// <summary>別ストーリーへ遷移する (Resources/Log と同じ「ホスト設備を借りる」窓口)。
    /// ホスト未結線は Log のみ (テスト等で落ちない)。</summary>
    public void Navigate(string path)
    {
        if (_navigator is not null) _navigator(path);
        else Log($"navigate: {path} (ホスト未結線)");
    }

    /// <summary>登録された knob (ギャラリーが列挙・編集する)。</summary>
    public IReadOnlyList<StoryKnob> Knobs => _knobs;

    private readonly List<StoryPlay> _plays = new();

    /// <summary>登録された play (E2E ランナー/Gallery が実行する)。</summary>
    public IReadOnlyList<StoryPlay> Plays => _plays;

    /// <summary>true の間は Play 登録を無視する — docs の StoryRef 等、**別ストーリーを同じ ctx で
    /// 埋め込み構築する**ときに、埋め込まれた側の play がページへ漏れないようにする。</summary>
    public bool SuppressPlays { get; set; }

    /// <summary>play (対話テスト) を登録する — 本家 Storybook の play 関数相当。
    /// ストーリー本体と同居させ、クロージャで signal/widget を直接掴んで良い。
    /// **golden はここの <c>d.Snap()</c> だけが生む** — 初期絵の回帰だけ欲しければ
    /// <c>ctx.Play(d =&gt; d.Snap())</c> の 1 行。複数登録可 (名前付き) — **play ごとに
    /// ストーリーは作り直される** (独立実行、前の play の状態は引き継がない)。</summary>
    public void Play(Func<PlayDriver, Task> body)
    {
        if (!SuppressPlays) _plays.Add(new StoryPlay("", body));
    }

    /// <summary>名前付き play (1 ストーリーに複数のテストを紐づける)。テスト名は "パス#名前"。</summary>
    public void Play(string name, Func<PlayDriver, Task> body)
    {
        if (!SuppressPlays) _plays.Add(new StoryPlay(name, body));
    }

    /// <summary>「初期絵の golden を 1 枚」のトリビアル play を登録する糖衣 — 式形式のストーリー向け:
    /// <c>public static StoryResult X(StoryContext ctx) =&gt; ctx.Snap(Frame(...));</c></summary>
    public Widget Snap(Widget w)
    {
        Play(static d => d.Snap());
        return w;
    }

    /// <summary>signal を作成し knob として公開する。bool/int/float/string/uint(色) が編集対応。
    /// <paramref name="description"/> は Knobs テーブルの説明列 (autodoc 相当) に表示される。</summary>
    public Signal<T> Signal<T>(string name, T initial, string? description = null)
    {
        var sig = new Signal<T>(initial);
        _knobs.Add(StoryKnob.For(name, sig, description));
        return sig;
    }

    /// <summary>外部から seed/share 可能な Storybook arg を宣言する。</summary>
    public Signal<T> Arg<T>(string name, T defaultValue, StoryArgOptions<T>? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_argNames.Add(name))
            throw new InvalidOperationException($"Story arg '{name}' is declared more than once in the same instance.");

        T initial = defaultValue;
        if (_args.TryGet(name, out JsonElement incoming))
        {
            try { initial = options?.Parser is { } parser ? parser(incoming) : WidgetDebugCodec.Coerce<T>(incoming); }
            catch (Exception error) when (error is FormatException or InvalidCastException or JsonException or ArgumentException)
            {
                Log($"arg '{name}' was ignored: {error.Message}");
            }
        }

        var signal = new Signal<T>(initial);
        StoryKnob knob = StoryKnob.For(name, signal, options?.Description, options?.Parser);
        _knobs.Add(knob);
        _argKnobs.Add(name, knob);
        IReadOnlyList<string>? choices = typeof(T).IsEnum ? Enum.GetNames(typeof(T)) : null;
        _argDefinitions.Add(new StoryArgDefinition(
            name,
            knob.Type,
            StoryArgCodec.Serialize(defaultValue),
            options?.Description,
            options?.Order ?? 1000,
            options?.Min,
            options?.Max,
            options?.Step,
            choices));
        signal.Changed += value =>
        {
            _args = _args.With(name, StoryArgCodec.Serialize(value));
            ArgsChanged?.Invoke(_args);
        };
        return signal;
    }

    // ---- knob 編集キュー (KT): エディタの commit は effect 実行中に走るため、signal 書き込みは
    //      ここへ積んでホストのフレームループ (effect 文脈外) が Pump する ----
    private readonly object _knobEditGate = new();
    private readonly List<(StoryKnob Knob, string Value)> _knobEdits = new();

    /// <summary>knob の文字列編集をキューする (任意スレッド/effect 内から安全)。</summary>
    public void QueueKnobEdit(StoryKnob knob, string value)
    {
        lock (_knobEditGate) _knobEdits.Add((knob, value));
    }

    /// <summary>キューされた knob 編集を適用する — ホストのフレームループから毎フレーム呼ぶ。</summary>
    public void PumpKnobEdits()
    {
        (StoryKnob Knob, string Value)[] edits;
        lock (_knobEditGate)
        {
            if (_knobEdits.Count == 0) return;
            edits = _knobEdits.ToArray();
            _knobEdits.Clear();
        }
        foreach ((StoryKnob k, string v) in edits)
        {
            try { k.SetText(v); } catch { /* 不正値は無視 */ }
        }
    }

    /// <summary>イベントログを記録する (Storybook の Actions 相当)。ギャラリーの Log パネルに表示される。
    /// <c>Button(_ => ctx.Log("clicked"), ...)</c> のようにハンドラから呼ぶ。直近 200 件を保持。</summary>
    public void Log(string message)
    {
        StoryLogEntry entry;
        lock (_logGate)
        {
            entry = new StoryLogEntry(++_logSeq, DateTime.Now.ToString("HH:mm:ss.fff"), message);
            _log.Add(entry);
            if (_log.Count > LogCapacity) _log.RemoveAt(0);
        }
        Logged?.Invoke(entry);
    }

    /// <summary>ログのスナップショット (サーバスレッドから読む)。</summary>
    public StoryLogEntry[] LogSnapshot()
    {
        lock (_logGate) return _log.ToArray();
    }
}

/// <summary>Reflection-free reactive host used by generated component Basics in component assemblies.</summary>
public sealed class GeneratedComponentStoryPreview(Func<Widget> build) : CompositeControl
{
    private readonly Func<Widget> _build = build ?? throw new ArgumentNullException(nameof(build));
    protected override Widget Build() => _build();
}

/// <summary>ストーリーのイベントログ 1 件。Seq はストーリー実体化ごとの連番 (フロントの差分表示用)。</summary>
public readonly record struct StoryLogEntry(long Seq, string Time, string Message);

/// <summary>StoryContext の knob 1 つ (名前 + 型ヒント + 説明 + 現在値 + 書き込み)。</summary>
public sealed class StoryKnob
{
    private readonly Func<string> _get;
    private readonly Action<JsonElement> _set;

    public string Name { get; }
    /// <summary>DevTools と同じ型ヒント ("color"/"int"/"float"/"bool"/"string")。</summary>
    public string Type { get; }
    /// <summary>Knobs テーブルの説明列 (autodoc 相当、任意)。</summary>
    public string? Description { get; }
    public string Value => _get();

    private StoryKnob(string name, string type, string? description, Func<string> get, Action<JsonElement> set)
    { Name = name; Type = type; Description = description; _get = get; _set = set; }

    public void Set(JsonElement el) => _set(el);

    /// <summary>文字列表現から書き込む (エディタ経由 — 型に応じて JSON へ寄せる)。
    /// 数値/bool として解釈できない文字列は <see cref="FormatException"/> (Pump 側が無視する)。</summary>
    public void SetText(string v)
    {
        try
        {
            Set(Type switch
            {
                "bool" => JsonSerializer.SerializeToElement(
                    bool.TryParse(v, out bool b) ? b : throw new FormatException(v)),
                "int" => JsonSerializer.SerializeToElement(
                    int.TryParse(v, out int i) ? i : throw new FormatException(v)),
                "float" => JsonSerializer.SerializeToElement(
                    float.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out float f)
                        ? f : throw new FormatException(v)),
                _ => JsonSerializer.SerializeToElement(v),   // color/string は文字列のまま (Coerce が解釈)
            });
        }
        catch (FormatException)
        {
            // Interactive knob editors ignore incomplete/invalid text and retain the previous value.
        }
    }

    internal static StoryKnob For<T>(string name, Signal<T> sig, string? description = null,
        Func<JsonElement, T>? parser = null)
    {
        // enum: DebugProps と同じ "enum:A|B|C" 型ヒント。書き込みは canonical name。
        if (typeof(T).IsEnum)
            return new StoryKnob(name, $"enum:{string.Join('|', Enum.GetNames(typeof(T)))}", description,
                () => sig.Value?.ToString() ?? "",
                el => sig.Value = parser is not null ? parser(el) : WidgetDebugCodec.Coerce<T>(el));
        // Length: CSS 風文字列 ("120px" "50%" "1.5em" ...) で往復
        if (typeof(T) == typeof(Length))
            return new StoryKnob(name, "length", description,
                () => sig.Value!.ToString()!,
                el => sig.Value = parser is not null ? parser(el) : WidgetDebugCodec.Coerce<T>(el));
        if (parser is not null)
            return new StoryKnob(name, "string", description,
                () => sig.Value?.ToString() ?? "", el => sig.Value = parser(el));

        string type =
            typeof(T) == typeof(uint) ? "color" :
            typeof(T) == typeof(int) ? "int" :
            typeof(T) == typeof(float) || typeof(T) == typeof(double) ? "float" :
            typeof(T) == typeof(bool) ? "bool" : "string";
        Func<string> get =
            typeof(T) == typeof(uint) ? () => WidgetDebugCodec.FormatColor((uint)(object)sig.Value!) :
            typeof(T) == typeof(bool) ? () => (bool)(object)sig.Value! ? "true" : "false" :
            () => sig.Value?.ToString() ?? "";
        bool writable = typeof(T) == typeof(uint) || typeof(T) == typeof(int) || typeof(T) == typeof(float)
                     || typeof(T) == typeof(double) || typeof(T) == typeof(bool) || typeof(T) == typeof(string);
        Action<JsonElement> set = writable ? el => sig.Value = WidgetDebugCodec.Coerce<T>(el) : _ => { };
        return new StoryKnob(name, type, description, get, set);
    }
}

/// <summary>Source-generated identity for one production <c>[UiComponent]</c> Docs/Basic/Playground set.</summary>
public sealed record GeneratedComponentStoryDescriptor(
    string ComponentType,
    string AssemblyOwner,
    string Category,
    string ControlName,
    bool IsUserFacing = true)
{
    public string RoutePrefix => IsUserFacing
        ? $"Controls/{Category}/{ControlName}"
        : $"Gallery/Infrastructure/{ControlName}";
    public string DocsPath => $"{RoutePrefix}/Docs";
    public string BasicPath => $"{RoutePrefix}/Basic";
    public string PlaygroundPath => $"{RoutePrefix}/Playground";
}

/// <summary>StorybookのDocs/Story区分に対応する、pathとは独立したstoryの役割。</summary>
public enum StoryKind
{
    Unspecified,
    Docs,
    Basic,
    Playground,
    Example,
    State,
    AccessibilityFixture,
    TestFixture,
}

internal static class StoryKindResolver
{
    internal static StoryKind Infer(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return StoryKind.Unspecified;

        return segments[^1] switch
        {
            "Docs" => StoryKind.Docs,
            "Basic" => StoryKind.Basic,
            "Playground" => StoryKind.Playground,
            _ when segments.Contains("Examples", StringComparer.Ordinal) => StoryKind.Example,
            _ when segments.Contains("States", StringComparer.Ordinal) => StoryKind.State,
            _ when segments.Contains("Accessibility", StringComparer.Ordinal) => StoryKind.AccessibilityFixture,
            _ when segments.Contains("Test", StringComparer.Ordinal) => StoryKind.TestFixture,
            _ => StoryKind.Unspecified,
        };
    }
}

/// <summary>Declares the category project that owns a compiled story route.</summary>
public sealed record StoryOwnership(string Category, string RegistrationIdentity, GalleryCompatibility Compatibility)
{
    public static StoryOwnership BrowserSafe(string category, string registrationIdentity)
        => new(category, registrationIdentity, GalleryCompatibility.BrowserSafe);

    public static StoryOwnership NativeOnly(string category, string registrationIdentity)
        => new(category, registrationIdentity, GalleryCompatibility.NativeOnly);
}

public enum GalleryCompatibility
{
    BrowserSafe,
    NativeOnly,
}

/// <summary>Controls exact-path composition. Only authored stories may explicitly replace generated component fallbacks.</summary>
public enum StoryRegistrationKind
{
    Authored,
    GeneratedComponentFallback,
}

/// <summary>登録済みストーリー 1 件。<see cref="Build"/> は選択のたびに新しい semantic result を作る。
/// <paramref name="Source"/> は属性・signature・本体を含む [Story] メソッド宣言の C# ソース
/// (storysource — GalleryのSourceビュー／docsの「コードを見る」用、ジェネレーターが焼き込む)。<paramref name="RealWindowOnly"/> は snap 回帰の対象外 (実窓専用)。</summary>
public sealed record StoryInfo(string Path, Func<StoryContext, StoryResult> Build,
                               string? Source = null, bool RealWindowOnly = false,
                               IReadOnlyList<StoryArgDefinition>? ArgDefinitions = null, string? CapabilityNote = null,
                               StoryRegistrationKind RegistrationKind = StoryRegistrationKind.Authored,
                               GeneratedComponentStoryDescriptor? ProductionComponent = null,
                               StoryOwnership? Ownership = null,
                               bool IncludeInPageNavigation = true,
                               StoryKind Kind = StoryKind.Unspecified)
{
    /// <summary>パスの先頭セグメント (章 — サイドバーのトップレベル)。</summary>
    public string Component => Path.IndexOf('/') is >= 0 and var i ? Path[..i] : Path;
    /// <summary>パスの末尾セグメント (ストーリー名)。</summary>
    public string Name => Path.LastIndexOf('/') is >= 0 and var i ? Path[(i + 1)..] : "Default";
}

/// <summary>全アセンブリのストーリー登録先。ソースジェネレーターが module initializer から Register する。</summary>
public static class StoryRegistry
{
    private static readonly object Gate = new();
    private static readonly List<StoryInfo> Stories = new();
    private static readonly List<Action> Providers = new();
    private static readonly HashSet<Action> FailedProviders = new();
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal);
    public static void Register(StoryInfo story)
    {
        ArgumentNullException.ThrowIfNull(story);
        if (story.Kind == StoryKind.Unspecified)
            story = story with { Kind = StoryKindResolver.Infer(story.Path) };
        lock (Gate)
        {
            int existing = Stories.FindIndex(item => item.Path == story.Path);
            if (existing >= 0) Stories[existing] = story;
            else Stories.Add(story);
        }
    }

    /// <summary>列挙/検索の直前に追加storyを同期するproviderを登録する。providerは冪等であること。</summary>
    public static void RegisterProvider(Action provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (Gate) Providers.Add(provider);
    }

    private static void EnsureProviders()
    {
        Action[] providers;
        lock (Gate) providers = Providers.Where(provider => !FailedProviders.Contains(provider)).ToArray();
        foreach (Action provider in providers)
        {
            try { provider(); }
            catch (Exception error)
            {
                lock (Gate) FailedProviders.Add(provider);
                Console.Error.WriteLine($"[story-registry] provider failed and was disabled: {error}");
            }
        }
    }

    /// <summary>登録順を維持したStoryのスナップショット。同名置換は元の位置を維持する。</summary>
    public static IReadOnlyList<StoryInfo> All
    {
        get
        {
            EnsureProviders();
            lock (Gate) return Stories.ToArray();
        }
    }

    /// <summary>旧routeをcanonical storyへ解決するaliasを登録する。aliasは<see cref="All"/>へ表示しない。</summary>
    public static void RegisterAlias(string oldPath, string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        if (oldPath == canonicalPath) throw new ArgumentException("Story alias must point to a different path.", nameof(canonicalPath));
        lock (Gate) Aliases[oldPath] = canonicalPath;
    }

    /// <summary>canonical pathへ登録された旧route一覧のsnapshot。</summary>
    public static IReadOnlyList<string> AliasesFor(string canonicalPath)
    {
        lock (Gate)
            return Aliases.Where(pair => pair.Value == canonicalPath)
                .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
    }

    public static StoryInfo? Find(string path)
    {
        EnsureProviders();
        lock (Gate)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (Aliases.TryGetValue(path, out string? canonical))
            {
                if (!visited.Add(path)) throw new InvalidOperationException($"Story alias cycle detected at '{path}'.");
                path = canonical;
            }
            return Stories.FirstOrDefault(s => s.Path == path);
        }
    }
}
