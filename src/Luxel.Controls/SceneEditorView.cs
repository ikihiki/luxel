using System.Numerics;
using Luxel.SceneEdit;
using Luxel.TwoD;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>シーンエディタのツール。Select 以外はタイル描き込み系で、空間アダプタが
/// <see cref="ISceneTileAdapter"/> を実装しているときだけ効く (2D/3D 両対応原則 3)。</summary>
public enum SceneTool
{
    Select,
    /// <summary>ブラシ — ドラッグでセルを塗る (1 ストローク = 1 undo)。</summary>
    Brush,
    /// <summary>矩形 — ドラッグ範囲を塗り潰す。</summary>
    Rect,
    /// <summary>消しゴム — ブラシのタイル 0 版。</summary>
    Eraser,
    /// <summary>スポイト — クリックしたセルのタイルを <see cref="SceneEditorView.ActiveTile"/> に取る。</summary>
    Picker,
}

/// <summary>
/// シーンエディタのビュー (ADR-0016 / ToDo 27 GE-1) — エンティティの選択/移動/複製/削除を編集する
/// キャンバス。編集意味論は canvas 非依存の <see cref="Luxel.SceneEdit"/> (Transaction スタック 3 本目)、
/// **空間の知識 (座標変換/ヒット/カメラ/描画) はすべて <see cref="ISceneSpaceAdapter"/>** が持ち、
/// このシェルは入力を <see cref="SceneTransaction"/> にするだけ (2D/3D 両対応原則 3 — シェルに
/// 「ワールド = 平面」を書かない)。M11 は 2D アダプタ、3D アダプタは M12 でシェル無改修で足す。
/// </summary>
[UiComponent]
public sealed partial class SceneEditorView : Widget
{
    /// <summary>初期シーン。null なら空の 2D シーン。</summary>
    [UiParam] private readonly Bindable<SceneDoc> _source = new();
    /// <summary>ビュー幅 (px、既定 480)。</summary>
    [UiParam] private readonly Bindable<float> _viewWidth = new();
    /// <summary>ビュー高さ (px、既定 360)。</summary>
    [UiParam] private readonly Bindable<float> _viewHeight = new();

    /// <summary>空間アダプタ (null = シーンの space から自動選択。2D → <see cref="SceneSpace2DAdapter"/>、
    /// 3D は M12 で追加予定につき未対応例外)。</summary>
    public ISceneSpaceAdapter? Adapter { get; set; }

    /// <summary>true = ドロップ位置をグリッドにスナップする。</summary>
    public bool SnapToGrid { get; set; }

    /// <summary>現在のツール (既定 = 選択)。</summary>
    public SceneTool Tool { get; set; } = SceneTool.Select;

    /// <summary>描き込みに使うタイル番号 (スポイトで更新される)。</summary>
    public int ActiveTile { get; set; } = 1;

    /// <summary>描き込み対象のタイルレイヤ id (-1 = 最初のレイヤ)。</summary>
    public int ActiveLayer { get; set; } = -1;

    /// <summary>シーンが変わった (編集/undo/redo)。IEditorDocument アダプタのダーティ検知用。</summary>
    public Action<SceneEditorView>? OnEdit { get; set; }

    private SceneEditState _state = SceneEditState.Create();
    private readonly SceneHistory _history = new();
    private ISceneSpaceAdapter? _space;
    private bool _init;

    private enum Drag { None, Move, Marquee, Pan, Paint, PaintRect }
    private Drag _drag;
    private SceneHandleKind _axis = SceneHandleKind.Free;   // Move 中の拘束軸
    private Vector2 _dragScreenDelta;                       // Move の総移動量 (e.Delta は開始からの累計)
    private Vector2 _panApplied;                            // Pan で適用済みの累計 (増分化用)
    private int[] _dragIds = [];
    private Vector2 _marqStart, _marqCur;                   // marquee の対角 (view-local px)
    private SceneEditState? _preview;                       // ドラッグ中の描画用一時状態 (履歴に積まない)
    private readonly Dictionary<(int X, int Y), int> _stroke = new();   // 描き込みストロークの集計 (座標→タイル)
    private (int X, int Y) _lastCell, _rectA, _rectB;       // ブラシの前回セル / 矩形の対角
    private int _paintLayer;                                // 描き込み中のレイヤ id

