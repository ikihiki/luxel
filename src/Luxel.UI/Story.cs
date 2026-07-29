using System.Text.Json;

namespace Luxel.UI;

/// <summary>
/// ギャラリー (Storybook 風カタログ) のストーリー定義。
/// <c>[Story("Button/Primary")] static Widget Primary() => ...</c> と書くと
/// ソースジェネレーターが収集して <see cref="StoryRegistry"/> に登録する (reflection なし)。
/// 署名は <c>static Widget M()</c> または <c>static Widget M(StoryContext ctx)</c>。
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StoryAttribute(string path) : Attribute
{
    /// <summary>スラッシュ区切りの階層パス (本家 Storybook の title 相当 — 深さ任意)。
    /// 例: "Controls/Button/Primary" — 末尾がストーリー名、手前が章/フォルダ。
    /// パスは ID (golden ファイル名 / story: リンク / E2E 参照) — サイドバーはこれをそのまま木にする。</summary>
    public string Path { get; } = path;
    /// <summary>プレビューの論理サイズ。片方だけの指定はもう片方が既定 (480×320) で補完される。
    /// **両方省略するとプレビュー領域いっぱい (fill)** — docs ページ等の全面表示用
    /// (全画面モードではメイン全面、snap では 800×480 固定で決定的)。</summary>
    public int Width { get; set; } = 480;
    public int Height { get; set; } = 320;
    /// <summary>"light" / "dark" (省略時はギャラリーの現在値)。</summary>
    public string? Theme { get; set; }
    /// <summary>表示順 (小さいほど先頭、既定 1000 = アルファベット順)。コンポーネント (グループ) は
    /// 所属ストーリーの最小 Order で並ぶ — 章立て (はじめに → アーキテクチャ → …) 用。</summary>
    public int Order { get; set; } = 1000;
    /// <summary>true = 実ウィンドウ専用 (音声再生・実デバイス入力など)。snap 回帰は SKIP し、
    /// Gallery アプリでは通常どおり表示される。golden は作らない。</summary>
    public bool RealWindowOnly { get; set; }
    /// <summary>実行可能なコピー単位を記述する SampleBundle の ID。未指定は Gallery harness 専用。</summary>
    public string? SampleBundle { get; set; }
}

/// <summary>
/// ストーリー構築時の文脈。<see cref="Signal{T}"/> で作った signal は自動的に knob
/// (ブラウザから編集できるパラメータ) として公開される。Storybook の args 相当。
/// </summary>
public sealed class StoryContext
{
    private readonly List<StoryKnob> _knobs = new();
    private readonly object _logGate = new();
    private readonly List<StoryLogEntry> _log = new();
    private readonly Luxel.Resources.ResourceSystem? _resources;
    private long _logSeq;
    private const int LogCapacity = 200;
    private readonly StoryArgs _args;
    private readonly List<StoryArgDefinition> _argDefinitions = new();
    private readonly HashSet<string> _argNames = new(StringComparer.Ordinal);

    public StoryContext(Luxel.Resources.ResourceSystem? resources = null, StoryArgs? args = null)
    {
        _resources = resources;
        _args = args ?? StoryArgs.Empty;
    }

    /// <summary>この story instance に seed された canonical args。</summary>
    public StoryArgs Args => _args;

    /// <summary>build 中に宣言された public args schema。</summary>
    public IReadOnlyList<StoryArgDefinition> ArgDefinitions => _argDefinitions;

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
    /// ハンドルは取得側 (シーン等) が Dispose する (refcount)。Pump はホストの毎フレームループが叩く。
    /// 初回ロードの publish は Pump 不要 (直接反映) なので、GPU シーンの Init は Ready を待ってよい。</summary>
    public Luxel.Resources.ResourceSystem Resources
        => _resources ?? throw new InvalidOperationException("ホストが ResourceSystem を設定していません (StoryContext ctor で渡す)");

    /// <summary><see cref="Resources"/> の nullable 版 — 画像配線など「あれば使う」任意機能用。</summary>
    public Luxel.Resources.ResourceSystem? ResourcesOrNull => _resources;

    private Luxel.Graphics.GpuDevice? _device;
    private Luxel.Typography.VectorFont? _font;

