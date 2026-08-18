using Luxel.Graphics.TwoD;
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
    /// <summary>タブ 1 つのグループはタブ帯を出さない (固定ペイン風の chrome 用 —
    /// 内容ドロップゾーンは生きるので他のタブをドックして分割はできる)。</summary>
    [UiParam] private readonly Bindable<bool> _hideSingleTabStrip = false;
    /// <summary>タブ帯に閉じる/dirty グリフを表示する。</summary>
    [UiParam] private readonly Bindable<bool> _showTabClose = new();
    /// <summary>タブ帯の高さ。未設定は DocumentTabs の既定値。</summary>
    [UiParam] private readonly Bindable<float> _tabStripHeight = new();
    /// <summary>選択中タブの背景面を表示する。false では下線だけ。</summary>
    [UiParam] private readonly Bindable<bool> _tabActiveBackground = new();

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
        // ルートを FillPanel で包む — 親が高さ無制約 (VStack 内等) でも 0 に潰れない。
        // フロートは FillPanel が最前面 (高 Z + ヒットレイヤ) に重ねる
        var fill = new DockFillPanel { Child = BuildNode(t.Root, sig) };
        foreach (DockFloat fl in t.Floats)
        {
            int gid = fl.Group.Id;
            // コミットは**表示位置/表示サイズ基準の絶対値** (パネルが Offset/Size から計算して渡す) —
            // ツリー値基準 (fl.X + delta) だと、表示クランプでツリー値と表示がずれているとき
            // ドロップ位置から飛ぶ
            fill.Floats.Add(new DockFloatPanel
            {
                Rect = new Rect(fl.X, fl.Y, fl.W, fl.H),
                Child = BuildGroup(fl.Group, sig),
                OnMoved = (x, y) => sig.Value = sig.Value.MoveFloat(gid, MathF.Max(0, x), MathF.Max(0, y)),
                OnResized = (w, h) => sig.Value = sig.Value.ResizeFloat(gid, w, h),
            });
        }
        return fill;
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

        Widget contentOnly = activeId is not null ? _views[activeId] : Kit.Spacer();
        if (HideSingleTabStrip.Get() && g.Tabs.Count == 1)
        {
            // 固定ペイン風: タブ帯なし。ドロップゾーンは残す (他タブのドックは可能)
            return new DockDropZone
            {
                Child = contentOnly,
                Channel = this,
                OnDropped = (id, side) => sig.Value = side is null
                    ? sig.Value.MoveTab(id, g.Id)
                    : sig.Value.Dock(id, g.Id, side.Value),
            };
        }

        DocumentTabs strip = Kit.DocumentTabs(tabs, active: activeId ?? "",
            dragChannel: this,   // この DockHost 内の帯どうしでタブを移せる
            showClose: ShowTabClose.Or(true), stripHeight: TabStripHeight.Or(DocumentTabs.StripH),
            activeBackground: TabActiveBackground.Or(true),
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
        return Kit.Grid(rows: [GridLength.Px(TabStripHeight.Or(DocumentTabs.StripH)), GridLength.Star()])[strip, zone];
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
/// 親が高さ無制約でも Star 行が 0 に潰れないようにする。フロートパネルを最前面
/// (Z=300+ / ヒットレイヤ 1+) に重ねる — レイヤのおかげで背面ドックの深いヒットに負けない。</summary>
internal sealed class DockFillPanel : Widget
{
    public required Widget Child;
    public readonly List<DockFloatPanel> Floats = new();

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float w = float.IsInfinity(c.MaxW) ? 640 : c.MaxW;
        float h = float.IsInfinity(c.MaxH) ? 400 : c.MaxH;
        Child.Layout(new Constraints(w, w, h, h), ctx, parentUsesSize: true);
        Child.Offset = default;
        foreach (DockFloatPanel f in Floats)
        {
            float fw = MathF.Min(f.Rect.Width, w), fh = MathF.Min(f.Rect.Height, h);
            f.Layout(new Constraints(fw, fw, fh, fh), ctx, parentUsesSize: true);
            f.Offset = new Point(Math.Clamp(f.Rect.X, 0, MathF.Max(0, w - fw)),
                                 Math.Clamp(f.Rect.Y, 0, MathF.Max(0, h - fh)));
        }
        Size = c.Constrain(new Size(w, h));
    }

    public override IEnumerable<Widget> DebugChildren() => [Child, .. Floats];

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        Child.Realize(ctx, node, WorldPos);
        for (int i = 0; i < Floats.Count; i++)
        {
            UiNode holder = ctx.Canvas.AddChild(node);
            holder.Z = 300 + i;                          // 描画も前面 (末尾 = 最前)
            // レイヤは 2 刻み — +0 = パネル内容、+1 = パネル chrome (つかみバー/リサイズ隅) が
            // 深い内容ヒットに勝つための予約 (次のフロートの基底とは重ならない)
            Floats[i].HitLayer = ctx.HitLayer + 1 + i * 2;
            Floats[i].Realize(ctx, holder, WorldPos);
        }
    }
}

