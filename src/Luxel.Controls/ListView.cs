using Luxel.Animation;
using Luxel.Graphics.TwoD;
using Luxel.UI;

using Luxel.Typography.TwoD;
namespace Luxel.Controls;

/// <summary>
/// 文字列行のリスト (固定行高、ホイール + サムでスクロール、クリック選択)。**仮想化 (AP-M3)**:
/// 実体化するノードは可視行数 + 1 の固定プールだけで、スクロール/<see cref="Items"/> 差し替えは
/// プールへの**再バインド** (Content 再代入 = canvas の in-place 部分更新) — ノード数は行数にも
/// スクロールにも依存せず、構造 dirty は発生しない (10 万行でもノードは数十個)。
/// スクロール位置はフィールドなので再実体化をまたいで生き残る。
/// 行ごとに Effect は作らない — 色は Realize 時の単一 Effect が全行へ流す。
/// </summary>
[UiComponent]
public sealed partial class ListView : Widget
{
    /// <summary>リストの表示高 (px)。</summary>
    [UiParam] private readonly Bindable<float> _height = new();
    /// <summary>行高 (px、最小 8)。</summary>
    [UiParam] private readonly Bindable<float> _rowHeight = 18f;

    private IReadOnlyList<string> _rows = [];
    private readonly ScrollModel _scroll = new();     // 位置はフィールド — 再実体化をまたいで生き残る
    private readonly Signal<int> _selected = new(-1);
    private readonly Signal<int> _version = new(0);   // items 差し替え毎に進める (再バインドの再評価用)

    /// <summary>行クリックで呼ばれる (index)。(EV: 第一引数は発火元の ListView 自身)</summary>
    [UiEvent] public UiEvent<ListView, int> OnSelect;
    /// <summary>並べ替えドロップ (from 行, 挿入先 index — from 除去**前**の位置)。</summary>
    [UiEvent] public UiEvent<ListView, int, int> OnReorder;

    /// <summary>行データの signal。値の差し替えがそのまま反映される
    /// (参照が変わった時だけ再バインド — 選択解除 + スクロールはクランプ)。</summary>
    [UiParam] private readonly Bindable<Signal<IReadOnlyList<string>>> _items = new();

    /// <summary>行の D&D 並べ替えを許可する (QP-M4)。ドロップで <see cref="OnReorder"/> が呼ばれる —
    /// 呼び出し側が並べ替えた列を <see cref="Items"/> の signal へ入れ直す (このコントロールはデータを所有しない)。</summary>
    public bool AllowReorder { get; set; }

    /// <summary>並べ替えドラッグのペイロード (ドロップ先の自己判定用)。</summary>
    public sealed record ReorderDrag(ListView Owner, int Index, string Text);

    /// <summary>行テキスト色。未設定 → テーマ TextMuted。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _textColor = new();
    /// <summary>選択行の地色。未設定 → テーマ SurfaceAlt。</summary>
    [UiParam(Stateable = true)] private readonly Bindable<uint> _selectedColor = new();
    /// <summary>行テキストサイズ。未設定 → テーマ FontSm。</summary>
    [UiParam] private readonly Bindable<float> _fontSize = new();

    private const float DefaultWidth = 240f;
    private float W = DefaultWidth;   // PerformLayout で解決 (% / em / vw 対応)
    private float Fs => FontSize.Or(_ctx!.Theme.Value.FontSm);

    // 実体化状態 (SetRoot 再実体化で作り直される)
    private UiBuildContext? _ctx;
    private UiNode? _clip, _content, _highlight;
    private readonly List<UiNode> _rowNodes = new();   // 固定プール (可視行数 + 1)
    private int[] _boundIdx = [];                       // プール各ノードが表示中の行 index (-2 = 未バインド)
    private string?[] _boundText = [];                  // 同、表示中のテキスト (参照比較)
    private uint _rowColor = Color2D.White;
    private float _fs, _textY;                          // Realize で解決 (items 差し替え時の再予約に使う)

    // 旧 ctor のクランプは読み出し側で適用 (H = 表示高、RowH = 行高)
    private float H => MathF.Max(1, Height.Get());
    private float RowH => MathF.Max(8, RowHeight.Get());

    public int SelectedIndex => _selected.Value;
    public override string? DebugDetail => $"{(Items.Get()?.Value ?? _rows).Count} 行";

    private float ContentH => _rows.Count * RowH;