    private UiBuildContext _ctx = null!;
    private Signal<Theme> _theme = UiTheme.Current;
    private UiNode _root = null!, _world = null!, _overlay = null!, _marqueeN = null!;
    private FocusTarget? _focus;

    private float W => MathF.Max(160, ViewWidth.Or(480));
    private float H => MathF.Max(120, ViewHeight.Or(360));

    public override string DebugType => "SceneEditorView";
    public override string? DebugDetail => $"{_state.Doc.Entities.Count} エンティティ ({(_state.Doc.Space == SceneSpace.TwoD ? "2d" : "3d")})";

    // ---- 公開 API (テスト/play/外部) ----

    /// <summary>現在の状態 (不変スナップショット)。</summary>
    public SceneEditState Scene => _state;
    /// <summary>エンティティ数。</summary>
    public int EntityCount => _state.Doc.Entities.Count;
    /// <summary>選択エンティティ数。</summary>
    public int SelectionCount => _state.Selection.Entities.Count;
    /// <summary>エンティティが選択されているか。</summary>
    public bool IsSelected(int id) => _state.Selection.Contains(id);
    /// <summary>undo できるか。</summary>
    public bool CanUndo => _history.CanUndo;
    /// <summary>redo できるか。</summary>
    public bool CanRedo => _history.CanRedo;

    /// <summary>2D シーンのエンティティ位置 (transform2d.pos)。play/テストの便宜。</summary>
    public Vector2 EntityPos2D(int id)
        => _state.Doc.Entity(id).Component("transform2d")?.Get("pos")?.AsVec2() ?? default;

    /// <summary>エンティティ中心のクライアント座標 — play が d.Click/d.Drag に渡す用。</summary>
    public Vector2 EntityScreenCenter(int id)
    {
        Vector2 local = _space?.EntityLocalCenter(_state.Doc, id) ?? default;
        return new Vector2(WorldPos.X + local.X, WorldPos.Y + local.Y);
    }

    /// <summary>2D 平面点のクライアント座標 (3D アダプタは地面平面として解釈) — 空白クリック/marquee の play 用。</summary>
    public Vector2 ClientOf(Vector2 world)
    {
        Vector2 local = _space?.LocalOfPlane(world) ?? world;
        return new Vector2(WorldPos.X + local.X, WorldPos.Y + local.Y);
    }

    /// <summary>描き込み対象レイヤのタイル番号 (play/テスト用)。レイヤが無ければ 0。</summary>
    public int TileAt(int x, int y)
        => PaintTargetLayer() is { } id ? _state.Doc.Layer(id).Cell(x, y) : 0;

    /// <summary>描き込み対象レイヤのセル中心クライアント座標 (play 用)。</summary>
    public Vector2 CellClient(int x, int y)
    {
        if (_space is not ISceneTileAdapter tile || PaintTargetLayer() is not { } id) return default;
        Vector2 local = tile.CellLocalCenter(_state.Doc, id, x, y);
        return new Vector2(WorldPos.X + local.X, WorldPos.Y + local.Y);
    }

    // 描き込み対象レイヤ id (ActiveLayer 優先、-1 なら最初のレイヤ。無ければ null)
    private int? PaintTargetLayer()
    {
        if (ActiveLayer >= 0 && _state.Doc.TryLayer(ActiveLayer) is not null) return ActiveLayer;
        return _state.Doc.TileLayers.Count > 0 ? _state.Doc.TileLayers[0].Id : null;
    }

    /// <summary>シーンを丸ごと差し替える (選択・履歴はリセット)。</summary>
    public void Load(SceneDoc doc)
    {
        _state = SceneEditState.Create(doc);
        _history.Clear();
        _preview = null; _drag = Drag.None;
        _space = null;   // space が変わりうるので作り直す
        EnsureSpace();
        Refresh();
    }

    /// <summary>undo 1 手。</summary>
    public void Undo() { _state = _history.Undo(_state); Refresh(); OnEdit?.Invoke(this); }
    /// <summary>redo 1 手。</summary>
    public void Redo() { _state = _history.Redo(_state); Refresh(); OnEdit?.Invoke(this); }

