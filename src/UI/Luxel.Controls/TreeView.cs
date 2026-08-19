using Luxel.UI;
using Luxel.Graphics.TwoD;
using Luxel.Typography.TwoD;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

/// <summary>ツリーのノード 1 つ (イミュータブルなデータ)。<see cref="Key"/> は展開/選択状態の
/// 永続キー (ツリー再構築をまたいで一意なパス文字列を推奨)。<see cref="SearchText"/> は
/// 絞り込み (Filter) 時に Label に加えて照合される本文 (docs 全文検索用、表示はされない)。</summary>
public sealed record TreeNode(string Key, string Label, IReadOnlyList<TreeNode>? Children = null,
                              object? Tag = null, string? SearchText = null)
{
    public bool HasChildren => Children is { Count: > 0 };
}

/// <summary>TreeView の行レイアウトと配色をまとめて指定する外観設定。</summary>
public sealed record TreeViewAppearance(
    float RowHeight = 31,
    float RowSpacing = 1,
    float Indent = 16,
    float PaddingX = 8,
    float Radius = 6,
    float FolderFontSize = 12,
    float LeafFontSize = 13,
    float ChevronSize = 10,
    uint? FolderColor = null,
    uint? LeafColor = null,
    uint? HoverColor = null,
    uint? SelectedColor = null,
    uint? HoverBackground = null,
    uint? SelectedBackground = null,
    uint? ChevronColor = null);

/// <summary>
/// ツリービュー (展開/折りたたみ + 選択 + 絞り込み)。行 = インデント + シェブロン (子持ちのみ) + ラベル。
/// <list type="bullet">
/// <item>葉のクリック = <see cref="OnSelect"/> (sender-first)</item>
/// <item>子持ちノード: <c>Tag == null</c> (見出し行) はラベルクリック = 開閉。
///   <c>Tag != null</c> (選択できるページ等) はラベルクリック = <see cref="OnSelect"/> + 展開、
///   開閉はシェブロンのクリック</item>
/// <item>展開状態は <see cref="Expanded"/> に渡したセットに保持 — 呼び出し側が所有すれば
///   ツリーの作り直し (chrome 再構築等) をまたいで生き残る</item>
/// <item>選択ハイライトは <see cref="Selected"/> (Key 一致でアクセント色)</item>
/// <item><see cref="Filter"/> が非空なら、Label/SearchText がマッチするノードとその祖先だけを
///   全展開で表示する (開閉状態は変更しない — クリアで元の表示に戻る)</item>
/// </list>
/// </summary>
[UiComponent]
public sealed partial class TreeView : CompositeControl, ISemanticProvider
{
    /// <summary>ルートノード列。</summary>
    [UiParam] private readonly Bindable<IReadOnlyList<TreeNode>> _roots = new([]);
    /// <summary>展開状態のセット (呼び出し側所有で共有可 — 省略時は自前のセットを遅延生成)。</summary>
    [UiParam] private readonly Bindable<ISet<string>> _expanded = new();

    /// <summary>葉ノード選択 (第一引数 = 発火元)。Tag 付き子持ちノードのラベルクリックでも発火する。</summary>
    [UiEvent] public UiEvent<TreeView, TreeNode> OnSelect;

    /// <summary>選択中ノードの Key (ハイライト表示)。</summary>
    [UiParam] private readonly Bindable<string> _selected = "";

    /// <summary>絞り込みクエリ (大小無視の部分一致)。signal を渡せばタイプ毎に TreeView だけ再構築される。</summary>
    [UiParam] private readonly BindableString _filter = new();

    /// <summary>省略時は従来のコンパクト表示。指定時は行全体の hover/選択表示を使う。</summary>
    [UiParam] private readonly Bindable<TreeViewAppearance> _appearance = new();

    private readonly Signal<int> _version = new(0);   // 開閉トグルで進める (TrackBuild が再構築)
    private ISet<string>? _ownExpanded;   // Expanded 未指定時の自前セット (遅延生成)

    /// <summary>実際に使う展開セット — 呼び出し側所有 (<see cref="Expanded"/>) を優先し、
    /// 未指定なら自前の HashSet を遅延生成する。</summary>
    private ISet<string> ExpandedSet => Expanded.Get() ?? (_ownExpanded ??= new HashSet<string>());

    /// <summary>ノードを展開する (プログラム操作 — 選択パスを見せる等)。</summary>
    public void Expand(string key)
    {
        if (ExpandedSet.Add(key)) _version.Value++;
    }

