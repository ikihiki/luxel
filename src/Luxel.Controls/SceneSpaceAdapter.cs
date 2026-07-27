using System.Numerics;
using Luxel;
using Luxel.SceneEdit;
using Luxel.Graphics.TwoD;
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
    AxisZ,
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
/// タイル描き込みに対応する空間アダプタの追加口 (ADR-0016)。タイルは 2D 空間の機能なので
/// 基底の <see cref="ISceneSpaceAdapter"/> には載せない — シェルは <c>is</c> 判定で
/// ツールを有効化する (3D アダプタが実装しなければタイルツールが出ないだけ)。
/// </summary>
public interface ISceneTileAdapter
{
    /// <summary>view-local 点が指すレイヤセル座標。<paramref name="clamp"/>=true はレイヤ範囲へ
    /// クランプ (ストローク継続用)、false は範囲外 null。</summary>
    (int X, int Y)? CellAt(SceneDoc doc, int layerId, Vector2 local, bool clamp);

    /// <summary>セル中心の view-local 座標 (play/テスト用)。</summary>
    Vector2 CellLocalCenter(SceneDoc doc, int layerId, int x, int y);
}

/// <summary>
/// 2D 空間アダプタ — pan/zoom は world コンテナの <see cref="Affine2D"/> 変換 (ヒットが自動追従、
/// NodeGraphView と同じ手筋)。エンティティは `transform2d.pos` を中心とする定型ボックス、タイルは
/// **エディタ用プレースホルダ色** (タイル番号 → 決定的パレット) の矩形で表示する — 実アトラス描画
/// (TileSet/SpriteAtlas) はアセット配線後の GE-2/GE-3 で差し替える。ハンドルは画面空間に
/// 固定サイズで描く (zoom 非依存)。タイルレイヤの原点は world (0,0)、セル (x,y) は
/// [x·cell, (x+1)·cell] × [y·cell, (y+1)·cell]。
/// </summary>
public sealed class SceneSpace2DAdapter : ISceneSpaceAdapter, ISceneTileAdapter
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

    private UiNode _worldN = null!, _gridN = null!, _tileN = null!, _layerN = null!, _fillN = null!, _strokeN = null!, _selStrokeN = null!, _nameN = null!, _handleN = null!;

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
        _tileN = ctx.Canvas.AddChild(world); _tileN.Z = 1; _tileN.ContentColors = true;   // タイルは固定パレット色
        _layerN = ctx.Canvas.AddChild(world); _layerN.Z = 2;                              // レイヤ境界 (テーマ色)
        _fillN = ctx.Canvas.AddChild(world); _fillN.Z = 3;
        _strokeN = ctx.Canvas.AddChild(world); _strokeN.Z = 4;
        _selStrokeN = ctx.Canvas.AddChild(world); _selStrokeN.Z = 5;
        _nameN = ctx.Canvas.AddChild(world); _nameN.Z = 6;
        // ハンドルは固定色 2 色 (X=赤/Y=緑 の慣習) を焼き込むので ContentColors
        _handleN = ctx.Canvas.AddChild(overlay); _handleN.Z = 10; _handleN.ContentColors = true;
    }

    public void Refresh(SceneEditState state, Theme theme)
    {
        _fs = theme.FontSm;
        _gridN.Color = Styles.WithAlpha(theme.BorderColor, 40);
        _layerN.Color = Styles.WithAlpha(theme.TextMuted, 120);
        _fillN.Color = theme.Surface;
        _strokeN.Color = theme.BorderColor;
        _selStrokeN.Color = theme.Primary;
        _nameN.Color = theme.Text;
        _worldN.Transform = new Affine2D { A = _zoom, B = 0, C = 0, D = _zoom, E = _pan.X, F = _pan.Y };
        DrawGrid();
        DrawTiles(state);
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

    // ---- ISceneTileAdapter ----

    public (int X, int Y)? CellAt(SceneDoc doc, int layerId, Vector2 local, bool clamp)
    {
        if (doc.TryLayer(layerId) is not { } layer) return null;
        Vector2 w = ToWorld(local);
        int x = (int)MathF.Floor(w.X / layer.CellSize);
        int y = (int)MathF.Floor(w.Y / layer.CellSize);
        if (clamp)
            return (Math.Clamp(x, 0, layer.Width - 1), Math.Clamp(y, 0, layer.Height - 1));
        return x >= 0 && x < layer.Width && y >= 0 && y < layer.Height ? (x, y) : null;
    }

    public Vector2 CellLocalCenter(SceneDoc doc, int layerId, int x, int y)
    {
        TileLayer layer = doc.Layer(layerId);
        return ToLocal(new Vector2((x + 0.5f) * layer.CellSize, (y + 0.5f) * layer.CellSize));
    }

    /// <summary>エディタ用のタイル色 — ランタイム (Luxel.Player) と共有の <see cref="TilePalette"/>
    /// (実アトラス描画が配線されたら差し替え)。</summary>
    internal static uint TileColor(int tile) => TilePalette.ColorOf(tile);

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

    // タイルレイヤ: 非ゼロセルをパレット色の矩形で + レイヤ境界の枠
    private void DrawTiles(SceneEditState state)
    {
        var tiles = new Scene2D();
        var bounds = new Scene2D();
        foreach (TileLayer layer in state.Doc.TileLayers)
        {
            float cs = layer.CellSize;
            for (int y = 0; y < layer.Height; y++)
                for (int x = 0; x < layer.Width; x++)
                {
                    int t = layer.Cell(x, y);
                    if (t != 0) tiles.FillRect(TileColor(t), x * cs, y * cs, cs, cs);
                }
            bounds.StrokeRoundedRect(Color2D.White, 1, 0, 0, layer.Width * cs, layer.Height * cs, 0);
        }
        _tileN.Content = tiles;
        _layerN.Content = bounds;
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

/// <summary>
/// 3D 空間アダプタ — <see cref="OrbitCamera"/> で地面グリッド / transform3d の AABB / 3 軸移動ハンドルを
/// 2D キャンバスへ投影して描く。共有シェルは view-local px だけを渡し、このアダプタが
/// レイピック・軸拘束・orbit/dolly 操作を閉じ込める。
/// </summary>
public sealed class SceneSpace3DAdapter : ISceneSpaceAdapter
{
    public float GridStep { get; init; } = 1f;
    public int GridHalfLines { get; init; } = 8;
    public Vector3 BoxSize { get; init; } = Vector3.One;

    private OrbitCamera _cam = new(new Vector3(0, 0.4f, 0), yaw: 0.72f, pitch: 0.42f, distance: 8f,
        fovYRadians: 1.05f, aspect: 620f / 360f, near: 0.05f, far: 100f);
    private float _viewW = 620f, _viewH = 360f, _fs = 13f;
    private VectorFont? _font;

    private UiNode _gridN = null!, _boxN = null!, _selN = null!, _nameN = null!, _handleN = null!;

    private const float HandleLenWorld = 1.35f;
    private const float HandleGrab = 8f;
    private const float EntityGrabPad = 5f;

    public OrbitCamera Camera => _cam;

    private static Vector3? Pos(SceneDoc doc, int id)
        => doc.TryEntity(id)?.Component("transform3d")?.Get("pos")?.AsVec3();

    private static Vector3 Scale(SceneEntity e)
        => e.Component("transform3d")?.Get("scale")?.AsVec3() ?? Vector3.One;

    private (Vector3 Min, Vector3 Max)? EntityBounds(SceneEntity e)
    {
        Vector3? p = e.Component("transform3d")?.Get("pos")?.AsVec3();
        if (p is null) return null;
        Vector3 s = Vector3.Max(Vector3.Abs(Scale(e)), new Vector3(0.15f));
        Vector3 half = BoxSize * s * 0.5f;
        return (p.Value - half, p.Value + half);
    }

    public void Attach(UiBuildContext ctx, UiNode world, UiNode overlay, float viewW, float viewH, VectorFont? font)
    {
        _viewW = viewW; _viewH = viewH; _font = font;
        _cam.Aspect = viewW / MathF.Max(1f, viewH);
        _gridN = ctx.Canvas.AddChild(world); _gridN.Z = 0; _gridN.ContentColors = true;
        _boxN = ctx.Canvas.AddChild(world); _boxN.Z = 1; _boxN.ContentColors = true;
        _selN = ctx.Canvas.AddChild(world); _selN.Z = 2; _selN.ContentColors = true;
        _nameN = ctx.Canvas.AddChild(world); _nameN.Z = 3; _nameN.ContentColors = true;
        _handleN = ctx.Canvas.AddChild(overlay); _handleN.Z = 10; _handleN.ContentColors = true;
    }

    public void Refresh(SceneEditState state, Theme theme)
    {
        _fs = theme.FontSm;
        DrawGrid();
        DrawEntities(state);
        DrawHandles(state);
    }

    public int HitEntity(SceneDoc doc, Vector2 local)
    {
        for (int i = doc.Entities.Count - 1; i >= 0; i--)
        {
            SceneEntity e = doc.Entities[i];
            if (e.Component("transform3d")?.Get("pos")?.AsVec3() is { } p && Project(p) is { } c &&
                (local - c).LengthSquared() <= 26f * 26f)
                return e.Id;
            if (ScreenBounds(e) is not { } r) continue;
            if (local.X >= r.X - EntityGrabPad && local.X <= r.X + r.W + EntityGrabPad &&
                local.Y >= r.Y - EntityGrabPad && local.Y <= r.Y + r.H + EntityGrabPad)
                return e.Id;
        }
        return -1;
    }

    public SceneHandleKind HitHandle(SceneEditState state, Vector2 local)
    {
        if (state.Selection.Main < 0 || Pos(state.Doc, state.Selection.Main) is not { } pos) return SceneHandleKind.None;
        SceneHandleKind best = SceneHandleKind.None;
        float bestD = HandleGrab;
        CheckAxis(SceneHandleKind.AxisX, pos, Vector3.UnitX);
        CheckAxis(SceneHandleKind.AxisY, pos, Vector3.UnitY);
        CheckAxis(SceneHandleKind.AxisZ, pos, Vector3.UnitZ);
        return best;

        void CheckAxis(SceneHandleKind kind, Vector3 p, Vector3 axis)
        {
            if (Project(p) is not { } a || Project(p + axis * HandleLenWorld) is not { } b) return;
            float d = DistanceToSegment(local, a, b);
            if (d <= bestD) { bestD = d; best = kind; }
        }
    }

    public IReadOnlyList<int> EntitiesIn(SceneDoc doc, Vector2 aLocal, Vector2 bLocal)
    {
        float x0 = MathF.Min(aLocal.X, bLocal.X), x1 = MathF.Max(aLocal.X, bLocal.X);
        float y0 = MathF.Min(aLocal.Y, bLocal.Y), y1 = MathF.Max(aLocal.Y, bLocal.Y);
        var ids = new List<int>();
        foreach (SceneEntity e in doc.Entities)
        {
            if (ScreenBounds(e) is not { } r) continue;
            if (r.X <= x1 && r.X + r.W >= x0 && r.Y <= y1 && r.Y + r.H >= y0) ids.Add(e.Id);
        }
        return ids;
    }

    public IReadOnlyList<SceneChange> BuildMove(SceneDoc doc, IReadOnlyList<int> ids, Vector2 screenDelta, SceneHandleKind axis, bool snap)
    {
        var changes = new List<SceneChange>(ids.Count);
        foreach (int id in ids)
        {
            if (Pos(doc, id) is not { } cur) continue;
            Vector3 delta = axis switch
            {
                SceneHandleKind.AxisX => AxisDelta(cur, Vector3.UnitX, screenDelta),
                SceneHandleKind.AxisY => AxisDelta(cur, Vector3.UnitY, screenDelta),
                SceneHandleKind.AxisZ => AxisDelta(cur, Vector3.UnitZ, screenDelta),
                _ => PlaneDelta(cur, screenDelta),
            };
            Vector3 tgt = cur + delta;
            if (snap) tgt = Snap(tgt);
            if (tgt != cur) changes.Add(new SetField(id, "transform3d", "pos", SceneValue.Of(tgt)));
        }
        return changes;
    }

    public SceneEntity OffsetDuplicate(SceneEntity entity)
    {
        if (entity.Component("transform3d") is not { } t || t.Get("pos") is not { } pos) return entity;
        return entity.WithComponent(t.With("pos", SceneValue.Of(pos.AsVec3() + new Vector3(0.75f, 0, 0.75f))));
    }

    public void Pan(Vector2 screenDelta) => _cam.Orbit(screenDelta.X * 0.01f, screenDelta.Y * 0.01f);

    public void ZoomAt(float wheelDelta, Vector2 local) => _cam.Dolly(MathF.Pow(0.9f, wheelDelta), 1.5f, 40f);

    public void ResetView()
        => _cam = new OrbitCamera(new Vector3(0, 0.4f, 0), yaw: 0.72f, pitch: 0.42f, distance: 8f,
            fovYRadians: 1.05f, aspect: _viewW / MathF.Max(1f, _viewH), near: 0.05f, far: 100f);

    public Vector2 EntityLocalCenter(SceneDoc doc, int id)
        => Pos(doc, id) is { } p && Project(p) is { } s ? s : default;

    public Vector2 LocalOfPlane(Vector2 world)
        => Project(new Vector3(world.X, 0, world.Y)) ?? default;

    private Vector3 Snap(Vector3 v)
        => new(MathF.Round(v.X / GridStep) * GridStep, MathF.Round(v.Y / GridStep) * GridStep, MathF.Round(v.Z / GridStep) * GridStep);

    private Vector3 AxisDelta(Vector3 pos, Vector3 axis, Vector2 screenDelta)
    {
        if (Project(pos) is not { } a || Project(pos + axis) is not { } b) return Vector3.Zero;
        Vector2 sa = b - a;
        float len2 = sa.LengthSquared();
        if (len2 < 1e-4f) return Vector3.Zero;
        return axis * (Vector2.Dot(screenDelta, sa) / len2);
    }

    private Vector3 PlaneDelta(Vector3 pos, Vector2 screenDelta)
    {
        if (Project(pos) is not { } start) return Vector3.Zero;
        Vector3 normal = Vector3.Normalize(_cam.Target - _cam.Eye);
        if (!RayPlane(start, pos, normal, out Vector3 a)) return Vector3.Zero;
        if (!RayPlane(start + screenDelta, pos, normal, out Vector3 b)) return Vector3.Zero;
        return b - a;
    }

    private bool RayPlane(Vector2 local, Vector3 planePoint, Vector3 planeNormal, out Vector3 hit)
    {
        Ray(local, out Vector3 origin, out Vector3 dir);
        float denom = Vector3.Dot(dir, planeNormal);
        if (MathF.Abs(denom) < 1e-5f) { hit = default; return false; }
        float t = Vector3.Dot(planePoint - origin, planeNormal) / denom;
        if (t < 0) { hit = default; return false; }
        hit = origin + dir * t;
        return true;
    }

    private void Ray(Vector2 local, out Vector3 origin, out Vector3 dir)
    {
        Matrix4x4.Invert(_cam.ViewProjection, out Matrix4x4 inv);
        float x = local.X / MathF.Max(1f, _viewW) * 2f - 1f;
        float y = 1f - local.Y / MathF.Max(1f, _viewH) * 2f;
        Vector3 near = Unproject(new Vector4(x, y, 0f, 1f), inv);
        Vector3 far = Unproject(new Vector4(x, y, 1f, 1f), inv);
        origin = near;
        dir = Vector3.Normalize(far - near);
    }

    private static Vector3 Unproject(Vector4 clip, Matrix4x4 inv)
    {
        Vector4 w = Vector4.Transform(clip, inv);
        return new Vector3(w.X, w.Y, w.Z) / w.W;
    }

    private Vector2? Project(Vector3 world)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), _cam.ViewProjection);
        if (clip.W <= 1e-5f) return null;
        float ndcX = clip.X / clip.W, ndcY = clip.Y / clip.W;
        if (float.IsNaN(ndcX) || float.IsNaN(ndcY)) return null;
        return new Vector2((ndcX + 1f) * 0.5f * _viewW, (1f - ndcY) * 0.5f * _viewH);
    }

    private RectF? ScreenBounds(SceneEntity e)
    {
        if (EntityBounds(e) is not { } b) return null;
        Span<Vector3> c = stackalloc Vector3[8];
        Corners(b.Min, b.Max, c);
        bool any = false;
        float x0 = float.PositiveInfinity, y0 = float.PositiveInfinity, x1 = float.NegativeInfinity, y1 = float.NegativeInfinity;
        foreach (Vector3 p in c)
        {
            if (Project(p) is not { } s) continue;
            any = true;
            x0 = MathF.Min(x0, s.X); y0 = MathF.Min(y0, s.Y);
            x1 = MathF.Max(x1, s.X); y1 = MathF.Max(y1, s.Y);
        }
        return any ? new RectF(x0, y0, x1 - x0, y1 - y0) : null;
    }

    private void DrawGrid()
    {
        var s = new Scene2D();
        uint major = Color2D.Rgba(108, 128, 150, 90);
        uint minor = Color2D.Rgba(108, 128, 150, 50);
        uint xColor = Color2D.Rgba(229, 83, 75, 150);
        uint zColor = Color2D.Rgba(71, 132, 238, 150);
        int n = Math.Max(1, GridHalfLines);
        for (int i = -n; i <= n; i++)
        {
            float v = i * GridStep;
            DrawLine(s, new Vector3(-n * GridStep, 0, v), new Vector3(n * GridStep, 0, v), i == 0 ? xColor : (i % 4 == 0 ? major : minor), 1);
            DrawLine(s, new Vector3(v, 0, -n * GridStep), new Vector3(v, 0, n * GridStep), i == 0 ? zColor : (i % 4 == 0 ? major : minor), 1);
        }
        _gridN.Content = s;
    }

    private void DrawEntities(SceneEditState state)
    {
        var boxes = new Scene2D();
        var selected = new Scene2D();
        var names = new Scene2D();
        foreach (SceneEntity e in state.Doc.Entities)
        {
            if (EntityBounds(e) is not { } b) continue;
            DrawBox(state.Selection.Contains(e.Id) ? selected : boxes, b.Min, b.Max,
                state.Selection.Contains(e.Id) ? Color2D.Rgba(245, 196, 80) : Color2D.Rgba(218, 226, 235, 190),
                state.Selection.Contains(e.Id) ? 2.2f : 1.2f);
            if (_font is { } font && e.Name.Length > 0 && Project(new Vector3((b.Min.X + b.Max.X) * 0.5f, b.Max.Y, (b.Min.Z + b.Max.Z) * 0.5f)) is { } p)
                font.AppendText(names, e.Name, p.X + 6, p.Y - 4, _fs, Color2D.Rgba(235, 240, 245));
        }
        _boxN.Content = boxes;
        _selN.Content = selected;
        _nameN.Content = names;
    }

    private void DrawHandles(SceneEditState state)
    {
        if (state.Selection.Main < 0 || Pos(state.Doc, state.Selection.Main) is not { } pos)
        {
            _handleN.Content = null;
            return;
        }
        var s = new Scene2D();
        DrawAxis(s, pos, Vector3.UnitX, Color2D.Rgba(229, 83, 75), "X");
        DrawAxis(s, pos, Vector3.UnitY, Color2D.Rgba(63, 185, 80), "Y");
        DrawAxis(s, pos, Vector3.UnitZ, Color2D.Rgba(71, 132, 238), "Z");
        _handleN.Content = s;
    }

    private void DrawAxis(Scene2D s, Vector3 origin, Vector3 axis, uint color, string label)
    {
        Vector3 end = origin + axis * HandleLenWorld;
        DrawLine(s, origin, end, color, 2.4f);
        if (_font is { } font && Project(end) is { } p)
            font.AppendText(s, label, p.X + 4, p.Y - 4, _fs, color);
    }

    private void DrawBox(Scene2D s, Vector3 min, Vector3 max, uint color, float width)
    {
        Span<Vector3> c = stackalloc Vector3[8];
        Corners(min, max, c);
        for (int i = 0; i < 8; i++)
        {
            if ((i & 1) == 0) DrawLine(s, c[i], c[i | 1], color, width);
            if ((i & 2) == 0) DrawLine(s, c[i], c[i | 2], color, width);
            if ((i & 4) == 0) DrawLine(s, c[i], c[i | 4], color, width);
        }
    }

    private void DrawLine(Scene2D s, Vector3 a, Vector3 b, uint color, float width)
    {
        if (Project(a) is not { } p0 || Project(b) is not { } p1) return;
        s.StrokeLine(color, width, p0.X, p0.Y, p1.X, p1.Y);
    }

    private static void Corners(Vector3 min, Vector3 max, Span<Vector3> c)
    {
        for (int i = 0; i < 8; i++)
            c[i] = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-4f) return (p - a).Length();
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return (p - (a + ab * t)).Length();
    }
}