    /// <summary>カメラを画面量で平行移動する。</summary>
    public void Pan(Vector2 screenDelta) { _space?.Pan(screenDelta); Refresh(); }
    /// <summary>カメラを既定に戻す。</summary>
    public void ResetView() { _space?.ResetView(); Refresh(); }

    // ---- レイアウト ----

    private void EnsureInit()
    {
        if (_init) return;
        _init = true;
        if (Source.Get() is { } doc) _state = SceneEditState.Create(doc);
    }

    private void EnsureSpace()
    {
        if (_space is not null) return;
        _space = Adapter ?? _state.Doc.Space switch
        {
            SceneSpace.TwoD => new SceneSpace2DAdapter(),
            _ => throw new NotSupportedException("3D 空間アダプタは M12 で追加予定 (ToDo/27 GE-8)"),
        };
    }

    protected override void PerformLayout(Constraints c, LayoutContext ctx)
    {
        EnsureInit();
        Size = c.Constrain(new Size(W, H));
    }

    public override float MaxIntrinsicWidth(float height, LayoutContext ctx) => W;

    protected override void RealizeCore(UiBuildContext ctx, UiNode parent, Point worldOrigin)
    {
        EnsureInit();
        _ctx = ctx;
        _theme = ctx.Theme;

        _root = CreateRoot(ctx, parent, worldOrigin);
        var bg = new Scene2D();
        bg.FillRoundedRect(Color2D.White, 0, 0, W, H, _theme.Peek().Radius + 1);
        _root.Content = bg;
        ctx.Effect(() => _root.Color = _theme.Value.Background);
        FocusRing.Add(ctx, _root, -3, -3, W + 6, H + 6, 9, Focused);

        // クリップ内に world コンテナ (アダプタがカメラ変換を設定) + 画面空間オーバーレイ
        UiNode clip = ctx.Canvas.AddChild(_root);
        clip.Z = 1;
        clip.Clip = new RectClip(0, 0, W, H);
        _world = ctx.Canvas.AddChild(clip);
        _overlay = ctx.Canvas.AddChild(_root); _overlay.Z = 100;
        _marqueeN = ctx.Canvas.AddChild(_overlay); _marqueeN.ContentColors = true;

        EnsureSpace();
        _space!.Attach(ctx, _world, _overlay, W, H, ctx.Font);

        ctx.Effect(() => { _ = _theme.Value; Refresh(); });   // テーマ変化で描き直し

        _focus ??= new FocusTarget { OnFocus = on => Focused.Value = on, OnKey = OnKey };
        FocusTarget f = ctx.AddFocusable(_focus);

        ctx.AddHit(_root, new Rect(0, 0, W, H), focus: f, cursor: CursorKind.Arrow,
            onDragStart: OnDragStart, onDrag: OnDrag, onDragEnd: OnDragEnd);
        ctx.AddScroll(_root, new Rect(0, 0, W, H), onScrollPos: (d, x, y) => { _space!.ZoomAt(d, new Vector2(x, y)); Refresh(); });

        Refresh();
    }

    // ---- 入力 (すべて view-local px でアダプタへ) ----

    private void OnDragStart(PointerEvent e)
    {
        if (_space is null) return;
        Focused.Value = true;
        var local = new Vector2(e.X, e.Y);

        if (e.Button == PointerButton.Middle)   // 中ボタン = pan
        {
            _drag = Drag.Pan;
            _panApplied = Vector2.Zero;
            return;
        }

        // タイルツール (Select 以外) はアダプタが対応していれば描き込みへ
        if (Tool != SceneTool.Select && _space is ISceneTileAdapter tile && PaintTargetLayer() is { } layerId)
        {
            ToolDown(tile, layerId, local);
            Refresh();
            return;
        }

        // 1) 主選択の移動ハンドル (エンティティより優先 — 重なりの上に描かれている)
        SceneHandleKind handle = _space.HitHandle(_state, local);
        if (handle != SceneHandleKind.None)
        {
            _drag = Drag.Move;
            _axis = handle;
            _dragIds = _state.Selection.Entities.ToArray();
            _dragScreenDelta = Vector2.Zero;
            _preview = _state;
            return;
        }

        // 2) エンティティ本体
        int hit = _space.HitEntity(_state.Doc, local);
        if (hit >= 0)
        {
            if (e.Ctrl)   // Ctrl+Click = 選択トグル (移動はしない)
            {
                var ids = _state.Selection.Entities.ToList();
                bool had = ids.Remove(hit);
                if (!had) ids.Add(hit);
                _state = SceneCommands.SelectEntities(_state, ids, had ? -1 : hit).State;
                _drag = Drag.None;
            }
            else
            {
                if (!_state.Selection.Contains(hit))
                    _state = SceneCommands.SelectEntities(_state, [hit], hit).State;
                _drag = Drag.Move;
                _axis = SceneHandleKind.Free;
                _dragIds = _state.Selection.Entities.ToArray();
                _dragScreenDelta = Vector2.Zero;
                _preview = _state;
            }
        }
        else
        {
            // 空白 — 選択解除して marquee 開始
            _state = SceneCommands.SelectNone(_state).State;
            _drag = Drag.Marquee;
            _marqStart = _marqCur = local;
        }
        Refresh();
    }

