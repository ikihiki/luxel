using Luxel.Graphics.TwoD;
using Luxel.UI;
using Luxel.Workbench;
using static Luxel.Controls.Kit;

using Luxel.Typography.TwoD;
namespace Luxel.Controls;

/// <summary>
/// メニューバー (ADR-0013): <see cref="CommandRegistry"/> のメニュー階層を横一列のルートに描き、
/// クリックでドロップダウン (項目 = タイトル + キーバインド、disabled はグレー、深い階層は
/// グループ見出しでインライン展開)。アクティブ doc の寄与は <see cref="Contributions"/>
/// (Build 中に評価 — 中で signal を読めば追従) で合成。
/// </summary>
[UiComponent]
public sealed partial class MenuBar : CompositeControl
{
    public const float BarH = 30f;

    /// <summary>コマンドの単一の真実。</summary>
    [UiParam] private readonly Bindable<CommandRegistry> _registry = new();
    /// <summary>アクティブ doc の寄与 (null = なし)。Build 中に呼ぶ — 中で Workspace.Active 等の
    /// signal を読めばアクティブ切替に自動追従する。</summary>
    [UiParam] private readonly Bindable<Func<IReadOnlyList<CommandContribution>>?> _contributions = new();

    private UiBuildContext? _ctx;
    private readonly Dictionary<string, Widget> _rootLabels = new();
    private IReadOnlyList<MenuNode> _nodes = [];

    /// <summary>ルートメニューのラベル widget (play/テスト用)。</summary>
    public Widget? RootLabel(string label) => _rootLabels.GetValueOrDefault(label);

    protected override void OnRealize(UiBuildContext ctx) => _ctx = ctx;

    protected override Widget Build()
    {
        CommandRegistry reg = Registry.Get();
        _ = reg.Version.Value;   // 登録変更で自動再構築
        _nodes = reg.BuildMenu(Contributions.Get()?.Invoke());
        _rootLabels.Clear();

        var roots = new List<Widget>();
        foreach (MenuNode node in _nodes)
        {
            MenuNode n = node;
            Widget lbl = LinkText(_ => OpenRoot(n), node.Label, margin: new Thickness(10, 6, 2, 0));
            _rootLabels[node.Label] = lbl;
            roots.Add(lbl);
        }
        Widget row = HStack(2)[roots.ToArray()];
        Widget hairline = Box(background: (Func<uint>)(() => UiTheme.T.BorderColor), height: 1, hAlign: Align.Stretch);
        return Border(background: (Func<uint>)(() => UiTheme.T.Surface), hAlign: Align.Stretch)[
            VStack()[Spacer(height: 0), row, Spacer(height: 5), hairline]];
    }

    /// <summary>ルートのドロップダウンを開く (play からも呼べる)。</summary>
    public void OpenRoot(MenuNode node)
    {
        if (_ctx is null) return;
        Widget anchor = _rootLabels.GetValueOrDefault(node.Label) ?? this;
        var rows = new List<Widget>();
        AppendRows(rows, node.Children, depth: 0);
        if (rows.Count == 0) return;
        // 幅 = 最長行の内容幅 (キーバインド列が全行で揃う)。全幅に伸ばさない
        var lc = new LayoutContext { Font = _ctx.Font, Theme = _ctx.Theme.Peek() };
        float w = 0;
        foreach (Widget r in rows) w = MathF.Max(w, r.MaxIntrinsicWidth(0, lc));
        w = Math.Clamp(w + 8, 200, 380);
        // 外周 1px の枠 (外 Border = BorderColor、内 = Surface) — パネル縁が背景に溶けないように
        Widget menu = Border(background: (Func<uint>)(() => UiTheme.T.BorderColor), padding: new Thickness(1), rounded: 7f)[
            Border(background: (Func<uint>)(() => UiTheme.T.Surface), padding: new Thickness(4), rounded: 6f)[
                VStack(1)[rows.ToArray()]]];
        ContextMenu.OpenWidget(_ctx, anchor.WorldPos.X, anchor.WorldPos.Y + BarH - 6, menu, maxW: w);
    }

    private void AppendRows(List<Widget> rows, IReadOnlyList<MenuNode> nodes, int depth)
    {
        foreach (MenuNode n in nodes)
        {
            if (n.Command is { } cmd)
            {
                rows.Add(new MenuCommandRow
                {
                    Title = n.Label,
                    Gesture = cmd.Gesture is { } g ? KeyGestures.Format(g) : "",
                    Enabled = cmd.IsEnabled,
                    OnRun = () => { ContextMenu.Close(_ctx!); cmd.Run(); },
                });
            }
            else
            {
                // 深い階層はグループ見出しでインライン展開 (v1 — サブメニューのフライアウトはしない)
                if (rows.Count > 0) rows.Add(Divider());
                rows.Add(Text(n.Label, 11, color: Bind.From(() => UiTheme.T.TextMuted),
                              margin: new Thickness(8, 3, 0, 1)));
                AppendRows(rows, n.Children, depth + 1);
            }
        }
    }
}