    public string? FocusedKey { get; private set; }
    public IReadOnlySet<string> SelectedKeys => _selectedKeys;
    private readonly HashSet<string> _selectedKeys = new(StringComparer.Ordinal);
    private string? _selectionAnchor;
    private FocusTarget? _focusTarget;
    public bool IsKeyboardFocused { get; private set; }

    /// <summary>キーボード navigation と pointer selection を同じ selection path に流す。</summary>
    public string? MoveFocus(Key key, bool additive = false)
    {
        var flat = VisibleRows();
        if (flat.Count == 0) return FocusedKey = null;
        int current = flat.FindIndex(x => x.Node.Key == (FocusedKey ?? Selected.Get()));
        int next = key switch
        {
            Key.Home => 0,
            Key.End => flat.Count - 1,
            Key.Up => current <= 0 ? 0 : current - 1,
            Key.Down => current < 0 ? 0 : Math.Min(flat.Count - 1, current + 1),
            Key.Right when current >= 0 && flat[current].Node.HasChildren => ExpandAndStay(flat[current].Node, current),
            Key.Left when current >= 0 && ExpandedSet.Remove(flat[current].Node.Key) => BumpAndStay(current),
            _ => current < 0 ? 0 : current,
        };
        SelectNode(flat[next].Node, additive ? SelectionGesture.Extend : SelectionGesture.Replace, flat);
        return FocusedKey;
    }

    private enum SelectionGesture { Replace, Toggle, Extend }

    private List<(TreeNode Node, int Depth)> VisibleRows()
    {
        var flat = new List<(TreeNode Node, int Depth)>();
        string filter = Filter.Or("").Trim();
        if (filter.Length > 0) FlattenFiltered(FilterTree(Roots.Get(), filter), 0, flat);
        else Flatten(Roots.Get(), ExpandedSet, 0, flat);
        return flat;
    }