    private void OnDrag(PointerEvent e)
    {
        if (_space is null) return;
        switch (_drag)
        {
            case Drag.Move:
                _dragScreenDelta = new Vector2(e.DeltaX, e.DeltaY);   // Delta は開始からの累計
                // プレビューは拘束のみ (スナップは drop で) — 変更列は BuildMove が一元で組む
                _preview = _state.Apply([.. _space.BuildMove(_state.Doc, _dragIds, _dragScreenDelta, _axis, snap: false)]).State;
                break;
            case Drag.Marquee:
                _marqCur = new Vector2(e.X, e.Y);
                break;
            case Drag.Paint when _space is ISceneTileAdapter tile:
                if (tile.CellAt(_state.Doc, _paintLayer, new Vector2(e.X, e.Y), clamp: true) is { } cur)
                {
                    StampLine(_lastCell, cur);
                    _lastCell = cur;
                    _preview = PaintPreview();
                }
                break;
            case Drag.PaintRect when _space is ISceneTileAdapter tile:
                if (tile.CellAt(_state.Doc, _paintLayer, new Vector2(e.X, e.Y), clamp: true) is { } corner)
                {
                    _rectB = corner;
                    RebuildRectStroke();
                    _preview = PaintPreview();
                }
                break;
            case Drag.Pan:
                var total = new Vector2(e.DeltaX, e.DeltaY);
                _space.Pan(total - _panApplied);   // アダプタ API は相対 → 累計との差分を渡す
                _panApplied = total;
                break;
            default: return;
        }
        Refresh();
    }

    private void OnDragEnd(PointerEvent e)
    {
        if (_space is null) return;
        switch (_drag)
        {
            case Drag.Move:
                if (_dragScreenDelta != Vector2.Zero)
                {
                    IReadOnlyList<SceneChange> changes = _space.BuildMove(_state.Doc, _dragIds, _dragScreenDelta, _axis, SnapToGrid);
                    if (changes.Count > 0) Apply(_state.Update(new SceneTransactionSpec { Changes = changes }));
                }
                break;
            case Drag.Marquee:
                if ((_marqCur - _marqStart).LengthSquared() > 4)
                    _state = SceneCommands.SelectEntities(_state, _space.EntitiesIn(_state.Doc, _marqStart, _marqCur)).State;
                break;
            case Drag.Paint or Drag.PaintRect:
                CommitStroke();
                break;
        }
        _drag = Drag.None; _preview = null; _dragScreenDelta = Vector2.Zero; _axis = SceneHandleKind.Free;
        Refresh();
    }

    // ---- タイル描き込み (Brush/Rect/Eraser/Picker) ----

    private void ToolDown(ISceneTileAdapter tile, int layerId, Vector2 local)
    {
        // 開始点はレイヤ内であること (clamp しない — 枠外クリックで塗らない)
        if (tile.CellAt(_state.Doc, layerId, local, clamp: false) is not { } cell) return;
        _paintLayer = layerId;
        switch (Tool)
        {
            case SceneTool.Picker:
                ActiveTile = _state.Doc.Layer(layerId).Cell(cell.X, cell.Y);
                break;
            case SceneTool.Brush or SceneTool.Eraser:
                _drag = Drag.Paint;
                _stroke.Clear();
                _lastCell = cell;
                _stroke[cell] = PaintValue();
                _preview = PaintPreview();
                break;
            case SceneTool.Rect:
                _drag = Drag.PaintRect;
                _stroke.Clear();
                _rectA = _rectB = cell;
                RebuildRectStroke();
                _preview = PaintPreview();
                break;
        }
    }

