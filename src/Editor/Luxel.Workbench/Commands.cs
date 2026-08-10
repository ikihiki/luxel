using Luxel.UI;

namespace Luxel.Workbench;

/// <summary>コマンド 1 つ (ADR-0013 の単一の真実)。MenuBar/CommandPalette/Toolbar/Keymap は
/// すべてこの定義のビュー。</summary>
public sealed record Command(string Id, string Title, Action Run,
                             Func<bool>? Enabled = null, KeyGesture? Gesture = null)
{
    /// <summary>いま実行できるか (enablement)。評価は表示/実行時。</summary>
    public bool IsEnabled => Enabled?.Invoke() ?? true;
}

/// <summary>アクティブドキュメントからの寄与 1 件 (ADR-0013)。コマンド + メニューパス
/// (null = メニューに出さない) + ツールバー掲載。シェルがアクティブ doc の
/// <see cref="IEditorDocument.Contributions"/> を集めて各サーフェスへ合成する。</summary>
public sealed record CommandContribution(Command Command, string? MenuPath = null,
                                         bool Toolbar = false, int Order = 0);

/// <summary>メニュー階層のノード (BuildMenu の結果)。Command が null = フォルダ。</summary>
public sealed record MenuNode(string Label, Command? Command, IReadOnlyList<MenuNode> Children);

/// <summary>キーバインド文字列 ⇄ <see cref="KeyGesture"/> ("Ctrl+Shift+P" / "F3" / "Ctrl+1")。</summary>
public static class KeyGestures
{
    /// <summary>解析。不明トークンは null。</summary>
    public static KeyGesture? Parse(string text)
    {
        Key key = Key.None;
        bool ctrl = false, shift = false, alt = false;
        foreach (string token in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default:
                    string name = token.Length == 1 && char.IsAsciiDigit(token[0]) ? $"D{token}" : token;
                    if (!Enum.TryParse(name, ignoreCase: true, out key)) return null;
                    break;
            }
        }
        return key == Key.None ? null : new KeyGesture(key, ctrl, shift, alt);
    }

    /// <summary>表示用文字列 ("Ctrl+Shift+P")。</summary>
    public static string Format(KeyGesture g)
    {
        string key = g.Key is >= Key.D0 and <= Key.D9 ? ((int)(g.Key - Key.D0)).ToString() : g.Key.ToString();
        return $"{(g.Ctrl ? "Ctrl+" : "")}{(g.Shift ? "Shift+" : "")}{(g.Alt ? "Alt+" : "")}{key}";
    }
}

/// <summary>
/// コマンドの単一の真実 (ADR-0013)。コマンド { id, タイトル, キーバインド, enablement, run } を
/// 登録し、メニュー項目は**パス文字列** ("File/保存") + コマンド id の寄与で足す (Unity 流)。
/// MenuBar / CommandPalette / Toolbar / Keymap はここから生成される純粋ビュー —
/// アクティブ doc の <see cref="CommandContribution"/> は各ビューの生成時に合成する (Unreal 流)。
/// 変更は <see cref="Version"/> が進む (UI の TrackBuild 再構築フック)。
/// </summary>
public sealed class CommandRegistry
{
    private readonly Dictionary<string, Command> _commands = new();
    private readonly List<(string Path, string CommandId, int Order, int Seq)> _menu = new();
    private readonly List<(string CommandId, int Order, int Seq)> _toolbar = new();
    private int _seq;

    /// <summary>登録の世代 (UI が Build で読むと登録変更で自動再構築)。</summary>
    public Signal<int> Version { get; } = new(0);

    /// <summary>コマンドを登録する (同 id は上書き)。menuPath / toolbar で同時に掲載できる。</summary>
    public void Register(Command command, string? menuPath = null, int order = 0, bool toolbar = false)
    {
        _commands[command.Id] = command;
        if (menuPath is not null) _menu.Add((menuPath, command.Id, order, _seq++));
        if (toolbar) _toolbar.Add((command.Id, order, _seq++));
        Version.Value++;
    }

    /// <summary>省略形: キーは "Ctrl+S" 形式の文字列で。</summary>
    public Command Register(string id, string title, Action run, Func<bool>? enabled = null,
                            string? key = null, string? menuPath = null, int order = 0, bool toolbar = false)
    {
        var cmd = new Command(id, title, run, enabled, key is null ? null : KeyGestures.Parse(key));
        Register(cmd, menuPath, order, toolbar);
        return cmd;
    }

    public Command? Find(string id) => _commands.GetValueOrDefault(id);

    /// <summary>全コマンド (パレット用は <see cref="PaletteCommands"/>)。</summary>
    public IReadOnlyCollection<Command> Commands => _commands.Values;

    /// <summary>id のコマンドを実行する (enabled のときだけ)。実行したら true。</summary>
    public bool Run(string id)
    {
        if (Find(id) is not { } c || !c.IsEnabled) return false;
        c.Run();
        return true;
    }

