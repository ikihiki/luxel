using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>ドキュメントタブの 1 項目。Dirty は signal — ● がライブに追従する (null = 常にクリーン)。</summary>
public sealed record DocTab(string Id, string Title, Signal<bool>? Dirty = null);

/// <summary>
/// 複数ドキュメントのタブ帯 (ADR-0014)。ダーティ ●・× 閉じ・D&amp;D 並べ替え・オーバーフロー
/// (幅超過はタブを等比縮小 + ラベルクリップ) を持つ。既存 <see cref="Tabs"/> はビュー切替専用 —
/// こちらはドキュメント一覧をデータで受け、開閉/並べ替えはイベントで呼び出し側 (シェル) に返す。
/// 同じ <see cref="DragChannel"/> を持つ帯どうしはタブをドラッグで移せる (DockHost のグループ間移動)。
/// </summary>
[UiComponent]
public sealed partial class DocumentTabs : Widget
{
    public const float StripH = 32f;
    private const float PadX = 10f, GlyphW = 18f, MaxTabW = 176f, MinTabW = 56f;

    /// <summary>タブ一覧 (表示順)。</summary>
    [UiParam] private readonly Bindable<IReadOnlyList<DocTab>> _items = new([]);
    /// <summary>アクティブタブの id (null = なし)。</summary>
    [UiParam] private readonly Bindable<string?> _active = new();
    /// <summary>D&amp;D の受け入れ範囲 — 同じ channel オブジェクトを持つ帯間でタブを移せる
    /// (null = この帯内の並べ替えのみ)。</summary>
    [UiParam] private readonly Bindable<object?> _dragChannel = new();

    /// <summary>タブクリック (id)。</summary>
    [UiEvent] public UiEvent<DocumentTabs, string> OnActivate;
    /// <summary>× クリック (id)。閉じるかはシェルが決める (ダーティ確認など)。</summary>
    [UiEvent] public UiEvent<DocumentTabs, string> OnClose;
    /// <summary>タブがこの帯へドロップされた (id, 挿入 index — 除去前の並び基準)。
    /// 同一帯の並べ替えと他帯からの移動の両方がここに来る。</summary>
    [UiEvent] public UiEvent<DocumentTabs, string, int> OnDropTab;

    /// <summary>タブドラッグのペイロード (channel が一致する帯だけが受け入れる)。</summary>
    public sealed record TabDrag(object Channel, string Id, string Title);

    private float[] _tabX = [];
    private float[] _tabW = [];
    private float W;

    private object Channel => DragChannel.Get() ?? this;

    public override string? DebugDetail => $"{Items.Get().Count} tabs";

    /// <summary>タブ本体の画面中心 (play/テスト用、実体化後に有効)。無ければ null。</summary>
    public Point? TabCenterOf(string id)
    {
        IReadOnlyList<DocTab> items = Items.Get();
        for (int i = 0; i < items.Count && i < _tabW.Length; i++)
            if (items[i].Id == id)
                return new Point(WorldPos.X + _tabX[i] + (_tabW[i] - GlyphW) / 2, WorldPos.Y + StripH / 2);
        return null;
    }

    /// <summary>タブの × グリフの画面中心 (play/テスト用)。無ければ null。</summary>
    public Point? CloseCenterOf(string id)
    {
        IReadOnlyList<DocTab> items = Items.Get();
        for (int i = 0; i < items.Count && i < _tabW.Length; i++)
            if (items[i].Id == id)
                return new Point(WorldPos.X + _tabX[i] + _tabW[i] - GlyphW / 2 - 2, WorldPos.Y + StripH / 2);
        return null;
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        IReadOnlyList<DocTab> items = Items.Get();
        float fs = ctx.Theme.FontSm;
        _tabW = new float[items.Count];
        float total = 0;
        for (int i = 0; i < items.Count; i++)
        {
            _tabW[i] = MathF.Min(MaxTabW, ctx.Font.Measure(items[i].Title, fs).width + PadX * 2 + GlyphW);
            total += _tabW[i];
        }
        W = ResolveW(c, ctx, float.IsInfinity(c.MaxW) ? MathF.Max(total, 120) : c.MaxW);
        // オーバーフロー: 幅超過はタブを等比縮小 (MinTabW まで)。それでも超える分は帯のクリップに任せる
        if (total > W && total > 0)
        {
            float k = W / total;
            for (int i = 0; i < _tabW.Length; i++) _tabW[i] = MathF.Max(MinTabW, _tabW[i] * k);
        }
        _tabX = new float[items.Count + 1];
        for (int i = 0; i < items.Count; i++) _tabX[i + 1] = _tabX[i] + _tabW[i];
        Size = c.Constrain(new Size(W, StripH));
    }

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        UiNode node = CreateRoot(ctx, parent, worldOrigin);
        node.Clip = new RectClip(0, 0, W, StripH);
        IReadOnlyList<DocTab> items = Items.Get();
        float fs = ctx.Theme.Peek().FontSm;
        float textY = (StripH - ctx.Font.Measure("Mg", fs).height) / 2 + ctx.Font.Ascent(fs);

        // 帯の下辺ヘアライン
        UiNode baseline = ctx.Canvas.AddChild(node);
        var bs = new Scene2D(); bs.FillRect(Color2D.White, 0, StripH - 1, W, 1);
        baseline.Content = bs;
        ctx.Effect(() => baseline.Color = ctx.Theme.Value.BorderColor);

