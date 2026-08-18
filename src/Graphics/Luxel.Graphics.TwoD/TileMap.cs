using System.Numerics;

namespace Luxel.Graphics.TwoD;

/// <summary>タイル 1 種の定義: 描画に使うアトラススプライト名 + 衝突フラグ。</summary>
public readonly record struct TileDef(string Sprite, bool Solid = false);

/// <summary>
/// タイルセット: スプライトアトラス + タイルの px サイズ + タイル id → <see cref="TileDef"/> の対応。
/// id 0 は常に「空」(描画も衝突もしない) の予約値。直交正方タイル前提 (isometric/hex は対象外)。
/// </summary>
public sealed class TileSet
{
    private readonly Dictionary<int, TileDef> _defs;

    public TileSet(SpriteAtlas atlas, int tileWidth, int tileHeight, IEnumerable<KeyValuePair<int, TileDef>> defs)
    {
        if (tileWidth <= 0 || tileHeight <= 0) throw new ArgumentOutOfRangeException(nameof(tileWidth), "タイルサイズは正。");
        Atlas = atlas;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        _defs = new Dictionary<int, TileDef>(defs);
    }

    public SpriteAtlas Atlas { get; }
    public int TileWidth { get; }
    public int TileHeight { get; }

    /// <summary>id の定義を取得 (0 や未登録は false)。</summary>
    public bool TryDef(int id, out TileDef def)
    {
        if (id != 0 && _defs.TryGetValue(id, out def)) return true;
        def = default;
        return false;
    }

    /// <summary>id が衝突タイルか (0/未登録は非衝突)。</summary>
    public bool IsSolid(int id) => id != 0 && _defs.TryGetValue(id, out TileDef d) && d.Solid;

    /// <summary>id のスプライト名 (0/未登録は null)。</summary>
    public string? Sprite(int id) => id != 0 && _defs.TryGetValue(id, out TileDef d) ? d.Sprite : null;
}

/// <summary>
/// 直交グリッドのタイルマップ (幅×高さ, 0 = 空) + <see cref="TileSet"/>。
/// 描画はチャンク単位 (<see cref="AppendChunk"/> で 1 チャンク分の image パス列を <see cref="Scene2D"/> へ焼く) で、
/// 可視チャンクのみ実体化する (<see cref="VisibleChunks"/>)。編集 (<see cref="SetTile"/>) は該当チャンクを dirty にし、
/// そのチャンクだけ再構築すればよい。衝突は物理エンジン非依存の純 AABB グリッドクエリ
/// (<see cref="QueryAabb"/> / <see cref="Sweep"/>)。タイル (tx,ty) はワールド矩形
/// [tx·tileW, (tx+1)·tileW] × [ty·tileH, (ty+1)·tileH] を占める。
/// </summary>
public sealed class TileMap
{
    private readonly int[] _tiles;
    private readonly HashSet<(int Cx, int Cy)> _dirtyChunks = new();

