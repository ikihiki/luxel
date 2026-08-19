using Luxel.Graphics.TwoD;
using Luxel.Typography.TwoD;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Controls;

public sealed record TabStripItem(
    string Key,
    string Title,
    Signal<bool>? Marker = null,
    string? Badge = null,
    string? Tooltip = null,
    bool Closable = true,
    bool Disabled = false);

public sealed record TabDropRequest(string Key, int Index, object? SourceStrip, object? TargetStrip);

[UiComponent]
public sealed partial class TabStrip : Widget, ISemanticProvider
{
    private const float StripH = 32f;
    private const float PadX = 10f;
    private const float CloseW = 20f;
    private const float OverflowW = 28f;
    private const float MinTabW = 64f;
    private const float MaxTabW = 190f;

    [UiParam] private readonly Bindable<IReadOnlyList<TabStripItem>> _items = new([]);
    [UiParam] private readonly Bindable<string?> _selectedKey = new();
    [UiParam] private readonly Bindable<object?> _dragChannel = new();
    [UiEvent] public UiEvent<TabStrip, string> OnSelect;
    [UiEvent] public UiEvent<TabStrip, string> OnCloseRequest;
    [UiEvent] public UiEvent<TabStrip, TabDropRequest> OnDropRequest;

    public sealed record TabDrag(object Channel, TabStrip Source, string Key, string Title);

    private float[] _tabX = [];
    private float[] _tabW = [];
    private float _width;
    private float _contentWidth;
    private float _viewportWidth;
    private float _scrollOffset;
    private FocusTarget? _focusTarget;

    public string? FocusedKey { get; private set; }
    public bool IsKeyboardFocused { get; private set; }
    public int OverflowCount { get; private set; }
    public float ScrollOffset => _scrollOffset;
    private object Channel => DragChannel.Get() ?? this;

