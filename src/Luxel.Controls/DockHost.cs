using Luxel.TwoD;
using Luxel.UI;
using Luxel.Workbench;

namespace Luxel.Controls;

/// <summary>DockHost がタブ id から引く 1 ドキュメント分の表示情報。CreateView は初回だけ呼ばれ、
/// ビュー widget は DockHost がキャッシュする (タブ切替・レイアウト変更で状態が生き残る)。</summary>
public sealed record DockItem(string Title, Func<Widget> CreateView, Signal<bool>? Dirty = null);

/// <summary>
/// <see cref="DockTree"/> (ADR-0010) を描く container (ADR-0014)。分割 = <see cref="Splitter"/>、
/// 葉 = <see cref="DocumentTabs"/> + アクティブタブのビュー。タブの操作 (クリック/並べ替え/
/// グループ間移動/端へのドロップ分割) は tree signal を書き換え、TrackBuild が自動で組み直す —
/// レイアウトの真実は常に DockTree。× は <see cref="OnCloseTab"/> をシェルへ返す
/// (ダーティ確認の後 tree から外すのはシェルの責務。既定 <see cref="CloseRemoves"/>=true なら自動で外す)。
/// </summary>
[UiComponent]
public sealed partial class DockHost : CompositeControl
{
    /// <summary>レイアウトの真実 (シェルが所有)。DockHost は読み書きする。</summary>
    [UiParam] private readonly Bindable<Signal<DockTree>> _tree = new();
    /// <summary>タブ id → 表示情報。</summary>
    [UiParam] private readonly Bindable<Func<string, DockItem>> _resolve = new();
    /// <summary>× で自動的に tree から外す (false = OnCloseTab だけ発火しシェルが外す)。</summary>
    [UiParam] private readonly Bindable<bool> _closeRemoves = true;

    /// <summary>タブの × が押された (id)。</summary>
    [UiEvent] public UiEvent<DockHost, string> OnCloseTab;

    private readonly Dictionary<string, Widget> _views = new();     // ビューは Rebuild をまたいで生き残る
    private readonly List<DocumentTabs> _strips = new();             // 直近 Build のタブ帯 (座標ヘルパ用)

    /// <summary>id のビュー (テスト/検査用)。未実体化なら null。</summary>
    public Widget? ViewOf(string id) => _views.GetValueOrDefault(id);

    /// <summary>タブの画面中心 (play/テスト用)。見つからなければ null。</summary>
    public Point? TabCenter(string id)
    {
        foreach (DocumentTabs strip in _strips)
            if (strip.TabCenterOf(id) is { } p) return p;
        return null;
    }

    protected override Widget Build()
    {
        Signal<DockTree> sig = Tree.Get();
        DockTree t = sig.Value;   // tracked — tree 変化で自動 Rebuild
        _strips.Clear();

        // 閉じたタブのビューを掃除
        var live = t.Groups.SelectMany(g => g.Tabs).ToHashSet();
        foreach (string k in _views.Keys.Where(k => !live.Contains(k)).ToList())
        {
            (_views[k] as IDisposable)?.Dispose();
            _views.Remove(k);
        }
        // ルートを FillPanel で包む — 親が高さ無制約 (VStack 内等) でも 0 に潰れない
        return new DockFillPanel { Child = BuildNode(t.Root, sig) };
    }

    private Widget BuildNode(DockNode n, Signal<DockTree> sig) => n switch
    {
        DockGroup g => BuildGroup(g, sig),
        DockSplit s => BuildSplit(s, sig),
        _ => throw new InvalidOperationException(),
    };