    public TileMap(int width, int height, TileSet tileSet, int chunkTiles = 32)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "マップサイズは正。");
        if (chunkTiles <= 0) throw new ArgumentOutOfRangeException(nameof(chunkTiles), "チャンクサイズは正。");
        Width = width;
        Height = height;
        TileSet = tileSet;
        ChunkTiles = chunkTiles;
        _tiles = new int[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public TileSet TileSet { get; }
    public int ChunkTiles { get; }

    public float TileW => TileSet.TileWidth;
    public float TileH => TileSet.TileHeight;

    public int ChunksX => (Width + ChunkTiles - 1) / ChunkTiles;
    public int ChunksY => (Height + ChunkTiles - 1) / ChunkTiles;

    /// <summary>ワールド全体のサイズ (px)。</summary>
    public RectF WorldBounds => new(0, 0, Width * TileW, Height * TileH);

    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>タイル id (範囲外は 0)。</summary>
    public int Get(int x, int y) => InBounds(x, y) ? _tiles[y * Width + x] : 0;

    /// <summary>タイルを書き換え、変化があれば所属チャンクを dirty にする。</summary>
    public void SetTile(int x, int y, int id)
    {
        if (!InBounds(x, y)) return;
        int i = y * Width + x;
        if (_tiles[i] == id) return;
        _tiles[i] = id;
        _dirtyChunks.Add((x / ChunkTiles, y / ChunkTiles));
    }

    /// <summary>タイル (tx,ty) が属するチャンク座標。</summary>
    public (int Cx, int Cy) ChunkOf(int tx, int ty) => (tx / ChunkTiles, ty / ChunkTiles);

    // ---- dirty 追跡 (保持型レイヤが「変わったチャンクだけ再構築」に使う) ----

    public IReadOnlyCollection<(int Cx, int Cy)> DirtyChunks => _dirtyChunks;
    public bool IsChunkDirty(int cx, int cy) => _dirtyChunks.Contains((cx, cy));
    public void ClearChunkDirty(int cx, int cy) => _dirtyChunks.Remove((cx, cy));
    public void ClearAllDirty() => _dirtyChunks.Clear();

    /// <summary>ワールド表示矩形と交差する可視チャンクの範囲 (両端含む、マップ内にクランプ)。
    /// 交差が無ければ <c>X1 &lt; X0</c> (空範囲) を返す。</summary>
    public (int X0, int Y0, int X1, int Y1) VisibleChunks(RectF worldView)
    {
        // 表示矩形がマップ外なら空範囲
        if (worldView.MaxX <= 0 || worldView.MinX >= Width * TileW ||
            worldView.MaxY <= 0 || worldView.MinY >= Height * TileH)
            return (0, 0, -1, -1);

        float cw = ChunkTiles * TileW, ch = ChunkTiles * TileH;
        int x0 = Math.Clamp((int)MathF.Floor(worldView.MinX / cw), 0, ChunksX - 1);
        int x1 = Math.Clamp((int)MathF.Floor((worldView.MaxX - Eps) / cw), 0, ChunksX - 1);
        int y0 = Math.Clamp((int)MathF.Floor(worldView.MinY / ch), 0, ChunksY - 1);
        int y1 = Math.Clamp((int)MathF.Floor((worldView.MaxY - Eps) / ch), 0, ChunksY - 1);
        return (x0, y0, x1, y1);
    }

    /// <summary>チャンク (cx,cy) の非空タイルを <c>Scene2D.DrawSprite</c> で <paramref name="scene"/> へ焼く
    /// (ワールド座標。タイル (tx,ty) は左上 (tx·tileW, ty·tileH))。空/スプライト未定義のタイルは飛ばす。</summary>
    public void AppendChunk(int cx, int cy, Scene2D scene)
    {
        SpriteAtlas atlas = TileSet.Atlas;
        int tx0 = cx * ChunkTiles, ty0 = cy * ChunkTiles;
        int tx1 = Math.Min(tx0 + ChunkTiles, Width), ty1 = Math.Min(ty0 + ChunkTiles, Height);
        for (int ty = ty0; ty < ty1; ty++)
            for (int tx = tx0; tx < tx1; tx++)
            {
                int id = _tiles[ty * Width + tx];
                string? sprite = TileSet.Sprite(id);
                if (sprite is null || !atlas.Contains(sprite)) continue;
                scene.DrawSprite(atlas, sprite, tx * TileW, ty * TileH);
            }
    }

    // ---- 衝突 (物理エンジン非依存の AABB グリッドクエリ) ----

    /// <summary>ワールド矩形 <paramref name="box"/> と重なる**衝突タイル**の座標を列挙する。</summary>
    public IEnumerable<(int X, int Y)> QueryAabb(RectF box)
    {
        int x0 = FloorDiv(box.MinX, TileW), x1 = FloorDiv(box.MaxX - Eps, TileW);
        int y0 = FloorDiv(box.MinY, TileH), y1 = FloorDiv(box.MaxY - Eps, TileH);
        for (int y = Math.Max(0, y0); y <= Math.Min(Height - 1, y1); y++)
            for (int x = Math.Max(0, x0); x <= Math.Min(Width - 1, x1); x++)
                if (TileSet.IsSolid(_tiles[y * Width + x]))
                    yield return (x, y);
    }

    /// <summary>AABB <paramref name="box"/> を <paramref name="delta"/> だけ動かしたときの、衝突タイルにめり込まない
    /// 移動可能量を返す (プラットフォーマの定番: X→Y の軸分離スイープ)。
    /// <paramref name="hitX"/>/<paramref name="hitY"/> は各軸で壁に当たって切り詰められたか。</summary>
    public Vector2 Sweep(RectF box, Vector2 delta, out bool hitX, out bool hitY)
    {
        hitX = hitY = false;
        float dx = SweepAxis(box, delta.X, horizontal: true, ref hitX);
        RectF movedX = box with { X = box.X + dx };
        float dy = SweepAxis(movedX, delta.Y, horizontal: false, ref hitY);
        return new Vector2(dx, dy);
    }

    private float SweepAxis(RectF box, float d, bool horizontal, ref bool hit)
    {
        if (MathF.Abs(d) < Eps) return 0f;
        float size = horizontal ? TileW : TileH;
        // 垂直方向 (掃引軸に直交) のタイル行/列レンジ
        int pMin = horizontal ? FloorDiv(box.MinY, TileH) : FloorDiv(box.MinX, TileW);
        int pMax = horizontal ? FloorDiv(box.MaxY - Eps, TileH) : FloorDiv(box.MaxX - Eps, TileW);
        float lead = d > 0 ? (horizontal ? box.MaxX : box.MaxY) : (horizontal ? box.MinX : box.MinY);
        float allowed = d;

        int c0 = FloorDiv(lead, size);
        int c1 = FloorDiv(lead + d, size);
        int lo = Math.Min(c0, c1), hi = Math.Max(c0, c1);
        for (int c = lo; c <= hi; c++)
            for (int p = pMin; p <= pMax; p++)
            {
                int tx = horizontal ? c : p, ty = horizontal ? p : c;
                if (!InBounds(tx, ty) || !TileSet.IsSolid(_tiles[ty * Width + tx])) continue;
                if (d > 0)
                {
                    float gap = c * size - lead;           // タイル手前 (左/上) 端までの隙間
                    if (gap < allowed) { allowed = MathF.Max(0f, gap); hit = true; }
                }
                else
                {
                    float gap = (c + 1) * size - lead;      // タイル奥 (右/下) 端までの隙間 (負)
                    if (gap > allowed) { allowed = MathF.Min(0f, gap); hit = true; }
                }
            }
        return allowed;
    }

    private const float Eps = 1e-4f;
    private static int FloorDiv(float v, float size) => (int)MathF.Floor(v / size);

    // ---- 読み込み (v1: 自前 CSV) ----

    /// <summary>行ごとにカンマ区切りの整数 id を並べた CSV からマップを作る (空行/空白は無視)。
    /// 行数 = 高さ、最長行 = 幅 (足りない列は 0)。</summary>
    public static TileMap FromCsv(TileSet tileSet, string csv, int chunkTiles = 32)
    {
        string[] lines = csv.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int[][] rows = new int[lines.Length][];
        int width = 0;
        for (int y = 0; y < lines.Length; y++)
        {
            string[] cells = lines[y].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            rows[y] = Array.ConvertAll(cells, int.Parse);
            width = Math.Max(width, rows[y].Length);
        }
        var map = new TileMap(Math.Max(1, width), Math.Max(1, rows.Length), tileSet, chunkTiles);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                map._tiles[y * map.Width + x] = rows[y][x];
        map.ClearAllDirty();   // 初期ロードは dirty 扱いしない (初回はフル構築)
        return map;
    }

    /// <summary>Tiled (.tmj = JSON) の最小 import: 最初のタイルレイヤ (<c>"type":"tilelayer"</c>) の
    /// <c>data</c> (行優先の gid 配列) を読む。gid の上位 3 ビット (フリップフラグ) は落とし、
    /// 残りをそのままタイル id として扱う (0 = 空)。TileSet の id は Tiled の gid に合わせておくこと。
    /// オブジェクトレイヤ/無限マップ/複数レイヤ/tileset の firstgid オフセットは対象外 (v1)。</summary>
    public static TileMap FromTiledJson(TileSet tileSet, string json, int chunkTiles = 32)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        System.Text.Json.JsonElement root = doc.RootElement;
        int w = root.GetProperty("width").GetInt32();
        int h = root.GetProperty("height").GetInt32();

        System.Text.Json.JsonElement layer = default;
        bool found = false;
        foreach (System.Text.Json.JsonElement l in root.GetProperty("layers").EnumerateArray())
            if (l.TryGetProperty("type", out var t) && t.GetString() == "tilelayer")
            {
                layer = l;
                found = true;
                break;
            }
        if (!found) throw new FormatException("Tiled JSON: tilelayer が見つかりません。");

        var map = new TileMap(w, h, tileSet, chunkTiles);
        int i = 0;
        foreach (System.Text.Json.JsonElement cell in layer.GetProperty("data").EnumerateArray())
        {
            if (i >= w * h) break;
            long gid = cell.GetInt64() & 0x1FFFFFFF;   // フリップフラグ (上位 3 bit) を除去
            map._tiles[i++] = (int)gid;
        }
        map.ClearAllDirty();
        return map;
    }
}