/// <summary>ドロップダウン/パレットの 1 行: タイトル左 + キーバインド右、disabled はグレー。</summary>
internal sealed class MenuCommandRow : Widget
{
    public required string Title;
    public string Gesture = "";
    public bool Enabled = true;
    public required Action OnRun;
    public bool Highlight;   // パレットの選択行

    private const float RowH = 26f, PadX = 10f, MinW = 200f;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float tw = ctx.Font.Measure(Title, ctx.Theme.FontSm).width
                 + (Gesture.Length > 0 ? ctx.Font.Measure(Gesture, 11).width + 28 : 0);
        float w = float.IsInfinity(c.MaxW) ? MathF.Max(MinW, tw + PadX * 2) : c.MaxW;
        Size = c.Constrain(new Size(w, RowH));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
        => MathF.Max(MinW, ctx.Font.Measure(Title, ctx.Theme.FontSm).width
                         + (Gesture.Length > 0 ? ctx.Font.Measure(Gesture, 11).width + 28 : 0) + PadX * 2);

    public override string? DebugDetail => Title;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        float fs = ctx.Theme.Peek().FontSm;
        float textY = (RowH - ctx.Font.Measure("Mg", fs).height) / 2 + ctx.Font.Ascent(fs);

        UiNode bg = ctx.Canvas.AddChild(node);
        var bs = new Scene2D(); bs.FillRoundedRect(Color2D.White, 0, 0, Size.Width, RowH, 5);
        bg.Content = bs;
        ctx.Effect(() => bg.Color = ctx.Theme.Value.SurfaceAlt);
        bg.Opacity = Highlight ? 1f : 0f;
        if (!Highlight)
        {
            ctx.AddHit(node, new Rect(0, 0, Size.Width, RowH),
                onHover: h => bg.Opacity = h ? 1f : 0f,
                onClick: Enabled ? OnRun : null,
                cursor: Enabled ? CursorKind.Hand : CursorKind.Arrow);
        }
        else
        {
            ctx.AddHit(node, new Rect(0, 0, Size.Width, RowH), onClick: Enabled ? OnRun : null,
                cursor: Enabled ? CursorKind.Hand : CursorKind.Arrow);
        }

        UiNode lbl = ctx.Canvas.AddChild(node); lbl.Z = 1;
        var ls = new Scene2D();
        ctx.Font.AppendText(ls, Title, PadX, textY, fs, Color2D.White);
        lbl.Content = ls;
        ctx.Effect(() => lbl.Color = Enabled ? ctx.Theme.Value.Text : ctx.Theme.Value.TextMuted);

        if (Gesture.Length > 0)
        {
            UiNode key = ctx.Canvas.AddChild(node); key.Z = 1;
            float kw = ctx.Font.Measure(Gesture, 11).width;
            var ks = new Scene2D();
            ctx.Font.AppendText(ks, Gesture, Size.Width - PadX - kw, textY - 1, 11, Color2D.White);
            key.Content = ks;
            ctx.Effect(() => key.Color = ctx.Theme.Value.TextMuted);
        }
    }
}

/// <summary>ツールバー (ADR-0013): 掲載コマンドをボタン列に。disabled はグレー非活性。
/// クリックはコマンドを実行。登録変更 (<see cref="CommandRegistry.Version"/>) で自動追従、
/// enablement の再評価は <see cref="Refresh"/>。</summary>
[UiComponent]
public sealed partial class Toolbar : CompositeControl
{
    /// <summary>コマンドの単一の真実。</summary>
    [UiParam] private readonly Bindable<CommandRegistry> _registry = new();
    /// <summary>アクティブ doc の寄与 (Build 中に評価)。</summary>
    [UiParam] private readonly Bindable<Func<IReadOnlyList<CommandContribution>>?> _contributions = new();

    private readonly Signal<int> _refresh = new(0);

    /// <summary>enablement を再評価して描き直す。Effect 内から呼ばれても自己購読しないよう
    /// Peek ベース (`++` は get を含み、呼び元 Effect が購読して無限ループする)。</summary>
    public void Refresh() => _refresh.Value = _refresh.Peek() + 1;