    private Widget BuildGroup(DockGroup g, Signal<DockTree> sig)
    {
        Func<string, DockItem> resolve = Resolve.Get();
        var tabs = new List<DocTab>(g.Tabs.Count);
        foreach (string id in g.Tabs)
        {
            DockItem item = resolve(id);
            tabs.Add(new DocTab(id, item.Title, item.Dirty));
            if (!_views.ContainsKey(id)) _views[id] = item.CreateView();
        }
        string? activeId = g.Active >= 0 && g.Active < g.Tabs.Count ? g.Tabs[g.Active] : null;

        DocumentTabs strip = Kit.DocumentTabs(tabs, active: activeId,
            dragChannel: this,   // この DockHost 内の帯どうしでタブを移せる
            onActivate: (_, id) => sig.Value = sig.Value.ActivateTab(id),
            onClose: (_, id) =>
            {
                OnCloseTab.Invoke(this, id);
                if (CloseRemoves.Get()) sig.Value = sig.Value.RemoveTab(id);
            },
            onDropTab: (_, id, index) => sig.Value = sig.Value.MoveTab(id, g.Id, index),
            hAlign: Align.Stretch);
        _strips.Add(strip);

        Widget content = activeId is not null ? _views[activeId] : Kit.Spacer();
        var zone = new DockDropZone
        {
            Child = content,
            Channel = this,
            OnDropped = (id, side) => sig.Value = side is null
                ? sig.Value.MoveTab(id, g.Id)
                : sig.Value.Dock(id, g.Id, side.Value),
        };
        zone.GridRow(1);
        strip.GridRow(0);
        return Kit.Grid(rows: [GridLength.Px(DocumentTabs.StripH), GridLength.Star()])[strip, zone];
    }

    private Widget BuildSplit(DockSplit s, Signal<DockTree> sig)
        => new DockSplitPanel(
            s.Horizontal,
            s.Children.Select(c => BuildNode(c, sig)).ToArray(),
            Enumerable.Range(0, s.Children.Count).Select(i => i < s.Sizes.Count ? s.Sizes[i] : 1f / s.Children.Count).ToArray(),
            (gap, deltaFrac) =>
            {
                DockTree t = sig.Value;
                if (t.Split(s.Id) is not { } cur) return;
                var sizes = Enumerable.Range(0, cur.Children.Count)
                    .Select(i => i < cur.Sizes.Count ? cur.Sizes[i] : 1f / cur.Children.Count).ToArray();
                if (gap < 0 || gap + 1 >= sizes.Length) return;
                // 移動ぶんを境界の両側でやり取り (最小 5%)
                float d = Math.Clamp(deltaFrac, -(sizes[gap] - 0.05f), sizes[gap + 1] - 0.05f);
                sizes[gap] += d;
                sizes[gap + 1] -= d;
                sig.Value = t.WithSizes(s.Id, sizes);
            });
}

/// <summary>DockHost ルートの詰め物: 制約いっぱいに子を広げる (無制約軸は 640×400 の既定) —
/// 親が高さ無制約でも Star 行が 0 に潰れないようにする。</summary>
internal sealed class DockFillPanel : Widget
{
    public required Widget Child;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float w = float.IsInfinity(c.MaxW) ? 640 : c.MaxW;
        float h = float.IsInfinity(c.MaxH) ? 400 : c.MaxH;
        Child.Layout(new Constraints(w, w, h, h), ctx, parentUsesSize: true);
        Child.Offset = default;
        Size = c.Constrain(new Size(w, h));
    }

    public override IEnumerable<Widget> DebugChildren() => [Child];

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Child.Realize(ctx, node, WorldPos);
    }
}

/// <summary>DockSplit の描画: 子を割合で並べ、境界に <see cref="Splitter"/> を置く。
/// ドラッグ終了で移動量を割合に換算して onResize(gap, deltaFrac) へ返す。</summary>
internal sealed class DockSplitPanel : Widget
{
    private readonly bool _horizontal;
    private readonly Widget[] _panes;
    private readonly float[] _fractions;
    private readonly Splitter[] _splitters;
    private float _avail = 1;   // 直近レイアウトの分配可能長 (px→割合の換算用)

    public DockSplitPanel(bool horizontal, Widget[] panes, float[] fractions, Action<int, float> onResize)
    {
        _horizontal = horizontal;
        _panes = panes;
        _fractions = fractions;
        _splitters = new Splitter[Math.Max(0, panes.Length - 1)];
        for (int i = 0; i < _splitters.Length; i++)
        {
            int gap = i;
            _splitters[i] = Kit.Splitter(vertical: horizontal,
                onResized: (_, deltaPx) => { if (_avail > 0) onResize(gap, deltaPx / _avail); });
        }
    }

    public override IEnumerable<Widget> DebugChildren() => [.. _panes, .. _splitters];
    public override string? DebugDetail => $"{(_horizontal ? "H" : "V")} {_panes.Length} panes";

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float w = float.IsInfinity(c.MaxW) ? 640 : c.MaxW;
        float h = float.IsInfinity(c.MaxH) ? 400 : c.MaxH;
        float main = _horizontal ? w : h;
        _avail = MathF.Max(1, main - Splitter.Thickness * _splitters.Length);