    /// <summary>最長行のエンコードサイズをプール全ノードの最低予約にする — 行毎の glyph 数の揺れを
    /// in-place で受け、スクロール中のフル再構築を防ぐ (予約は次のフル再構築で反映される)。</summary>
    private void ReservePool()
    {
        if (_ctx is null || _rowNodes.Count == 0 || _rows.Count == 0) return;
        string longest = "";
        foreach (string s in _rows) if (s.Length > longest.Length) longest = s;
        var sc = new Scene2D();
        _ctx.Font.AppendText(sc, longest, 0, _textY, _fs, Color2D.White);
        (int segs, int paths) = sc.CountEncoded();
        // 文字数が同じでも字形により線分/パス数は揺れるため 25% 上乗せ
        foreach (UiNode r in _rowNodes) r.ReserveContent(segs * 5 / 4, paths * 5 / 4);
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        W = ResolveW(c, ctx, DefaultWidth);
        Size = c.Constrain(new Size(W, H));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => ResolveWIntrinsic(ctx, DefaultWidth);

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        _ctx = ctx;
        _rowNodes.Clear();   // 再実体化 (旧ノードは SetRoot が破棄済み)

        UiNode node = CreateRoot(ctx, parent, worldOrigin);

        _clip = node;
        node.Clip = new RectClip(0, 0, W, H);

        // 選択ハイライト (行の背面)
        _highlight = ctx.Canvas.AddChild(node);
        var hs = new Scene2D();
        hs.FillRoundedRect(Color2D.White, 0, 0, W - 10, RowH, 2);
        _highlight.Content = hs;
        ctx.Effect(() => _highlight!.Color = SelectedColor.Or(ctx.Theme.Value.SurfaceAlt));

        // 行コンテナ (スクロールは transform) + 仮想化プール (可視行数 + 1 の固定ノード)
        _content = ctx.Canvas.AddChild(node);
        _content.Z = 1;
        int pool = (int)MathF.Ceiling(H / RowH) + 1;
        _boundIdx = new int[pool];
        _boundText = new string?[pool];
        for (int j = 0; j < pool; j++)
        {
            _boundIdx[j] = -2;
            _rowNodes.Add(ctx.Canvas.AddChild(_content));
        }
        // スムーズスクロール (AS-M3): _scroll.Offset は目標、表示は動的状態 "wheel"/"drag" で追従
        // (drag は table で 0ms = 直接操作は即時)
        UiStates scroll = ctx.States(new TransitionTable()
            .On("offset", new TransitionSpec(0.12f))
            .To("drag", new TransitionSpec(0f)));
        scroll.Start("idle", ("offset", _scroll.ClampedPeek));
        string src = "wheel";
        ctx.Effect(() => scroll.Goto(src, ("offset", _scroll.Clamped)));
        ctx.Effect(() => _content!.Transform = Affine2D.Translate(0, -scroll.Float("offset")));
        // 選択強調 (AS-M3): 行間の移動は動的状態 "selected" のスライド、消灯は "none"。
        // スクロール追従は表示オフセット同期 (アニメ項と分離)
        UiStates sel = ctx.States(new TransitionTable().Default(new TransitionSpec(0.12f)));
        float lastY = MathF.Max(0, _selected.Peek()) * RowH;
        sel.Start(_selected.Peek() >= 0 ? "selected" : "none", ("y", lastY), ("on", _selected.Peek() >= 0 ? 1f : 0f));
        ctx.Effect(() =>
        {
            int s = _selected.Value;
            if (s >= 0) { lastY = s * RowH; sel.Goto("selected", ("y", lastY), ("on", 1f)); }
            else sel.Goto("none", ("y", lastY), ("on", 0f));   // 位置は保ったまま消灯
        });
        ctx.Effect(() =>   // 選択強調の位置 (content と同じ座標系に置くため子にする方が素直だが、Z 順のため transform 合成)
        {
            _highlight!.Opacity = sel.Float("on");
            _highlight.Transform = Affine2D.Translate(4, sel.Float("y") - scroll.Float("offset") + 1);
        });

        // 行テキスト色は単一 Effect で全行へ (行ごとに Effect を作らない)
        ctx.Effect(() =>
        {
            _rowColor = TextColor.Or(ctx.Theme.Value.TextMuted);
            foreach (UiNode r in _rowNodes) r.Color = _rowColor;
        });

        // 再バインド: スクロール (_offset) / データ差し替え (_version) で可視範囲をプールへ流し込む。
        // 表示中の (index, テキスト) が同じノードは触らない — 1 行スクロールなら差し替えは 1 ノード。
        float fs = _fs = Fs;
        float textY = _textY = (RowH - ctx.Font.Measure("Mg", fs).height) / 2 + ctx.Font.Ascent(fs);
        // items 同期: Items (signal 可) の参照が変わったら選択解除 + 再予約 + 再バインド
        ctx.Effect(() =>
        {
            IReadOnlyList<string> items = Items.Get()?.Value ?? [];
            if (ReferenceEquals(items, _rows)) return;
            _rows = items;
            _selected.Value = -1;
            ReservePool();
            _scroll.SetLengths(ContentH, H);   // 位置はクランプで追従、サムは signal 経由で再評価
            _version.Value++;   // 再バインド
        });
        ReservePool();
        ctx.Effect(() =>
        {
            _ = _version.Value;
            int first = (int)(scroll.Float("offset") / RowH);   // 表示オフセット基準 — 滑走中も可視範囲を欠かさない
            for (int j = 0; j < _rowNodes.Count; j++)
            {
                int idx = first + j;
                string? text = idx < _rows.Count ? _rows[idx] : null;
                UiNode r = _rowNodes[j];
                r.Transform = Affine2D.Translate(8, idx * RowH);
                if (_boundIdx[j] == idx && ReferenceEquals(_boundText[j], text)) continue;
                _boundIdx[j] = idx;
                _boundText[j] = text;
                var s = new Scene2D();
                if (text is not null) ctx.Font.AppendText(s, text, 0, textY, fs, Color2D.White);
                r.Content = s;
                r.Color = _rowColor;
            }
        });

        // 入力: 行クリック=選択、ホイール、サムのドラッグ (トラック帯を前面で拾う)
        const float grabW = ScrollBars.GrabW;
        void SelectAt(float ly)
        {
            int i = (int)((ly + scroll.Float("offset")) / RowH);   // 見えている位置でヒット (滑走中も一致)
            if (i >= 0 && i < _rows.Count) { _selected.Value = i; OnSelect.Invoke(this, i); }
        }
        if (!AllowReorder)
        {
            ctx.AddHit(node, new Rect(0, 0, W - grabW, H), onClickPos: e => SelectAt(e.Y));
        }
        else
        {
            // D&D 並べ替え (QP-M4): 4px 動いたらドラッグへ昇格、動かなければクリック (選択)。
            // 挿入位置インジケータは行境界に置く 2px ライン (ドラッグ中のみ表示)。
            UiNode indicator = ctx.Canvas.AddChild(node);
            indicator.Z = 3;
            var ind = new Scene2D();
            ind.FillRect(Color2D.White, 4, -1, W - grabW - 8, 2);
            indicator.Content = ind;
            indicator.Opacity = 0f;
            ctx.Effect(() => indicator.Color = ctx.Theme.Value.Primary);
            int InsertIndexAt(float ly) => Math.Clamp((int)MathF.Round((ly + scroll.Float("offset")) / RowH), 0, _rows.Count);

            int pressIdx = -1; bool started = false;
            ctx.AddHit(node, new Rect(0, 0, W - grabW, H),
                onDragStart: e =>
                {
                    started = false;
                    int i = (int)((e.Y + scroll.Float("offset")) / RowH);
                    pressIdx = i >= 0 && i < _rows.Count ? i : -1;
                },
                onDrag: e =>
                {
                    if (started || pressIdx < 0 || _ctx?.Host is null) return;
                    if (MathF.Abs(e.DeltaX) + MathF.Abs(e.DeltaY) <= 4) return;   // 4px 動いたら昇格 (画面絶対基準)
                    started = true;
                    string text = _rows[pressIdx];
                    var ghost = new Scene2D();
                    ghost.FillRoundedRect(_ctx.Theme.Peek().SurfaceAlt, 0, 0, W - grabW, RowH + 4, 4);
                    _ctx.Font.AppendText(ghost, text, 8, _textY + 2, _fs, _ctx.Theme.Peek().Text);
                    _ctx.Host.BeginDrag(new ReorderDrag(this, pressIdx, text), ghost, grabX: e.StartX, grabY: RowH / 2);
                },
                onDragEnd: e => { if (!started && pressIdx >= 0) SelectAt(e.Y); },
                acceptsDrop: p => p is ReorderDrag d && d.Owner == this,
                onDropHover: h => { if (!h) indicator.Opacity = 0f; },
                onDropMove: (_, e) =>
                {
                    indicator.Opacity = 1f;
                    indicator.Transform = Affine2D.Translate(0, InsertIndexAt(e.Y) * RowH - scroll.Float("offset"));
                },
                onDrop: (p, e) =>
                {
                    indicator.Opacity = 0f;
                    if (p is ReorderDrag d && d.Owner == this) OnReorder.Invoke(this, d.Index, InsertIndexAt(e.Y));
                });
        }
        // サム (共通実装 — 表示はスムーズスクロールの動的値に追従、ドラッグは即時チャネル)
        ScrollBars.AttachVertical(ctx, node, _scroll, W, H,
            displayOffset: () => scroll.Float("offset"),
            onDirectChange: () => src = "drag",
            minThumb: 24);
        ctx.AddScroll(node, new Rect(0, 0, W, H),
            d => { src = "wheel"; _scroll.ScrollBy(-d); });
    }
}