    protected override Widget Build()
    {
        CommandRegistry reg = Registry.Get();
        _ = reg.Version.Value;
        _ = _refresh.Value;
        var buttons = new List<Widget>();
        foreach (Command cmd in reg.ToolbarCommands(Contributions.Get()?.Invoke()))
        {
            Command c = cmd;
            buttons.Add(c.IsEnabled
                ? Button(_ => c.Run(), c.Title, variant: Variant.Ghost, fontSize: UiTheme.T.FontSm)
                : Text(c.Title, 12, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(8, 6, 8, 0)));
        }
        return HStack(2)[buttons.ToArray()];
    }
}

/// <summary>
/// コマンドパレット (ADR-0013 の発見性の主役)。上部中央にクエリ入力 + 絞り込み一覧を開き、
/// ↑↓ で選択・Enter で実行・Esc/外側クリックで閉じる。<see cref="ContextMenu"/> と同じ
/// 前面機構 (実体化はいつでも可)。
/// </summary>
public static class CommandPalette
{
    /// <summary>パレットを開く (既に開いていれば閉じてから)。戻り値は play/テスト用のビュー。</summary>
    public static PaletteView Open(UiBuildContext ctx, CommandRegistry registry,
                                   IReadOnlyList<CommandContribution>? contributions = null)
    {
        float vw = ctx.Host?.Width ?? 640;
        var view = new PaletteView { Ctx = ctx, Registry = registry, Contributions = contributions };
        ContextMenu.OpenWidget(ctx, MathF.Max(8, (vw - PaletteView.PanelW) / 2), 48, view, maxW: PaletteView.PanelW);
        return view;
    }

    public sealed class PaletteView : CompositeControl
    {
        public const float PanelW = 440f;
        private const int MaxRows = 9;

        public required UiBuildContext Ctx;
        public required CommandRegistry Registry;
        public IReadOnlyList<CommandContribution>? Contributions;

        private readonly Signal<string> _query = new("");
        private readonly Signal<int> _selected = new(0);
        private IReadOnlyList<Command> _filtered = [];
        private TextField? _field;   // フィールド保持 — Rebuild をまたいでフォーカスが生き残る

        /// <summary>現在の絞り込み結果 (play/テスト用)。</summary>
        public IReadOnlyList<Command> Filtered => _filtered;

        /// <summary>クエリ入力 (play/テスト用)。</summary>
        public Widget? Field => _field;

        protected override Widget Build()
        {
            string q = _query.Value.Trim();
            int sel = _selected.Value;
            _filtered = Registry.PaletteCommands(Contributions)
                .Where(c => q.Length == 0
                            || c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                            || c.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(MaxRows).ToArray();
            sel = Math.Clamp(sel, 0, Math.Max(0, _filtered.Count - 1));

            if (_field is null)
            {
                _field = TextField(_query, placeholder: "コマンドを検索…", width: PanelW - 20);
                _field.ExtraKeys = ev => ev.Key switch
                {
                    Key.Down => Move(+1),
                    Key.Up => Move(-1),
                    Key.Enter => RunSelected(),
                    Key.Escape => CloseSelf(),
                    _ => false,
                };
            }

            var rows = new List<Widget> { _field };
            for (int i = 0; i < _filtered.Count; i++)
            {
                Command c = _filtered[i];
                rows.Add(new MenuCommandRow
                {
                    Title = c.Title,
                    Gesture = c.Gesture is { } g ? KeyGestures.Format(g) : "",
                    Enabled = c.IsEnabled,
                    Highlight = i == sel,
                    OnRun = () => { ContextMenu.Close(Ctx); if (c.IsEnabled) c.Run(); },
                });
            }
            if (_filtered.Count == 0)
                rows.Add(Text("該当なし", 12, color: Bind.From(() => UiTheme.T.TextMuted), margin: new Thickness(10, 6, 0, 4)));

            return Border(background: (Func<uint>)(() => UiTheme.T.BorderColor), padding: new Thickness(1), rounded: 9f)[
                Border(background: (Func<uint>)(() => UiTheme.T.Surface), padding: new Thickness(10, 10, 10, 8), rounded: 8f)[
                    VStack(4)[rows.ToArray()]]];
        }

        private bool Move(int d)
        {
            _selected.Value = Math.Clamp(_selected.Value + d, 0, Math.Max(0, _filtered.Count - 1));
            return true;
        }

        private bool RunSelected()
        {
            Command? c = _selected.Peek() < _filtered.Count ? _filtered[_selected.Peek()] : null;
            ContextMenu.Close(Ctx);
            if (c is { IsEnabled: true }) c.Run();
            return true;
        }

        private bool CloseSelf()
        {
            ContextMenu.Close(Ctx);
            return true;
        }
    }
}
