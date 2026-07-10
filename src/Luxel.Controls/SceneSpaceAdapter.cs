using System.Numerics;
using Luxel.SceneEdit;
using Luxel.TwoD;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Controls;

/// <summary>移動ギズモのハンドル種別 — v1 から軸分解の形 (2D/3D 両対応原則 4)。
/// 3D アダプタ (M12) が AxisZ を足す。</summary>
public enum SceneHandleKind
{
    None,
    /// <summary>本体ドラッグ = 自由移動。</summary>
    Free,
    AxisX,
    AxisY,
}

/// <summary>
/// シーンエディタの**空間アダプタ** (ADR-0016 原則 3) — スクリーン↔ワールド変換・ヒットテスト・
/// カメラ操作・描画をすべてここに閉じ、共有シェル (<see cref="SceneEditorView"/>) には
/// 「ワールド = 平面」等の空間前提を書かない。M11 は 2D 実装 (<see cref="SceneSpace2DAdapter"/>)、
/// M12 で 3D 実装 (OrbitCamera + レイピック) を追加する — シェル無改修が目標。
/// 座標の受け渡しはすべて view-local px (シェルが受ける PointerEvent の座標系)。
/// </summary>
public interface ISceneSpaceAdapter
{
    /// <summary>realize 時に 1 回呼ばれ、world 描画レイヤ (<paramref name="world"/> 配下) と
    /// 画面空間レイヤ (<paramref name="overlay"/> 配下、ハンドル等) を構築する。</summary>
    void Attach(UiBuildContext ctx, UiNode world, UiNode overlay, float viewW, float viewH, VectorFont? font);

    /// <summary>状態/テーマ変化ごとに全描画を作り直す (グリッド/エンティティ/選択/ハンドル)。</summary>
    void Refresh(SceneEditState state, Theme theme);

    /// <summary>view-local 点の直下のエンティティ id (上のものが優先。無ければ -1)。</summary>
    int HitEntity(SceneDoc doc, Vector2 local);

    /// <summary>主選択の移動ハンドルのヒット (選択が空なら None)。エンティティより優先して判定する。</summary>
    SceneHandleKind HitHandle(SceneEditState state, Vector2 local);

    /// <summary>view-local 矩形 (対角 2 点) と交差するエンティティ id 列 (marquee)。</summary>
    IReadOnlyList<int> EntitiesIn(SceneDoc doc, Vector2 aLocal, Vector2 bLocal);

    /// <summary>画面ドラッグ量を world 移動の変更列にする (axis 拘束 + グリッドスナップ)。
    /// ドラッグ中のプレビューにも drop の確定にも同じものを使う。</summary>
    IReadOnlyList<SceneChange> BuildMove(SceneDoc doc, IReadOnlyList<int> ids, Vector2 screenDelta, SceneHandleKind axis, bool snap);

    /// <summary>複製時の位置ずらし (見た目が重ならないように)。</summary>
    SceneEntity OffsetDuplicate(SceneEntity entity);

    /// <summary>カメラを画面量で平行移動する。</summary>
    void Pan(Vector2 screenDelta);

    /// <summary>view-local 点を中心にズームする (ホイール)。</summary>
    void ZoomAt(float wheelDelta, Vector2 local);

    /// <summary>カメラを既定に戻す。</summary>
    void ResetView();

    /// <summary>エンティティ中心の view-local 座標 (play/テスト用)。</summary>
    Vector2 EntityLocalCenter(SceneDoc doc, int id);

    /// <summary>2D 平面点の view-local 座標 (play/テスト用。3D アダプタは地面平面として解釈する)。</summary>
    Vector2 LocalOfPlane(Vector2 world);
}

/// <summary>
/// 2D 空間アダプタ — pan/zoom は world コンテナの <see cref="Affine2D"/> 変換 (ヒットが自動追従、
/// NodeGraphView と同じ手筋)。エンティティは `transform2d.pos` を中心とする定型ボックスで表示する
/// (見た目のスプライト対応は GE-2/GE-3)。ハンドルは画面空間に固定サイズで描く (zoom 非依存)。
/// </summary>
public sealed class SceneSpace2DAdapter : ISceneSpaceAdapter
{
    /// <summary>グリッド/スナップの間隔 (world)。</summary>
    public float GridStep { get; init; } = 32f;

    /// <summary>エンティティ表示ボックスの大きさ (world)。</summary>
    public Vector2 BoxSize { get; init; } = new(96, 40);

    private Vector2 _pan = Vector2.Zero;
    private float _zoom = 1f;
    private float _viewW, _viewH;
    private float _fs = 13;
    private VectorFont? _font;

    private UiNode _worldN = null!, _gridN = null!, _fillN = null!, _strokeN = null!, _selStrokeN = null!, _nameN = null!, _handleN = null!;

    private const float HandleLen = 44f;      // 中心→矢印先端 (画面 px)
    private const float HandleGrab = 8f;      // ハンドルのヒット許容 (画面 px)
    private const float HandleDead = 10f;     // 中心付近はハンドルにしない (本体ドラッグ優先)