    private void SelectNode(TreeNode node, SelectionGesture gesture, List<(TreeNode Node, int Depth)>? visible = null)
    {
        visible ??= VisibleRows();
        if (gesture == SelectionGesture.Replace)
        {
            _selectedKeys.Clear();
            _selectedKeys.Add(node.Key);
            _selectionAnchor = node.Key;
        }
        else if (gesture == SelectionGesture.Toggle)
        {
            if (!_selectedKeys.Remove(node.Key)) _selectedKeys.Add(node.Key);
            _selectionAnchor = node.Key;
        }
        else
        {
            string anchor = _selectionAnchor ?? FocusedKey ?? node.Key;
            int a = visible.FindIndex(x => x.Node.Key == anchor);
            int b = visible.FindIndex(x => x.Node.Key == node.Key);
            if (a < 0 || b < 0) { _selectedKeys.Add(node.Key); _selectionAnchor = node.Key; }
            else
            {
                _selectedKeys.Clear();
                for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) _selectedKeys.Add(visible[i].Node.Key);
            }
        }
        FocusedKey = node.Key;
        OnSelect.Invoke(this, node);
        _version.Value++;
    }

    private void PointerSelect(TreeNode node, PointerEvent e)
        => SelectNode(node, e.Shift ? SelectionGesture.Extend : e.Ctrl ? SelectionGesture.Toggle : SelectionGesture.Replace);

    private bool OnKey(KeyEvent e)
    {
        if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right or Key.Home or Key.End)
        {
            MoveFocus(e.Key, e.Shift);
            return true;
        }
        if (e.Key == Key.Space && FocusedKey is { } key)
        {
            TreeNode? node = VisibleRows().Select(x => x.Node).FirstOrDefault(x => x.Key == key);
            if (node is not null) SelectNode(node, e.Ctrl ? SelectionGesture.Toggle : SelectionGesture.Replace);
            return node is not null;
        }
        return false;
    }

    protected override void OnRealize(UiBuildContext ctx)
    {
        _focusTarget ??= new FocusTarget { OnFocus = focused => IsKeyboardFocused = focused, OnKey = OnKey };
        ctx.AddFocusable(_focusTarget);
    }

    public SemanticNode GetSemantics()
    {
        var children = VisibleRows().Select(x => new SemanticNode(SemanticRole.TreeItem, x.Node.Label, x.Node.Key,
            _selectedKeys.Contains(x.Node.Key) || x.Node.Key == Selected.Get(), false, null)).ToArray();
        return new SemanticNode(SemanticRole.Tree, Children: children);
    }

    private int ExpandAndStay(TreeNode node, int index) { Expand(node.Key); return index; }
    private int BumpAndStay(int index) { _version.Value++; return index; }

    protected override Widget Build()
    {
        _ = _version.Value;   // 開閉を追跡
        string sel = Selected.Get();
        string filter = Filter.Or("").Trim();
        IReadOnlyList<TreeNode> roots = Roots.Get();
        var flat = new List<(TreeNode Node, int Depth)>();
        if (filter.Length > 0)
        {
            // 絞り込み: マッチするノード + 祖先だけを全展開で (開閉セットは触らない)
            FlattenFiltered(FilterTree(roots, filter), 0, flat);
        }
        else
        {
            Flatten(roots, ExpandedSet, 0, flat);
        }
        TreeViewAppearance appearance = Appearance.Get() ?? new TreeViewAppearance(RowHeight: 22, RowSpacing: 3,
            Indent: 12, PaddingX: 4, Radius: 2, FolderFontSize: 13, LeafFontSize: 13);
        var rows = new List<Widget>();
        foreach ((TreeNode n, int depth) in flat)
            rows.Add(StyledRow(n, depth, _selectedKeys.Contains(n.Key) || n.Key == sel,
                filtered: filter.Length > 0, appearance));
        if (rows.Count == 0)
            rows.Add(LinkText(null, "(該当なし)", margin: new Thickness(4, 2, 0, 0)));
        return VStack(appearance.RowSpacing)[rows.ToArray()];
    }

    /// <summary>展開状態に従って可視行を列挙する (テスト用に分離)。</summary>
    internal static void Flatten(IReadOnlyList<TreeNode> roots, ISet<string> expanded, int depth,
                                 List<(TreeNode Node, int Depth)> into)
    {
        foreach (TreeNode n in roots)
        {
            into.Add((n, depth));
            if (n.HasChildren && expanded.Contains(n.Key))
                Flatten(n.Children!, expanded, depth + 1, into);
        }
    }

    /// <summary>クエリにマッチするノード (自身 or 子孫がマッチ) だけを残した木を返す (テスト用に分離)。
    /// マッチ = Label または SearchText の大小無視部分一致。</summary>
    internal static List<TreeNode> FilterTree(IReadOnlyList<TreeNode> roots, string query)
    {
        var result = new List<TreeNode>();
        foreach (TreeNode n in roots)
        {
            List<TreeNode>? kids = n.HasChildren ? FilterTree(n.Children!, query) : null;
            bool self = n.Label.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || (n.SearchText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
            if (self || kids is { Count: > 0 })
                result.Add(n with { Children = kids is { Count: > 0 } ? kids : null });
        }
        return result;
    }

    private static void FlattenFiltered(IReadOnlyList<TreeNode> roots, int depth,
                                        List<(TreeNode Node, int Depth)> into)
    {
        foreach (TreeNode n in roots)
        {
            into.Add((n, depth));
            if (n.HasChildren) FlattenFiltered(n.Children!, depth + 1, into);
        }
    }

    private Widget Row(TreeNode n, int depth, bool selected, bool filtered)
    {
        // 他フレームワークのツリー同様: 左寄せの LinkText 行、1 段 = 12px、
        // 葉はシェブロン列 (12px) 分を足して親ラベルと左端を揃える
        float indent = 4 + depth * 12;
        if (!n.HasChildren)
            return LinkText(_ => OnSelect.Invoke(this, n), n.Label, active: selected,
                hAlign: Align.Stretch, margin: new Thickness(indent + 12, 0, 0, 0));

        bool open = filtered || ExpandedSet.Contains(n.Key);
        // Tag 付きの子持ち (選択できるページ) はラベル = 選択、シェブロン = 開閉。
        // Tag なし (グループ見出し) は従来どおりラベルでも開閉
        Action<LinkText> onLabel = n.Tag is null
            ? _ => Toggle(n)
            : _ => { Expand(n.Key); OnSelect.Invoke(this, n); };
        return HStack(2)[
            Icon(open ? IconKind.ChevronDown : IconKind.ChevronRight, 10, stroke: 1.5f,
                 onClick: _ => Toggle(n), margin: new Thickness(indent, 3, 0, 0)),
            LinkText(onLabel, n.Label, active: selected)];
    }

    private Widget StyledRow(TreeNode n, int depth, bool selected, bool filtered, TreeViewAppearance appearance)
    {
        bool open = filtered || ExpandedSet.Contains(n.Key);
        Action<PointerEvent> activate = e =>
        {
            if (n.HasChildren && n.Tag is null && !e.Ctrl && !e.Shift) Toggle(n);
            else
            {
                if (n.HasChildren) Expand(n.Key);
                PointerSelect(n, e);
            }
        };
        return new TreeViewRow(n.Label, depth, n.HasChildren, open, selected, appearance,
            activate, n.HasChildren ? () => Toggle(n) : null, () => _focusTarget);
    }

    private void Toggle(TreeNode n)
    {
        ISet<string> expanded = ExpandedSet;
        if (!expanded.Remove(n.Key)) expanded.Add(n.Key);
        _version.Value++;
    }
}