        float pos = 0;
        for (int i = 0; i < _panes.Length; i++)
        {
            float len = i == _panes.Length - 1 ? MathF.Max(0, main - pos) : _avail * _fractions[i];
            Constraints cc = _horizontal ? new Constraints(len, len, h, h) : new Constraints(w, w, len, len);
            _panes[i].Layout(cc, ctx, parentUsesSize: true);
            _panes[i].Offset = _horizontal ? new Point(pos, 0) : new Point(0, pos);
            pos += len;
            if (i < _splitters.Length)
            {
                Constraints sc = _horizontal
                    ? new Constraints(0, Splitter.Thickness, h, h)
                    : new Constraints(w, w, 0, Splitter.Thickness);
                _splitters[i].Layout(sc, ctx, parentUsesSize: true);
                _splitters[i].Offset = _horizontal ? new Point(pos, 0) : new Point(0, pos);
                pos += Splitter.Thickness;
            }
        }
        Size = c.Constrain(new Size(w, h));
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Point world = WorldPos;
        foreach (Widget p in _panes) p.Realize(ctx, node, world);
        foreach (Splitter s in _splitters) s.Realize(ctx, node, world);
    }
}

/// <summary>グループ内容を包むドロップ対象。タブドラッグ中、端 (25%、上限 96px) で分割ゾーン・
/// 中央でタブ追加をハイライトし、ドロップで OnDropped(id, side|null=中央) を返す。</summary>
internal sealed class DockDropZone : Widget
{
    public required Widget Child;
    public required object Channel;
    public required Action<string, DockSide?> OnDropped;

    private DockSide? _zone;

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float w = float.IsInfinity(c.MaxW) ? 640 : c.MaxW;
        float h = float.IsInfinity(c.MaxH) ? 400 : c.MaxH;
        Child.Layout(new Constraints(w, w, h, h), ctx, parentUsesSize: true);
        Child.Offset = default;
        Size = c.Constrain(new Size(w, h));
    }

    public override IEnumerable<Widget> DebugChildren() => [Child];

    private (float X, float Y, float W, float H) ZoneRect(DockSide? side)
    {
        float w = Size.Width, h = Size.Height;
        float bw = MathF.Min(w * 0.25f, 96), bh = MathF.Min(h * 0.25f, 96);
        return side switch
        {
            DockSide.Left => (0, 0, bw, h),
            DockSide.Right => (w - bw, 0, bw, h),
            DockSide.Top => (0, 0, w, bh),
            DockSide.Bottom => (0, h - bh, w, bh),
            _ => (0, 0, w, h),
        };
    }

    private DockSide? ZoneAt(float x, float y)
    {
        float w = Size.Width, h = Size.Height;
        float bw = MathF.Min(w * 0.25f, 96), bh = MathF.Min(h * 0.25f, 96);
        if (x < bw) return DockSide.Left;
        if (x > w - bw) return DockSide.Right;
        if (y < bh) return DockSide.Top;
        if (y > h - bh) return DockSide.Bottom;
        return null;   // 中央 = タブ追加
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        node.Clip = new RectClip(0, 0, Size.Width, Size.Height);
        Child.Realize(ctx, node, WorldPos);

        // ゾーンハイライト (単位矩形を transform で伸縮)
        UiNode hl = ctx.Canvas.AddChild(node); hl.Z = 50;
        var hs = new Scene2D(); hs.FillRect(Color2D.White, 0, 0, 1, 1);
        hl.Content = hs;
        hl.Opacity = 0f;
        ctx.Effect(() => hl.Color = ctx.Theme.Value.Primary);

        ctx.AddHit(node, new Rect(0, 0, Size.Width, Size.Height),
            acceptsDrop: p => p is DocumentTabs.TabDrag d && Equals(d.Channel, Channel),
            onDropHover: h => { if (!h) { hl.Opacity = 0f; _zone = null; } },
            onDropMove: (_, e) =>
            {
                _zone = ZoneAt(e.X, e.Y);
                (float zx, float zy, float zw, float zh) = ZoneRect(_zone);
                hl.Opacity = 0.22f;
                hl.Transform = Affine2D.Mul(Affine2D.Translate(zx, zy), Affine2D.Scale(zw, zh));
            },
            onDrop: (p, e) =>
            {
                hl.Opacity = 0f;
                if (p is DocumentTabs.TabDrag d) OnDropped(d.Id, ZoneAt(e.X, e.Y));
            });
    }
}