    // ---- 変換 (2D: screen = world * zoom + pan) ----

    private Vector2 ToLocal(Vector2 world) => world * _zoom + _pan;

    private Vector2 ToWorld(Vector2 local) => (local - _pan) / _zoom;

    private static Vector2? Pos(SceneDoc doc, int id)
        => doc.TryEntity(id)?.Component("transform2d")?.Get("pos")?.AsVec2();

    private RectF EntityRect(SceneDoc doc, SceneEntity e)
    {
        Vector2? p = e.Component("transform2d")?.Get("pos")?.AsVec2();
        if (p is null) return new RectF(float.NaN, float.NaN, 0, 0);
        return new RectF(p.Value.X - BoxSize.X / 2, p.Value.Y - BoxSize.Y / 2, BoxSize.X, BoxSize.Y);
    }

    // ---- ISceneSpaceAdapter ----

    public void Attach(UiBuildContext ctx, UiNode world, UiNode overlay, float viewW, float viewH, VectorFont? font)
    {
        _viewW = viewW; _viewH = viewH; _font = font;
        _worldN = world;
        _gridN = ctx.Canvas.AddChild(world); _gridN.Z = 0;
        _fillN = ctx.Canvas.AddChild(world); _fillN.Z = 1;
        _strokeN = ctx.Canvas.AddChild(world); _strokeN.Z = 2;
        _selStrokeN = ctx.Canvas.AddChild(world); _selStrokeN.Z = 3;
        _nameN = ctx.Canvas.AddChild(world); _nameN.Z = 4;
        // ハンドルは固定色 2 色 (X=赤/Y=緑 の慣習) を焼き込むので ContentColors
        _handleN = ctx.Canvas.AddChild(overlay); _handleN.Z = 10; _handleN.ContentColors = true;
    }

    public void Refresh(SceneEditState state, Theme theme)
    {
        _fs = theme.FontSm;
        _gridN.Color = Styles.WithAlpha(theme.BorderColor, 40);
        _fillN.Color = theme.Surface;
        _strokeN.Color = theme.BorderColor;
        _selStrokeN.Color = theme.Primary;
        _nameN.Color = theme.Text;
        _worldN.Transform = new Affine2D { A = _zoom, B = 0, C = 0, D = _zoom, E = _pan.X, F = _pan.Y };
        DrawGrid();
        DrawEntities(state);
        DrawHandles(state);
    }

    public int HitEntity(SceneDoc doc, Vector2 local)
    {
        Vector2 w = ToWorld(local);
        // 後に描かれたもの (リスト末尾) が上 → 逆順で最初のヒット
        for (int i = doc.Entities.Count - 1; i >= 0; i--)
        {
            RectF r = EntityRect(doc, doc.Entities[i]);
            if (!float.IsNaN(r.X) && w.X >= r.X && w.X <= r.X + r.W && w.Y >= r.Y && w.Y <= r.Y + r.H)
                return doc.Entities[i].Id;
        }
        return -1;
    }

    public SceneHandleKind HitHandle(SceneEditState state, Vector2 local)
    {
        if (state.Selection.Main < 0 || Pos(state.Doc, state.Selection.Main) is not { } pos) return SceneHandleKind.None;
        Vector2 c = ToLocal(pos);
        // X 軸 (右向き): 中心から HandleDead〜HandleLen の帯
        if (MathF.Abs(local.Y - c.Y) <= HandleGrab && local.X - c.X is >= HandleDead and <= HandleLen + HandleGrab)
            return SceneHandleKind.AxisX;
        // Y 軸 (下向き)
        if (MathF.Abs(local.X - c.X) <= HandleGrab && local.Y - c.Y is >= HandleDead and <= HandleLen + HandleGrab)
            return SceneHandleKind.AxisY;
        return SceneHandleKind.None;
    }

    public IReadOnlyList<int> EntitiesIn(SceneDoc doc, Vector2 aLocal, Vector2 bLocal)
    {
        Vector2 a = ToWorld(aLocal), b = ToWorld(bLocal);
        float x0 = MathF.Min(a.X, b.X), x1 = MathF.Max(a.X, b.X);
        float y0 = MathF.Min(a.Y, b.Y), y1 = MathF.Max(a.Y, b.Y);
        var ids = new List<int>();
        foreach (SceneEntity e in doc.Entities)
        {
            RectF r = EntityRect(doc, e);
            if (float.IsNaN(r.X)) continue;
            if (r.X <= x1 && r.X + r.W >= x0 && r.Y <= y1 && r.Y + r.H >= y0) ids.Add(e.Id);
        }
        return ids;
    }