        // ドロップ (同一 channel のタブ): 挿入位置インジケータ + OnDropTab。
        // **タブ本体より先に登録する** — 同一ノードのヒットは後着優先のため、後に置くと
        // タブのクリック/ドラッグを遮ってしまう (ドロップ探索は OnDrop 持ちだけを見る別経路)。
        UiNode indicator = ctx.Canvas.AddChild(node); indicator.Z = 3;
        var ind = new Scene2D(); ind.FillRect(Color2D.White, -1, 3, 2, StripH - 6);
        indicator.Content = ind;
        indicator.Opacity = 0f;
        ctx.Effect(() => indicator.Color = ctx.Theme.Value.Primary);
        int InsertIndexAt(float lx)
        {
            for (int i = 0; i < _tabW.Length; i++)
                if (lx < _tabX[i] + _tabW[i] / 2) return i;
            return _tabW.Length;
        }
        ctx.AddHit(node, new Rect(0, 0, W, StripH),
            acceptsDrop: p => p is TabDrag d && Equals(d.Channel, Channel),
            onDropHover: h => { if (!h) indicator.Opacity = 0f; },
            onDropMove: (_, e) =>
            {
                indicator.Opacity = 1f;
                indicator.Transform = Affine2D.Translate(_tabX[Math.Min(InsertIndexAt(e.X), _tabX.Length - 1)], 0);
            },
            onDrop: (p, e) =>
            {
                indicator.Opacity = 0f;
                if (p is TabDrag d) OnDropTab.Invoke(this, d.Id, InsertIndexAt(e.X));
            });

        for (int i = 0; i < items.Count; i++)
        {
            DocTab tab = items[i];
            float x = _tabX[i], w = _tabW[i];
            bool active = tab.Id == Active.Get();

            // 地 (アクティブ = SurfaceAlt の角丸上箱 + 下線)
            UiNode bg = ctx.Canvas.AddChild(node);
            var gs = new Scene2D();
            gs.FillRoundedRect(Color2D.White, x + 1, 2, w - 2, StripH - 2, 4);
            bg.Content = gs;
            ctx.Effect(() => bg.Color = ctx.Theme.Value.SurfaceAlt);
            bg.Opacity = active ? 1f : 0f;

            if (active)
            {
                UiNode line = ctx.Canvas.AddChild(node); line.Z = 2;
                var ls = new Scene2D(); ls.FillRect(Color2D.White, x, StripH - 2, w, 2);
                line.Content = ls;
                ctx.Effect(() => line.Color = ctx.Theme.Value.Primary);
            }

            // ラベル (タブ幅でクリップ)
            UiNode lbl = ctx.Canvas.AddChild(node); lbl.Z = 1;
            lbl.Clip = new RectClip(x, 0, MathF.Max(0, w - GlyphW - 4), StripH);
            var ts = new Scene2D();
            ctx.Font.AppendText(ts, tab.Title, x + PadX, textY, fs, Color2D.White);
            lbl.Content = ts;
            ctx.Effect(() => lbl.Color = active ? ctx.Theme.Value.Text : ctx.Theme.Value.TextMuted);

            // 右端グリフ: ダーティなら ● / クリーンなら × (どちらもクリック = 閉じる要求)
            UiNode glyph = ctx.Canvas.AddChild(node); glyph.Z = 1;
            float gx = x + w - GlyphW - 2;
            ctx.Effect(() =>
            {
                var s = new Scene2D();
                if (tab.Dirty?.Value == true)
                    s.FillRoundedRect(Color2D.White, gx + 5, StripH / 2 - 3.5f, 7, 7, 3.5f);   // ●
                else
                    ctx.Font.AppendText(s, "×", gx + 3, textY, fs, Color2D.White);
                glyph.Content = s;
            });
            ctx.Effect(() => glyph.Color = active ? ctx.Theme.Value.Text : ctx.Theme.Value.TextMuted);

            // ヒット: 本体 = アクティブ化 + ドラッグ開始 (4px で昇格)、グリフ = 閉じ
            string id = tab.Id; string title = tab.Title;
            bool started = false;
            ctx.AddHit(node, new Rect(x, 0, w - GlyphW - 2, StripH),
                onDragStart: _ => started = false,
                onDrag: e =>
                {
                    if (started || ctx.Host is null) return;
                    if (MathF.Abs(e.DeltaX) + MathF.Abs(e.DeltaY) <= 4) return;
                    started = true;
                    var ghost = new Scene2D();
                    ghost.FillRoundedRect(ctx.Theme.Peek().SurfaceAlt, 0, 0, MathF.Min(w, 140), StripH - 6, 4);
                    ctx.Font.AppendText(ghost, title, PadX, textY - 3, fs, ctx.Theme.Peek().Text);
                    ctx.Host.BeginDrag(new TabDrag(Channel, id, title), ghost, grabX: 20, grabY: StripH / 2);
                },
                onDragEnd: _ => { if (!started) OnActivate.Invoke(this, id); });
            ctx.AddHit(node, new Rect(gx, 0, GlyphW + 2, StripH), onClick: () => OnClose.Invoke(this, id));
        }
    }
}