/// <summary>背景を含む TreeView の一行。TreeViewAppearance 指定時だけ利用する。</summary>
internal sealed class TreeViewRow(
    string label,
    int depth,
    bool hasChildren,
    bool open,
    bool selected,
    TreeViewAppearance appearance,
    Action<PointerEvent> activate,
    Action? toggle,
    Func<FocusTarget?>? focus = null) : Widget
{
    public TreeViewRow(string label, int depth, bool hasChildren, bool open, bool selected,
        TreeViewAppearance appearance, Action activate, Action? toggle)
        : this(label, depth, hasChildren, open, selected, appearance, _ => activate(), toggle) { }

    public override string? DebugDetail => label;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float fontSize = hasChildren ? appearance.FolderFontSize : appearance.LeafFontSize;
        float labelX = appearance.PaddingX + depth * appearance.Indent
                     + (hasChildren ? appearance.ChevronSize + 7 : 0);
        float intrinsic = labelX + ctx.Font.Measure(label, fontSize).width + appearance.PaddingX;
        float width = float.IsFinite(c.MaxW) ? c.MaxW : intrinsic;
        Size = c.Constrain(new Size(width, appearance.RowHeight));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx)
    {
        float fontSize = hasChildren ? appearance.FolderFontSize : appearance.LeafFontSize;
        float prefix = appearance.PaddingX + depth * appearance.Indent
                     + (hasChildren ? appearance.ChevronSize + 7 : 0);
        return prefix + ctx.Font.Measure(label, fontSize).width + appearance.PaddingX;
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);

        UiNode background = ctx.Canvas.AddChild(node);
        var backgroundScene = new Scene2D();
        backgroundScene.FillRoundedRect(Color2D.White, 0, 0, Size.Width, appearance.RowHeight, appearance.Radius);
        background.Content = backgroundScene;

        float fontSize = hasChildren ? appearance.FolderFontSize : appearance.LeafFontSize;
        float baseX = appearance.PaddingX + depth * appearance.Indent;
        float labelX = baseX + (hasChildren ? appearance.ChevronSize + 7 : 0);
        float textHeight = ctx.Font.Measure("Mg", fontSize).height;
        float baseline = (appearance.RowHeight - textHeight) / 2 + ctx.Font.Ascent(fontSize);

        UiNode text = ctx.Canvas.AddChild(node);
        text.Z = 1;
        var textScene = new Scene2D();
        ctx.Font.AppendText(textScene, label, labelX, baseline, fontSize, Color2D.White);
        text.Content = textScene;

        UiNode? chevron = null;
        if (hasChildren)
        {
            chevron = ctx.Canvas.AddChild(node);
            chevron.Z = 1;
            var iconScene = new Scene2D();
            Icon.Draw(iconScene, open ? IconKind.ChevronDown : IconKind.ChevronRight,
                appearance.ChevronSize, 1.5f);
            chevron.Content = iconScene;
            chevron.Transform = Affine2D.Translate(baseX,
                (appearance.RowHeight - appearance.ChevronSize) / 2);
        }

        ctx.Effect(() =>
        {
            Theme theme = ctx.Theme.Value;
            bool hovered = Hovered.Value;
            uint normalText = hasChildren
                ? appearance.FolderColor ?? theme.TextMuted
                : appearance.LeafColor ?? theme.Text;
            text.Color = selected
                ? appearance.SelectedColor ?? theme.Primary
                : hovered ? appearance.HoverColor ?? theme.Text : normalText;
            background.Color = selected
                ? appearance.SelectedBackground ?? theme.SurfaceAlt
                : hovered ? appearance.HoverBackground ?? theme.SurfaceAlt : 0x00000000u;
            if (chevron is not null)
                chevron.Color = appearance.ChevronColor ?? theme.TextMuted;
        });

        ctx.AddHit(node, new Rect(0, 0, Size.Width, appearance.RowHeight),
            onClickPos: activate, onHover: h => Hovered.Value = h, focus: focus?.Invoke(), cursor: CursorKind.Hand);
        if (hasChildren && toggle is not null)
        {
            float hitX = MathF.Max(0, baseX - 3);
            ctx.AddHit(node, new Rect(hitX, 0, appearance.ChevronSize + 6, appearance.RowHeight),
                onClick: toggle, onHover: h => Hovered.Value = h, cursor: CursorKind.Hand);
        }
    }
}