    public static void ValidateItems(IEnumerable<TabStripItem> items)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (TabStripItem item in items)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(item.Key);
            if (!keys.Add(item.Key)) throw new ArgumentException($"Duplicate tab key: {item.Key}", nameof(items));
        }
    }

    public string? MoveFocus(Key key)
    {
        TabStripItem[] enabled = Items.Get().Where(x => !x.Disabled).ToArray();
        if (enabled.Length == 0) return FocusedKey = null;
        int current = Array.FindIndex(enabled, x => x.Key == (FocusedKey ?? SelectedKey.Get()));
        int next = key switch
        {
            Key.Home => 0,
            Key.End => enabled.Length - 1,
            Key.Left => current <= 0 ? enabled.Length - 1 : current - 1,
            Key.Right => current < 0 || current == enabled.Length - 1 ? 0 : current + 1,
            _ => current < 0 ? 0 : current,
        };
        FocusedKey = enabled[next].Key;
        EnsureVisible(FocusedKey);
        OnSelect.Invoke(this, FocusedKey);
        return FocusedKey;
    }

    private bool OnKey(KeyEvent e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            MoveFocus(e.Key);
            return true;
        }
        if (e.Key is Key.Enter or Key.Space && FocusedKey is { } focused)
        {
            TabStripItem? item = Items.Get().FirstOrDefault(x => x.Key == focused);
            if (item is { Disabled: false }) OnSelect.Invoke(this, focused);
            return item is not null;
        }
        if (e.Key == Key.Delete && FocusedKey is { } closeKey)
        {
            TabStripItem? item = Items.Get().FirstOrDefault(x => x.Key == closeKey);
            if (item is { Closable: true, Disabled: false }) OnCloseRequest.Invoke(this, closeKey);
            return item is not null;
        }
        return false;
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        IReadOnlyList<TabStripItem> items = Items.Get();
        ValidateItems(items);
        _width = ResolveW(c, ctx, float.IsFinite(c.MaxW) ? c.MaxW : 320f);
        _tabX = new float[items.Count + 1];
        _tabW = new float[items.Count];
        float fs = ctx.Theme.FontSm;
        for (int i = 0; i < items.Count; i++)
        {
            TabStripItem item = items[i];
            string title = item.Badge is null ? item.Title : $"{item.Title} {item.Badge}";
            float close = item.Closable ? CloseW : 0;
            _tabW[i] = Math.Clamp(ctx.Font.Measure(title, fs).width + PadX * 2 + close, MinTabW, MaxTabW);
            _tabX[i + 1] = _tabX[i] + _tabW[i];
        }
        _contentWidth = _tabX[^1];
        _viewportWidth = MathF.Max(0, _width - (_contentWidth > _width ? OverflowW : 0));
        string? selected = SelectedKey.Get();
        if (FocusedKey is null || !items.Any(x => x.Key == FocusedKey && !x.Disabled))
            FocusedKey = items.FirstOrDefault(x => x.Key == selected && !x.Disabled)?.Key
                ?? items.FirstOrDefault(x => !x.Disabled)?.Key;
        EnsureVisible(selected ?? FocusedKey);
        UpdateOverflow(items.Count);
        Size = c.Constrain(new Size(_width, StripH));
    }

    private void EnsureVisible(string? key)
    {
        if (key is null || _viewportWidth <= 0) return;
        IReadOnlyList<TabStripItem> items = Items.Get();
        int index = -1;
        for (int i = 0; i < items.Count; i++) if (items[i].Key == key) { index = i; break; }
        if (index < 0 || index >= _tabW.Length) return;
        float left = _tabX[index], right = left + _tabW[index];
        if (left < _scrollOffset) _scrollOffset = left;
        else if (right > _scrollOffset + _viewportWidth) _scrollOffset = right - _viewportWidth;
        ClampScroll();
    }

    private void ClampScroll()
        => _scrollOffset = Math.Clamp(_scrollOffset, 0, MathF.Max(0, _contentWidth - _viewportWidth));

    private void UpdateOverflow(int count)
    {
        int hidden = 0;
        for (int i = 0; i < count && i < _tabW.Length; i++)
            if (_tabX[i] < _scrollOffset || _tabX[i] + _tabW[i] > _scrollOffset + _viewportWidth) hidden++;
        OverflowCount = hidden;
    }

    public Point? TabCenterOf(string key)
    {
        IReadOnlyList<TabStripItem> items = Items.Get();
        for (int i = 0; i < items.Count && i < _tabW.Length; i++)
            if (items[i].Key == key)
                return new Point(WorldPos.X + _tabX[i] - _scrollOffset + (_tabW[i] - (items[i].Closable ? CloseW : 0)) / 2,
                    WorldPos.Y + StripH / 2);
        return null;
    }

    public Point? CloseCenterOf(string key)
    {
        IReadOnlyList<TabStripItem> items = Items.Get();
        for (int i = 0; i < items.Count && i < _tabW.Length; i++)
            if (items[i].Key == key && items[i].Closable)
                return new Point(WorldPos.X + _tabX[i] - _scrollOffset + _tabW[i] - CloseW / 2, WorldPos.Y + StripH / 2);
        return null;
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        node.Clip = new RectClip(0, 0, _width, StripH);
        _focusTarget ??= new FocusTarget { OnFocus = focused => IsKeyboardFocused = focused, OnKey = OnKey };
        ctx.AddFocusable(_focusTarget);

        UiNode baseline = ctx.Canvas.AddChild(node);
        var baselineScene = new Scene2D(); baselineScene.FillRect(Color2D.White, 0, StripH - 1, _width, 1);
        baseline.Content = baselineScene;
        ctx.Effect(() => baseline.Color = ctx.Theme.Value.BorderColor);

        IReadOnlyList<TabStripItem> items = Items.Get();
        float fs = ctx.Theme.Peek().FontSm;
        float textY = (StripH - ctx.Font.Measure("Mg", fs).height) / 2 + ctx.Font.Ascent(fs);

        UiNode indicator = ctx.Canvas.AddChild(node); indicator.Z = 4; indicator.Opacity = 0;
        var indicatorScene = new Scene2D(); indicatorScene.FillRect(Color2D.White, -1, 3, 2, StripH - 6);
        indicator.Content = indicatorScene;
        ctx.Effect(() => indicator.Color = ctx.Theme.Value.Primary);
        int InsertIndexAt(float lx)
        {
            float contentX = lx + _scrollOffset;
            for (int i = 0; i < _tabW.Length; i++) if (contentX < _tabX[i] + _tabW[i] / 2) return i;
            return _tabW.Length;
        }
        ctx.AddHit(node, new Rect(0, 0, _viewportWidth, StripH),
            acceptsDrop: payload => payload is TabDrag drag && Equals(drag.Channel, Channel),
            onDropHover: hover => { if (!hover) indicator.Opacity = 0; },
            onDropMove: (_, e) =>
            {
                int index = InsertIndexAt(e.X);
                indicator.Opacity = 1;
                indicator.Transform = Affine2D.Translate(_tabX[Math.Min(index, _tabX.Length - 1)] - _scrollOffset, 0);
            },
            onDrop: (payload, e) =>
            {
                indicator.Opacity = 0;
                if (payload is TabDrag drag)
                    OnDropRequest.Invoke(this, new TabDropRequest(drag.Key, InsertIndexAt(e.X), drag.Source, this));
            });

        for (int i = 0; i < items.Count; i++)
        {
            TabStripItem item = items[i];
            float x = _tabX[i] - _scrollOffset, w = _tabW[i];
            if (x + w <= 0 || x >= _viewportWidth) continue;
            bool selected = item.Key == SelectedKey.Get();

            UiNode bg = ctx.Canvas.AddChild(node);
            var bgScene = new Scene2D(); bgScene.FillRoundedRect(Color2D.White, x + 1, 2, w - 2, StripH - 3, 4);
            bg.Content = bgScene; bg.Opacity = selected ? 1 : 0;
            ctx.Effect(() => bg.Color = ctx.Theme.Value.SurfaceAlt);

            if (selected)
            {
                UiNode line = ctx.Canvas.AddChild(node); line.Z = 2;
                var lineScene = new Scene2D(); lineScene.FillRect(Color2D.White, x + 8, StripH - 2, MathF.Max(0, w - 16), 2);
                line.Content = lineScene;
                ctx.Effect(() => line.Color = ctx.Theme.Value.Primary);
            }

            UiNode label = ctx.Canvas.AddChild(node); label.Z = 1;
            float closeW = item.Closable ? CloseW : 0;
            label.Clip = new RectClip(x, 0, MathF.Max(0, w - closeW), StripH);
            var textScene = new Scene2D();
            string title = item.Badge is null ? item.Title : $"{item.Title} {item.Badge}";
            ctx.Font.AppendText(textScene, title, x + PadX, textY, fs, Color2D.White);
            label.Content = textScene;
            ctx.Effect(() => label.Color = item.Disabled ? ctx.Theme.Value.TextMuted & 0x80ffffffu
                : selected ? ctx.Theme.Value.Text : ctx.Theme.Value.TextMuted);

            if (item.Marker is not null)
            {
                UiNode marker = ctx.Canvas.AddChild(node); marker.Z = 2;
                var markerScene = new Scene2D(); markerScene.FillRoundedRect(Color2D.White, x + 4, 5, 6, 6, 3);
                marker.Content = markerScene;
                ctx.Effect(() => { marker.Opacity = item.Marker.Value ? 1 : 0; marker.Color = ctx.Theme.Value.Primary; });
            }

            if (item.Closable)
            {
                UiNode close = ctx.Canvas.AddChild(node); close.Z = 2;
                var closeScene = new Scene2D(); ctx.Font.AppendText(closeScene, "×", x + w - CloseW + 4, textY, fs, Color2D.White);
                close.Content = closeScene;
                ctx.Effect(() => close.Color = item.Disabled ? ctx.Theme.Value.TextMuted & 0x80ffffffu : ctx.Theme.Value.TextMuted);
                if (!item.Disabled)
                    ctx.AddHit(node, new Rect(x + w - CloseW, 0, CloseW, StripH), focus: _focusTarget,
                        onClick: () => OnCloseRequest.Invoke(this, item.Key), cursor: CursorKind.Hand);
            }

            var tooltipOpen = new Signal<bool>(false);
            if (!string.IsNullOrWhiteSpace(item.Tooltip))
            {
                Widget bubble = Border(background: Bind.From(() => ctx.Theme.Value.Text), rounded: 5,
                    padding: new Thickness(8, 5))[Text(item.Tooltip!, 13, color: Bind.From(() => ctx.Theme.Value.OnAccent))];
                float anchorX = x;
                ctx.RegisterOverlay(new OverlayEntry
                {
                    Open = tooltipOpen,
                    Content = bubble,
                    Placement = OverlayPlacement.Above,
                    Anchor = () => new Rect(WorldPos.X + anchorX, WorldPos.Y, w, StripH),
                    DismissOnOutside = false,
                });
            }

            if (!item.Disabled)
            {
                bool started = false;
                ctx.AddHit(node, new Rect(x, 0, w - closeW, StripH), focus: _focusTarget,
                    onHover: hover => tooltipOpen.Value = hover,
                    onDragStart: _ => started = false,
                    onDrag: e =>
                    {
                        if (started || ctx.Host is null || MathF.Abs(e.DeltaX) + MathF.Abs(e.DeltaY) <= 4) return;
                        started = true;
                        var ghost = new Scene2D();
                        ghost.FillRoundedRect(ctx.Theme.Peek().SurfaceAlt, 0, 0, MathF.Min(w, 140), StripH - 6, 4);
                        ctx.Font.AppendText(ghost, item.Title, PadX, textY - 3, fs, ctx.Theme.Peek().Text);
                        ctx.Host.BeginDrag(new TabDrag(Channel, this, item.Key, item.Title), ghost, 20, StripH / 2);
                    },
                    onDragEnd: _ =>
                    {
                        if (started) return;
                        FocusedKey = item.Key;
                        EnsureVisible(item.Key);
                        OnSelect.Invoke(this, item.Key);
                    }, cursor: CursorKind.Hand);
            }
            else if (!string.IsNullOrWhiteSpace(item.Tooltip))
                ctx.AddHit(node, new Rect(x, 0, w, StripH), onHover: hover => tooltipOpen.Value = hover);
        }

        if (_contentWidth > _width)
        {
            UiNode overflow = ctx.Canvas.AddChild(node); overflow.Z = 5;
            var overflowScene = new Scene2D();
            overflowScene.FillRect(ctx.Theme.Peek().SurfaceAlt, _width - OverflowW, 0, OverflowW, StripH);
            ctx.Font.AppendText(overflowScene, $"+{OverflowCount}", _width - OverflowW + 4, textY, fs, ctx.Theme.Peek().TextMuted);
            overflow.Content = overflowScene;
            ctx.AddHit(node, new Rect(_width - OverflowW, 0, OverflowW, StripH), focus: _focusTarget,
                onClick: () =>
                {
                    _scrollOffset = Math.Min(_contentWidth - _viewportWidth, _scrollOffset + _viewportWidth * .75f);
                    UpdateOverflow(items.Count);
                    MarkNeedsRealize();
                }, cursor: CursorKind.Hand);
            ctx.AddScroll(node, new Rect(0, 0, _width, StripH), delta =>
            {
                _scrollOffset = Math.Clamp(_scrollOffset - delta, 0, MathF.Max(0, _contentWidth - _viewportWidth));
                UpdateOverflow(items.Count);
                MarkNeedsRealize();
            });
        }
    }

    public SemanticNode GetSemantics()
        => new(SemanticRole.TabList, Children: Items.Get().Select(item =>
            new SemanticNode(SemanticRole.Tab, item.Title, item.Key, item.Key == SelectedKey.Get(), item.Disabled, item.Tooltip)).ToArray());
}
