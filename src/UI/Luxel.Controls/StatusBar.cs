using Luxel.Graphics.TwoD;
using Luxel.Typography.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

public enum StatusBarRegion { Leading, Center, Trailing }

public sealed record StatusBarItem(
    string Key,
    Widget Content,
    StatusBarRegion Region = StatusBarRegion.Leading,
    int Priority = 0,
    bool Visible = true,
    bool Separator = false,
    float PreferredWidth = 80f);

/// <summary>A width-aware status surface with prioritized contributions and an actual overflow affordance.</summary>
[UiComponent]
public sealed partial class StatusBar : Widget, ISemanticProvider
{
    public const float BarH = 26f;
    private const float PadX = 10f;
    private const float Gap = 8f;
    private const float SeparatorSpace = 9f;
    private const float OverflowWidth = 38f;

    [UiParam] private readonly Bindable<Widget[]> _left = new([]);
    [UiParam] private readonly Bindable<Widget[]> _center = new([]);
    [UiParam] private readonly Bindable<Widget[]> _right = new([]);
    [UiParam] private readonly Bindable<IReadOnlyList<StatusBarItem>> _items = new([]);

    private sealed class LayoutEntry(StatusBarItem item, float width)
    {
        public StatusBarItem Item { get; } = item;
        public float Width { get; } = width;
        public float X { get; set; }
    }

    private readonly List<LayoutEntry> _layout = [];
    private readonly List<float> _separatorX = [];
    private readonly List<string> _collapsedKeys = [];
    private float _width;
    private bool _showOverflow;

    public IReadOnlyList<string> CollapsedKeys => _collapsedKeys;
    public IReadOnlyList<string> VisibleKeys => _layout.Where(x => !x.Item.Key.StartsWith("$legacy:", StringComparison.Ordinal))
        .Select(x => x.Item.Key).ToArray();
    public int SeparatorCount => _separatorX.Count;
    public bool HasOverflow => _showOverflow;

    public static IReadOnlyList<StatusBarItem> Collapse(IReadOnlyList<StatusBarItem> items, float availableWidth)
    {
        StatusBarItem[] visible = items.Where(x => x.Visible).ToArray();
        float width = visible.Sum(ContributionWidth);
        if (width <= availableWidth) return visible;
        var keep = visible.ToList();
        foreach (StatusBarItem item in visible.OrderBy(x => x.Priority).ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            if (width <= availableWidth) break;
            keep.Remove(item);
            width -= ContributionWidth(item);
        }
        return keep;
    }

    private static float ContributionWidth(StatusBarItem item)
        => MathF.Max(0, item.PreferredWidth) + Gap + (item.Separator ? SeparatorSpace : 0);

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        _width = ResolveW(c, ctx, float.IsFinite(c.MaxW) ? c.MaxW : 480f);
        _layout.Clear();
        _separatorX.Clear();
        _collapsedKeys.Clear();

        var all = new List<StatusBarItem>();
        AddLegacy(all, Left.Get(), StatusBarRegion.Leading, "left", ctx);
        AddLegacy(all, Center.Get(), StatusBarRegion.Center, "center", ctx);
        AddLegacy(all, Right.Get(), StatusBarRegion.Trailing, "right", ctx);
        IReadOnlyList<StatusBarItem> configured = Items.Get();
        ValidateKeys(configured);
        all.AddRange(configured.Where(x => x.Visible));

        float available = MathF.Max(0, _width - PadX * 2);
        StatusBarItem[] legacy = all.Where(IsLegacy).ToArray();
        StatusBarItem[] contributions = all.Where(x => !IsLegacy(x)).ToArray();
        float legacyWidth = legacy.Sum(ContributionWidth);
        IReadOnlyList<StatusBarItem> visible = Collapse(contributions, MathF.Max(0, available - legacyWidth));
        var visibleKeys = visible.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        foreach (StatusBarItem item in contributions) if (!visibleKeys.Contains(item.Key)) _collapsedKeys.Add(item.Key);
        _showOverflow = _collapsedKeys.Count > 0;
        if (_showOverflow)
        {
            visible = Collapse(contributions, MathF.Max(0, available - legacyWidth - OverflowWidth));
            visibleKeys = visible.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
            _collapsedKeys.Clear();
            foreach (StatusBarItem item in contributions) if (!visibleKeys.Contains(item.Key)) _collapsedKeys.Add(item.Key);
        }

        var selected = legacy.Concat(visible).ToArray();
        foreach (StatusBarItem item in selected)
        {
            float requested = IsLegacy(item)
                ? MathF.Max(0, item.Content.MaxIntrinsicWidth(BarH, ctx))
                : MathF.Max(0, item.PreferredWidth);
            item.Content.Layout(new Constraints(0, requested, 0, BarH - 1), ctx, parentUsesSize: true);
            float width = MathF.Max(requested, item.Content.Size.Width);
            _layout.Add(new LayoutEntry(item, width));
        }

        PlaceRegion(StatusBarRegion.Leading, PadX, leftToRight: true);
        float trailingRight = _width - PadX - (_showOverflow ? OverflowWidth : 0);
        PlaceRegion(StatusBarRegion.Trailing, trailingRight, leftToRight: false);

