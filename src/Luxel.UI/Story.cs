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
    /// <summary>"コンポーネント/ストーリー名" の 2 階層パス。</summary>
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

    public StoryContext(Luxel.Resources.ResourceSystem? resources = null) => _resources = resources;

    /// <summary>ホスト所有の ResourceSystem (画像/テクスチャ等のロード窓口 — knob/Log と同じく
    /// 「ストーリーがホスト設備を借りる」窓口)。キャッシュはストーリー横断で共有され、
    /// ハンドルは取得側 (シーン等) が Dispose する (refcount)。Pump はホストの毎フレームループが叩く。
    /// 初回ロードの publish は Pump 不要 (直接反映) なので、GPU シーンの Init は Ready を待ってよい。</summary>
    public Luxel.Resources.ResourceSystem Resources
        => _resources ?? throw new InvalidOperationException("ホストが ResourceSystem を設定していません (StoryContext ctor で渡す)");

    /// <summary><see cref="Resources"/> の nullable 版 — 画像配線など「あれば使う」任意機能用。</summary>
    public Luxel.Resources.ResourceSystem? ResourcesOrNull => _resources;

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

    /// <summary>signal を作成し knob として公開する。bool/int/float/string/uint(色) が編集対応。
    /// <paramref name="description"/> は Knobs テーブルの説明列 (autodoc 相当) に表示される。</summary>
    public Signal<T> Signal<T>(string name, T initial, string? description = null)
    {
        var sig = new Signal<T>(initial);
        _knobs.Add(StoryKnob.For(name, sig, description));
        return sig;
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
/// <paramref name="Source"/> は [Story] メソッドの C# ソース (storysource — docs の「コードを見る」用、
/// ジェネレーターが焼き込む)。</summary>
public sealed record StoryInfo(string Path, int Width, int Height, string? Theme, Func<StoryContext, Widget> Build,
                               int Order = 1000, string? Source = null)
{
    /// <summary>パスの前半 (コンポーネント名)。</summary>
    public string Component => Path.IndexOf('/') is >= 0 and var i ? Path[..i] : Path;
    /// <summary>パスの後半 (ストーリー名)。</summary>
    public string Name => Path.IndexOf('/') is >= 0 and var i ? Path[(i + 1)..] : "Default";
}

/// <summary>全アセンブリのストーリー登録先。ソースジェネレーターが module initializer から Register する。</summary>
public static class StoryRegistry
{
    private static readonly object Gate = new();
    private static readonly List<StoryInfo> Stories = new();

    public static void Register(StoryInfo story)
    {
        lock (Gate)
        {
            Stories.RemoveAll(s => s.Path == story.Path);   // 同名は上書き (Auto を手書きで置換できる)
            Stories.Add(story);
        }
    }

    /// <summary>表示順のスナップショット: コンポーネントは「所属ストーリーの最小 Order → 名前」、
    /// コンポーネント内は「Order → Path」。Order 未指定 (既定 1000) なら従来のアルファベット順。</summary>
    public static IReadOnlyList<StoryInfo> All
    {
        get
        {
            lock (Gate)
                return Stories
                    .GroupBy(s => s.Component)
                    .OrderBy(g => g.Min(s => s.Order))
                    .ThenBy(g => g.Key, StringComparer.Ordinal)
                    .SelectMany(g => g.OrderBy(s => s.Order).ThenBy(s => s.Path, StringComparer.Ordinal))
                    .ToArray();
        }
    }

    public static StoryInfo? Find(string path)
    {
        lock (Gate) return Stories.FirstOrDefault(s => s.Path == path);
    }
}