    /// <summary>キー入力をコマンドへ配送する (寄与優先 → 登録順)。実行したら true。
    /// UiHost へ常時結線するなら <see cref="BindShortcuts"/>。</summary>
    public bool HandleKey(Key keyValue, KeyModifiers mods, IReadOnlyList<CommandContribution>? extra = null)
    {
        var g = new KeyGesture(keyValue, mods.HasFlag(KeyModifiers.Ctrl), mods.HasFlag(KeyModifiers.Shift), mods.HasFlag(KeyModifiers.Alt));
        if (extra is not null)
            foreach (CommandContribution c in extra)
                if (c.Command.Gesture == g && c.Command.IsEnabled) { c.Command.Run(); return true; }
        foreach (Command c in _commands.Values)
            if (c.Gesture == g && c.IsEnabled) { c.Run(); return true; }
        return false;
    }

    /// <summary>登録済みキーバインドを UiHost の全域ショートカットへ結線する (登録変更に追従)。
    /// 配送は「フォーカス中コントロールが消費しなかったキーだけ」(UiHost の規約)。
    /// Dispose で解除。</summary>
    public IDisposable BindShortcuts(UiHost host)
    {
        var bound = new List<KeyGesture>();
        void Rebind()
        {
            foreach (KeyGesture g in bound) host.UnregisterShortcut(g);
            bound.Clear();
            foreach (Command c in _commands.Values)
                if (c.Gesture is { } g)
                {
                    string id = c.Id;
                    host.RegisterShortcut(g, () => Run(id));
                    bound.Add(g);
                }
        }
        IDisposable eff = Reactive.Effect(() => { _ = Version.Value; Rebind(); });
        return new Binder(() => { eff.Dispose(); foreach (KeyGesture g in bound) host.UnregisterShortcut(g); });
    }

    private sealed class Binder(Action dispose) : IDisposable
    {
        private Action? _d = dispose;
        public void Dispose() { _d?.Invoke(); _d = null; }
    }

    /// <summary>メニュー階層を組む (登録分 + 寄与、パスの各セグメントがフォルダ)。
    /// 並びは (Order, 登録順)。同一パス末端は後勝ち。</summary>
    public IReadOnlyList<MenuNode> BuildMenu(IReadOnlyList<CommandContribution>? extra = null)
    {
        var entries = new List<(string Path, Command Cmd, int Order, int Seq)>();
        foreach ((string path, string id, int order, int seq) in _menu)
            if (Find(id) is { } c) entries.Add((path, c, order, seq));
        if (extra is not null)
        {
            int seq = _seq;
            foreach (CommandContribution c in extra)
                if (c.MenuPath is not null) entries.Add((c.MenuPath, c.Command, c.Order, seq++));
        }

        var root = new Builder("");
        foreach ((string path, Command cmd, int order, int seq) in entries)
        {
            string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            Builder cur = root;
            for (int i = 0; i < parts.Length - 1; i++) cur = cur.Child(parts[i], order, seq);
            Builder leaf = cur.Child(parts[^1], order, seq);
            leaf.Command = cmd;
        }
        return root.ToNodes();
    }

    private sealed class Builder(string label)
    {
        public readonly string Label = label;
        public Command? Command;
        public int Order = int.MaxValue;
        public int Seq = int.MaxValue;
        private readonly List<Builder> _kids = new();

        public Builder Child(string name, int order, int seq)
        {
            Builder? b = _kids.FirstOrDefault(k => k.Label == name);
            if (b is null) _kids.Add(b = new Builder(name));
            b.Order = Math.Min(b.Order, order);
            b.Seq = Math.Min(b.Seq, seq);
            return b;
        }

        public IReadOnlyList<MenuNode> ToNodes()
            => _kids.OrderBy(k => k.Order).ThenBy(k => k.Seq)
                    .Select(k => new MenuNode(k.Label, k.Command, k.ToNodes())).ToArray();
    }

    /// <summary>ツールバー掲載コマンド (登録分 + 寄与、(Order, 登録順))。</summary>
    public IReadOnlyList<Command> ToolbarCommands(IReadOnlyList<CommandContribution>? extra = null)
    {
        var items = new List<(Command Cmd, int Order, int Seq)>();
        foreach ((string id, int order, int seq) in _toolbar)
            if (Find(id) is { } c) items.Add((c, order, seq));
        if (extra is not null)
        {
            int seq = _seq;
            foreach (CommandContribution c in extra)
                if (c.Toolbar) items.Add((c.Command, c.Order, seq++));
        }
        return items.OrderBy(x => x.Order).ThenBy(x => x.Seq).Select(x => x.Cmd).ToArray();
    }

    /// <summary>パレットに出す全コマンド (登録分 + 寄与、タイトル順)。</summary>
    public IReadOnlyList<Command> PaletteCommands(IReadOnlyList<CommandContribution>? extra = null)
    {
        IEnumerable<Command> all = _commands.Values;
        if (extra is not null) all = all.Concat(extra.Select(c => c.Command)).DistinctBy(c => c.Id);
        return all.OrderBy(c => c.Title, StringComparer.Ordinal).ToArray();
    }
}