    /// <summary>ホスト所有の GPU デバイスとフォントを結線する (Resources と同じ「借りる」窓口)。
    /// 実窓ホストだけが呼ぶ — 実窓専用ストーリー (追加ウィンドウの生成等) が使う。</summary>
    public void SetGpuHost(Luxel.Graphics.GpuDevice device, Luxel.Typography.VectorFont font)
    {
        _device = device;
        _font = font;
    }

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
    /// <c>public static Widget X(StoryContext ctx) =&gt; ctx.Snap(Frame(...));</c></summary>
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
            try { initial = WidgetDebugCodec.Coerce<T>(incoming); }
            catch (Exception error) when (error is FormatException or InvalidCastException or JsonException)
            {
                Log($"arg '{name}' was ignored: {error.Message}");
            }
        }

        var signal = new Signal<T>(initial);
        StoryKnob knob = StoryKnob.For(name, signal, options?.Description);
        _knobs.Add(knob);
        _argDefinitions.Add(new StoryArgDefinition(
            name,
            knob.Type,
            JsonSerializer.SerializeToElement(defaultValue),
            options?.Description,
            options?.Order ?? 1000,
            options?.Min,
            options?.Max,
            options?.Step));
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
        lock (_logGate)
        {
            _log.Add(new StoryLogEntry(++_logSeq, DateTime.Now.ToString("HH:mm:ss.fff"), message));
            if (_log.Count > LogCapacity) _log.RemoveAt(0);
        }
    }

    /// <summary>ログのスナップショット (サーバスレッドから読む)。</summary>
    public StoryLogEntry[] LogSnapshot()
    {
        lock (_logGate) return _log.ToArray();
    }
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
        => Set(Type switch
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

    internal static StoryKnob For<T>(string name, Signal<T> sig, string? description = null)
    {
        // enum: DebugProps と同じ "enum:A|B|C" 型ヒント。書き込みは名前の TryParse (不正値は無視)
        if (typeof(T).IsEnum)
            return new StoryKnob(name, $"enum:{string.Join('|', Enum.GetNames(typeof(T)))}", description,
                () => sig.Value?.ToString() ?? "",
                el =>
                {
                    if (el.ValueKind == JsonValueKind.String
                        && Enum.TryParse(typeof(T), el.GetString(), ignoreCase: true, out object? v))
                        sig.Value = (T)v;
                });
        // Length: CSS 風文字列 ("120px" "50%" "1.5em" ...) で往復
        if (typeof(T) == typeof(Length))
            return new StoryKnob(name, "length", description,
                () => sig.Value!.ToString()!,
                el =>
                {
                    if (el.ValueKind == JsonValueKind.String
                        && Length.TryParse(el.GetString(), null, out Length l))
                        sig.Value = (T)(object)l;
                });

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

/// <summary>登録済みストーリー 1 件。<see cref="Build"/> は選択のたびに新しい widget ツリーを作る。
/// <paramref name="Width"/>/<paramref name="Height"/> が 0,0 = fill (ホストがプレビュー領域
/// いっぱいに表示する — 属性で両方省略したストーリー)。
/// <paramref name="Order"/> は表示順 (小さいほど先頭、既定 1000 = アルファベット順)。
/// <paramref name="Source"/> は属性・signature・本体を含む [Story] メソッド宣言の C# ソース
/// (storysource — GalleryのSourceビュー／docsの「コードを見る」用、ジェネレーターが焼き込む)。<paramref name="RealWindowOnly"/> は snap 回帰の対象外 (実窓専用)。</summary>
public sealed record StoryInfo(string Path, int Width, int Height, string? Theme, Func<StoryContext, Widget> Build,
                               int Order = 1000, string? Source = null, bool RealWindowOnly = false, string? SampleBundle = null,
                               Func<StoryContext, StoryResult>? ResultBuild = null, string? RuntimeBundleId = null)
{
    /// <summary>Widget/Markdown を区別した semantic build。既存 Widget Story は暗黙変換で統一される。</summary>
    public StoryResult BuildResult(StoryContext context) => ResultBuild?.Invoke(context) ?? Build(context);

    /// <summary>パスの先頭セグメント (章 — サイドバーのトップレベル)。</summary>
    public string Component => Path.IndexOf('/') is >= 0 and var i ? Path[..i] : Path;
    /// <summary>パスの末尾セグメント (ストーリー名)。</summary>
    public string Name => Path.LastIndexOf('/') is >= 0 and var i ? Path[(i + 1)..] : "Default";
}

/// <summary>全アセンブリのストーリー登録先。ソースジェネレーターが module initializer から Register する。</summary>
public enum SampleCopyLevel { Snippet, Block, Recipe, StandaloneProject, GalleryOnly }
public enum SampleFileKind { Project, CSharp, Shader, Asset, Generated }
public enum SampleFileMode { Whole, Region, Generated, Glob }
public enum SampleMergeRule { Error, KeepFirst, Replace, Append }
public sealed record SampleFileInfo(string Path, SampleFileKind Kind, string? Region = null, string? Language = null,
    string? Destination = null, SampleFileMode Mode = SampleFileMode.Whole, string? Wrapper = null,
    string? AssetGlob = null, SampleMergeRule MergeRule = SampleMergeRule.Error)
{
    public string OutputPath => Destination ?? Path;
    public SampleFileMode EffectiveMode => AssetGlob is not null ? SampleFileMode.Glob
        : Kind == SampleFileKind.Generated ? SampleFileMode.Generated : Mode;
}
public sealed record SampleBundleInfo(string Id, string Name, string Description, string Difficulty, SampleCopyLevel CopyLevel,
    IReadOnlyList<SampleFileInfo> Files, IReadOnlyList<string>? Dependencies = null, IReadOnlyList<string>? Requirements = null,
    string? ExportSymbol = null, string? RunCommand = null, string? SmokeCommand = null,
    IReadOnlyList<string>? Platforms = null, int TimeoutSeconds = 300, int ExpectedExitCode = 0,
    string? ExpectedStdoutMarker = null, IReadOnlyList<string>? ExpectedArtifacts = null);

public static class SampleBundleRegistry
{
    private static readonly Dictionary<string, SampleBundleInfo> Bundles = new(StringComparer.Ordinal);
    public static IReadOnlyCollection<SampleBundleInfo> All => Bundles.Values;
    public static void Register(SampleBundleInfo bundle) { ArgumentNullException.ThrowIfNull(bundle); Bundles[bundle.Id] = bundle; }
    public static SampleBundleInfo? Find(string? id) => id is not null && Bundles.TryGetValue(id, out var bundle) ? bundle : null;
}

public static class StoryRegistry
{
    private static readonly object Gate = new();
    private static readonly List<StoryInfo> Stories = new();
    private static readonly List<Action> Providers = new();
    private static readonly HashSet<Action> FailedProviders = new();
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, int> ComponentOrder = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["Start"] = 0, ["Learn"] = 10, ["Build"] = 20, ["Examples"] = 30,
        ["Controls"] = 40, ["Apps"] = 50, ["Game"] = 60, ["Reference"] = 70,
        ["Internals"] = 80, ["RealWindow"] = 90, ["ADR"] = 100, ["Docs"] = 110,
    };

    public static void Register(StoryInfo story)
    {
        lock (Gate)
        {
            Stories.RemoveAll(s => s.Path == story.Path);   // 同名は上書き (Auto を手書きで置換できる)
            Stories.Add(story);
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

    /// <summary>表示順のスナップショット: コンポーネントは「所属ストーリーの最小 Order → 名前」、
    /// コンポーネント内は「Order → Path」。Order 未指定 (既定 1000) なら従来のアルファベット順。</summary>
    public static IReadOnlyList<StoryInfo> All
    {
        get
        {
            EnsureProviders();
            lock (Gate)
                return Stories
                    .GroupBy(s => s.Component)
                    .OrderBy(g => ComponentOrder.GetValueOrDefault(g.Key, 1000))
                    .ThenBy(g => g.Min(s => s.Order))
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .SelectMany(g => g.OrderBy(s => s.Order).ThenBy(s => s.Path, StringComparer.Ordinal))
                    .ToArray();
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