    private int PaintValue() => Tool == SceneTool.Eraser ? 0 : ActiveTile;

    // ブラシの前回セル → 現在セルを直線補間で埋める (速いドラッグでも途切れない)
    private void StampLine((int X, int Y) a, (int X, int Y) b)
    {
        int n = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        int tileValue = PaintValue();
        for (int i = 0; i <= n; i++)
        {
            float t = n == 0 ? 1 : (float)i / n;
            var c = ((int)MathF.Round(a.X + (b.X - a.X) * t), (int)MathF.Round(a.Y + (b.Y - a.Y) * t));
            _stroke[c] = tileValue;
        }
    }

    private void RebuildRectStroke()
    {
        _stroke.Clear();
        int tileValue = PaintValue();
        for (int y = Math.Min(_rectA.Y, _rectB.Y); y <= Math.Max(_rectA.Y, _rectB.Y); y++)
            for (int x = Math.Min(_rectA.X, _rectB.X); x <= Math.Max(_rectA.X, _rectB.X); x++)
                _stroke[(x, y)] = tileValue;
    }

    // ストローク集計 → プレビュー状態 (履歴に積まない)
    private SceneEditState PaintPreview()
        => _state.Apply(new PaintTiles(_paintLayer, _stroke.Select(kv => new TilePaint(kv.Key.X, kv.Key.Y, kv.Value)).ToList())).State;

    // drop で 1 ストローク = 1 PaintTiles = 1 undo (値の変わらないセルは除外、全部同値なら記録なし)
    private void CommitStroke()
    {
        TileLayer layer = _state.Doc.Layer(_paintLayer);
        var cells = _stroke
            .Where(kv => layer.Cell(kv.Key.X, kv.Key.Y) != kv.Value)
            .Select(kv => new TilePaint(kv.Key.X, kv.Key.Y, kv.Value))
            .ToList();
        _stroke.Clear();
        if (cells.Count > 0) Apply(_state.Apply(new PaintTiles(_paintLayer, cells)));
    }

    private bool OnKey(KeyEvent ev)
    {
        switch (ev.Key)
        {
            case Key.Z when ev.Ctrl: Undo(); return true;
            case Key.Y when ev.Ctrl: Redo(); return true;
            case Key.A when ev.Ctrl: Apply(SceneCommands.SelectAll(_state)); return true;
            case Key.D when ev.Ctrl: Apply(SceneCommands.DuplicateSelection(_state, _space is { } sp ? sp.OffsetDuplicate : null)); return true;
            case Key.Delete or Key.Backspace: Apply(SceneCommands.DeleteSelection(_state)); return true;
            case Key.Escape: Apply(SceneCommands.SelectNone(_state)); return true;
            default: return false;
        }
    }

    private void Apply(SceneTransaction tr)
    {
        if (tr.DocChanged) _history.Record(tr);
        _state = tr.State;
        Refresh();
        if (tr.DocChanged) OnEdit?.Invoke(this);
    }

    // ---- 描画 (world 側はアダプタ、marquee だけシェル) ----

    private void Refresh()
    {
        if (_ctx is null || _space is null) return;
        _space.Refresh(_preview ?? _state, _theme.Peek());
        DrawMarquee();
    }

    private void DrawMarquee()
    {
        if (_drag != Drag.Marquee) { _marqueeN.Content = null; return; }
        float x = MathF.Min(_marqStart.X, _marqCur.X), y = MathF.Min(_marqStart.Y, _marqCur.Y);
        float w = MathF.Abs(_marqStart.X - _marqCur.X), h = MathF.Abs(_marqStart.Y - _marqCur.Y);
        Theme t = _theme.Peek();
        var s = new Scene2D();
        s.FillRect(Styles.WithAlpha(t.Primary, 40), x, y, w, h);
        s.StrokeRoundedRect(t.Primary, 1, x, y, w, h, 0);
        _marqueeN.Content = s;
    }
}