    public IReadOnlyList<SceneChange> BuildMove(SceneDoc doc, IReadOnlyList<int> ids, Vector2 screenDelta, SceneHandleKind axis, bool snap)
    {
        Vector2 delta = screenDelta / MathF.Max(_zoom, 1e-4f);
        if (axis == SceneHandleKind.AxisX) delta.Y = 0;
        if (axis == SceneHandleKind.AxisY) delta.X = 0;
        var changes = new List<SceneChange>(ids.Count);
        foreach (int id in ids)
        {
            if (Pos(doc, id) is not { } cur) continue;   // transform2d の無いエンティティは動かせない
            Vector2 tgt = cur + delta;
            if (snap) tgt = new Vector2(MathF.Round(tgt.X / GridStep) * GridStep, MathF.Round(tgt.Y / GridStep) * GridStep);
            if (tgt != cur) changes.Add(new SetField(id, "transform2d", "pos", SceneValue.Of(tgt)));
        }
        return changes;
    }

    public SceneEntity OffsetDuplicate(SceneEntity entity)
    {
        if (entity.Component("transform2d") is not { } t || t.Get("pos") is not { } pos) return entity;
        return entity.WithComponent(t.With("pos", SceneValue.Of(pos.AsVec2() + new Vector2(24, 24))));
    }

    public void Pan(Vector2 screenDelta) => _pan += screenDelta;

    public void ZoomAt(float wheelDelta, Vector2 local)
    {
        float nz = Math.Clamp(_zoom * MathF.Pow(1.1f, wheelDelta), 0.25f, 4f);
        Vector2 world = ToWorld(local);   // カーソル下の world 点を固定
        _pan = local - world * nz;
        _zoom = nz;
    }

    public void ResetView() { _pan = Vector2.Zero; _zoom = 1f; }

    public Vector2 EntityLocalCenter(SceneDoc doc, int id)
        => Pos(doc, id) is { } p ? ToLocal(p) : default;

    public Vector2 LocalOfPlane(Vector2 world) => ToLocal(world);

    // ---- 描画 ----

    private void DrawGrid()
    {
        var s = new Scene2D();
        if (_zoom * GridStep >= 6f)
        {
            Vector2 tl = ToWorld(Vector2.Zero), br = ToWorld(new Vector2(_viewW, _viewH));
            float x0 = MathF.Floor(tl.X / GridStep) * GridStep;
            float y0 = MathF.Floor(tl.Y / GridStep) * GridStep;
            int guard = 0;
            for (float x = x0; x <= br.X && guard < 500; x += GridStep, guard++) s.StrokeLine(Color2D.White, 1, x, tl.Y, x, br.Y);
            for (float y = y0; y <= br.Y && guard < 1000; y += GridStep, guard++) s.StrokeLine(Color2D.White, 1, tl.X, y, br.X, y);
        }
        _gridN.Content = s;
    }

    private void DrawEntities(SceneEditState state)
    {
        var fill = new Scene2D();
        var stroke = new Scene2D();
        var sel = new Scene2D();
        var names = new Scene2D();
        foreach (SceneEntity e in state.Doc.Entities)
        {
            RectF r = EntityRect(state.Doc, e);
            if (float.IsNaN(r.X)) continue;
            fill.FillRoundedRect(Color2D.White, r.X, r.Y, r.W, r.H, 5);
            bool isSel = state.Selection.Contains(e.Id);
            (isSel ? sel : stroke).StrokeRoundedRect(Color2D.White, isSel ? 2f : 1.2f, r.X, r.Y, r.W, r.H, 5);
            if (_font is { } font && e.Name.Length > 0)
            {
                (float tw, float th) = font.Measure(e.Name, _fs);
                font.AppendText(names, e.Name, r.X + (r.W - tw) / 2, r.Y + (r.H - th) / 2 + font.Ascent(_fs), _fs, Color2D.White);
            }
        }
        _fillN.Content = fill;
        _strokeN.Content = stroke;
        _selStrokeN.Content = sel;
        _nameN.Content = names;
    }

    // 主選択に軸分解の移動ハンドル (画面空間・固定サイズ)。X=赤/Y=緑の慣習色を焼き込む。
    private void DrawHandles(SceneEditState state)
    {
        if (state.Selection.Main < 0 || Pos(state.Doc, state.Selection.Main) is not { } pos)
        {
            _handleN.Content = null;
            return;
        }
        Vector2 c = ToLocal(pos);
        var red = Color2D.Rgba(229, 83, 75);
        var green = Color2D.Rgba(63, 185, 80);
        var s = new Scene2D();
        // X 軸 (右)
        s.StrokeLine(red, 2, c.X + HandleDead, c.Y, c.X + HandleLen, c.Y);
        s.BeginFill(red).MoveTo(c.X + HandleLen + 8, c.Y).LineTo(c.X + HandleLen, c.Y - 5).LineTo(c.X + HandleLen, c.Y + 5).Close().End();
        // Y 軸 (下)
        s.StrokeLine(green, 2, c.X, c.Y + HandleDead, c.X, c.Y + HandleLen);
        s.BeginFill(green).MoveTo(c.X, c.Y + HandleLen + 8).LineTo(c.X - 5, c.Y + HandleLen).LineTo(c.X + 5, c.Y + HandleLen).Close().End();
        _handleN.Content = s;
    }
}