/// <summary>窓内フローティングパネル: つかみバー (ドラッグで移動、Splitter と同じゴースト追従 +
/// 終了時 commit) + グループ内容 (タブ帯 + ドロップゾーン) + 右下リサイズハンドル (ドラッグ中は
/// アウトラインのゴースト)。chrome (バー/隅) のヒットは内容よりレイヤを 1 つ上げる —
/// 深さ優先の判定では深い内容ヒット (ドロップゾーン等) に負けるため。</summary>
internal sealed class DockFloatPanel : Widget
{
    public const float GrabH = 14f;
    private const float Corner = 14f;

    public required Rect Rect;
    public required Widget Child;
    public required Action<float, float> OnMoved;     // (newX, newY) 表示位置基準の絶対値、ドラッグ終了時
    public required Action<float, float> OnResized;   // (newW, newH) 同、ドラッグ終了時

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        float w = c.MaxW, h = c.MaxH;
        Child.Layout(new Constraints(w, w, h - GrabH, h - GrabH), ctx, parentUsesSize: true);
        Child.Offset = new Point(0, GrabH);
        Size = c.Constrain(new Size(w, h));
    }

    public override IEnumerable<Widget> DebugChildren() => [Child];

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        float w = Size.Width, h = Size.Height;

        // 地 + 枠 (浮いて見えるように背景を不透明で塗る)
        UiNode bg = ctx.Canvas.AddChild(node);
        var bs = new Scene2D();
        bs.FillRoundedRect(Color2D.White, 0, 0, w, h, 6);
        bg.Content = bs;
        ctx.Effect(() => bg.Color = ctx.Theme.Value.Surface);
        UiNode frame = ctx.Canvas.AddChild(node); frame.Z = 60;
        var fs = new Scene2D();
        fs.StrokeRoundedRect(Color2D.White, 1, 0.5f, 0.5f, w - 1, h - 1, 6);
        frame.Content = fs;
        ctx.Effect(() => frame.Color = ctx.Theme.Value.BorderColor);

        // つかみバー (中央にグリップ点々)。移動コミットは表示位置 (Offset) + 移動量の絶対値
        UiNode grab = ctx.Canvas.AddChild(node); grab.Z = 61;
        var gs = new Scene2D();
        for (int i = -2; i <= 2; i++) gs.FillRoundedRect(Color2D.White, w / 2 + i * 8 - 1.5f, GrabH / 2 - 1.5f, 3, 3, 1.5f);
        grab.Content = gs;
        ctx.Effect(() => grab.Color = ctx.Theme.Value.TextMuted);
        HitTarget grabHit = ctx.AddHit(node, new Rect(0, 0, w - Corner, GrabH), cursor: CursorKind.Hand,
            onDrag: e => node.Transform = Affine2D.Translate(Offset.X + e.DeltaX, Offset.Y + e.DeltaY),
            onDragEnd: e =>
            {
                node.Transform = Affine2D.Translate(Offset.X, Offset.Y);
                if (e.DeltaX != 0 || e.DeltaY != 0) OnMoved(Offset.X + e.DeltaX, Offset.Y + e.DeltaY);
            });
        grabHit.Layer += 1;   // 深い内容ヒット (タブ帯/ドロップゾーン) に負けない

        // 右下リサイズハンドル — ドラッグ中は新しいサイズのアウトラインを見せ、離した位置で commit
        UiNode corner = ctx.Canvas.AddChild(node); corner.Z = 61;
        var cs = new Scene2D();
        cs.FillRect(Color2D.White, w - 10, h - 3, 8, 2);
        cs.FillRect(Color2D.White, w - 3, h - 10, 2, 8);
        corner.Content = cs;
        ctx.Effect(() => corner.Color = ctx.Theme.Value.TextMuted);
        UiNode resizeGhost = ctx.Canvas.AddChild(node); resizeGhost.Z = 62;
        resizeGhost.Opacity = 0f;
        // 予約: リサイズ中の Content 差し替え (アウトライン 4 辺) を in-place で受ける
        resizeGhost.Content = new Scene2D().StrokeRoundedRect(Color2D.White, 1, 0.5f, 0.5f, w - 1, h - 1, 6);
        ctx.Effect(() => resizeGhost.Color = ctx.Theme.Value.Primary);
        (float W, float H) NewSize(PointerEvent e) =>
            (MathF.Max(120, w + e.DeltaX), MathF.Max(80, h + e.DeltaY));
        HitTarget cornerHit = ctx.AddHit(node, new Rect(w - Corner, h - Corner, Corner, Corner), cursor: CursorKind.ResizeH,
            onDrag: e =>
            {
                (float nw, float nh) = NewSize(e);
                resizeGhost.Opacity = 1f;
                resizeGhost.Content = new Scene2D().StrokeRoundedRect(Color2D.White, 1, 0.5f, 0.5f, nw - 1, nh - 1, 6);
            },
            onDragEnd: e =>
            {
                resizeGhost.Opacity = 0f;
                if (e.DeltaX == 0 && e.DeltaY == 0) return;
                (float nw, float nh) = NewSize(e);
                OnResized(nw, nh);
            });
        cornerHit.Layer += 1;

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