        LayoutEntry[] center = _layout.Where(x => x.Item.Region == StatusBarRegion.Center).ToArray();
        float centerWidth = RegionWidth(center);
        float centerX = MathF.Max(PadX, (_width - centerWidth) / 2);
        if (center.Length > 0)
        {
            float leftEnd = RegionEnd(StatusBarRegion.Leading, PadX);
            float rightStart = RegionStart(StatusBarRegion.Trailing, trailingRight);
            centerX = Math.Clamp(centerX, leftEnd, MathF.Max(leftEnd, rightStart - centerWidth));
            PlaceEntries(center, centerX, true);
        }

        foreach (LayoutEntry entry in _layout)
        {
            entry.Item.Content.Offset = new Point(entry.X, (BarH - 1 - entry.Item.Content.Size.Height) / 2 + 1);
            if (entry.Item.Separator) _separatorX.Add(entry.X - SeparatorSpace / 2);
        }
        Size = c.Constrain(new Size(_width, BarH));
    }

    private static void ValidateKeys(IReadOnlyList<StatusBarItem> items)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (StatusBarItem item in items)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Key);
            if (!keys.Add(item.Key)) throw new ArgumentException($"Duplicate status item key: {item.Key}", nameof(items));
        }
    }

    private static bool IsLegacy(StatusBarItem item) => item.Key.StartsWith("$legacy:", StringComparison.Ordinal);

    private static void AddLegacy(List<StatusBarItem> target, Widget[] widgets, StatusBarRegion region, string prefix, LayoutContext ctx)
    {
        for (int i = 0; i < widgets.Length; i++)
            target.Add(new StatusBarItem($"$legacy:{prefix}:{i}", widgets[i], region, int.MaxValue,
                PreferredWidth: MathF.Max(0, widgets[i].MaxIntrinsicWidth(BarH, ctx))));
    }

    private void PlaceRegion(StatusBarRegion region, float edge, bool leftToRight)
        => PlaceEntries(_layout.Where(x => x.Item.Region == region).ToArray(), edge, leftToRight);

    private static float RegionWidth(IReadOnlyList<LayoutEntry> entries)
    {
        float width = 0;
        for (int i = 0; i < entries.Count; i++) width += entries[i].Width + (i > 0 ? Gap : 0)
            + (entries[i].Item.Separator ? SeparatorSpace : 0);
        return width;
    }

    private static void PlaceEntries(IReadOnlyList<LayoutEntry> entries, float edge, bool leftToRight)
    {
        if (leftToRight)
        {
            float x = edge;
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) x += Gap;
                if (entries[i].Item.Separator) x += SeparatorSpace;
                entries[i].X = x;
                x += entries[i].Width;
            }
        }
        else
        {
            float x = edge;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                x -= entries[i].Width;
                entries[i].X = x;
                if (entries[i].Item.Separator) x -= SeparatorSpace;
                if (i > 0) x -= Gap;
            }
        }
    }

    private float RegionEnd(StatusBarRegion region, float fallback)
    {
        LayoutEntry[] entries = _layout.Where(x => x.Item.Region == region).ToArray();
        return entries.Length == 0 ? fallback : entries.Max(x => x.X + x.Width) + Gap;
    }

    private float RegionStart(StatusBarRegion region, float fallback)
    {
        LayoutEntry[] entries = _layout.Where(x => x.Item.Region == region).ToArray();
        return entries.Length == 0 ? fallback : entries.Min(x => x.X) - Gap;
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        var background = new Scene2D(); background.FillRect(Color2D.White, 0, 0, _width, BarH);
        node.Content = background;
        ctx.Effect(() => node.Color = ctx.Theme.Value.SurfaceAlt);

        UiNode hairline = ctx.Canvas.AddChild(node);
        var hairlineScene = new Scene2D(); hairlineScene.FillRect(Color2D.White, 0, 0, _width, 1);
        hairline.Content = hairlineScene;
        ctx.Effect(() => hairline.Color = ctx.Theme.Value.BorderColor);

        foreach (float x in _separatorX)
        {
            UiNode separator = ctx.Canvas.AddChild(node); separator.Z = 1;
            var scene = new Scene2D(); scene.FillRect(Color2D.White, x, 6, 1, BarH - 12);
            separator.Content = scene;
            ctx.Effect(() => separator.Color = ctx.Theme.Value.BorderColor);
        }

        foreach (LayoutEntry entry in _layout) entry.Item.Content.Realize(ctx, node, WorldPos);

        if (_showOverflow)
        {
            UiNode overflow = ctx.Canvas.AddChild(node); overflow.Z = 2;
            float fs = ctx.Theme.Peek().FontSm;
            float baseline = (BarH - ctx.Font.Measure("Mg", fs).height) / 2 + ctx.Font.Ascent(fs);
            var scene = new Scene2D();
            scene.FillRoundedRect(Color2D.White, _width - PadX - OverflowWidth + 3, 4, OverflowWidth - 3, BarH - 8, 4);
            ctx.Font.AppendText(scene, $"+{_collapsedKeys.Count}", _width - PadX - OverflowWidth + 10, baseline, fs, Color2D.White);
            overflow.Content = scene;
            ctx.Effect(() => overflow.Color = ctx.Theme.Value.TextMuted);
        }
    }

    public override IEnumerable<Widget> DebugChildren() => _layout.Select(x => x.Item.Content);

    public SemanticNode GetSemantics()
        => new(SemanticRole.Status, Children: _layout.Where(x => !IsLegacy(x.Item)).Select(x =>
            new SemanticNode(SemanticRole.Status, x.Item.Key, x.Item.Key)).ToArray());
}
