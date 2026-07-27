using System.Linq;
using System.Numerics;
using Luxel.Graphics.TwoD;

namespace Luxel.Tests;

/// <summary>
/// タイルマップ (タスク 18 ステップ 3-4) の GPU 不要・決定的テスト:
/// TileSet の衝突/スプライト解決 / SetTile のチャンク dirty / VisibleChunks の可視範囲 /
/// AppendChunk の非空タイル数 / QueryAabb / Sweep (壁へめり込み・沿い移動・ゼロ・左右上下・境界) /
/// FromCsv パース / TileMapLayer (保持型) の可視チャンク実体化と dirty 再構築 (headless)。
/// </summary>
public class TileMapTests
{
    private const int TW = 16, TH = 16;

    private static TileSet MakeTileSet()
    {
        var atlas = new SpriteAtlas("atlas", [
            new("grass", new SpriteRect(0, 0, TW, TH)),
            new("wall", new SpriteRect(TW, 0, TW, TH)),
        ]);
        return new TileSet(atlas, TW, TH, [
            new(1, new TileDef("grass", Solid: false)),
            new(2, new TileDef("wall", Solid: true)),
        ]);
    }

    // ---- TileSet ----

    [Fact]
    public void TileSet_IsSolid()
    {
        TileSet ts = MakeTileSet();
        Assert.False(ts.IsSolid(0));   // 空
        Assert.False(ts.IsSolid(1));   // grass 非衝突
        Assert.True(ts.IsSolid(2));    // wall 衝突
        Assert.False(ts.IsSolid(99));  // 未登録
    }

    [Fact]
    public void TileSet_Sprite()
    {
        TileSet ts = MakeTileSet();
        Assert.Equal("grass", ts.Sprite(1));
        Assert.Equal("wall", ts.Sprite(2));
        Assert.Null(ts.Sprite(0));
        Assert.Null(ts.Sprite(99));
    }

    // ---- SetTile / dirty / chunks ----

    [Fact]
    public void SetTile_MarksOwningChunkDirty()
    {
        var map = new TileMap(40, 40, MakeTileSet(), chunkTiles: 32);   // ChunksX=2
        Assert.Empty(map.DirtyChunks);
        map.SetTile(35, 5, 2);
        Assert.Equal(2, map.Get(35, 5));
        Assert.True(map.IsChunkDirty(1, 0));
        Assert.False(map.IsChunkDirty(0, 0));
    }

    [Fact]
    public void SetTile_SameValue_NoDirty()
    {
        var map = new TileMap(40, 40, MakeTileSet());
        map.SetTile(1, 1, 2);
        map.ClearAllDirty();
        map.SetTile(1, 1, 2);   // 同値 → dirty にしない
        Assert.Empty(map.DirtyChunks);
    }

    [Fact]
    public void SetTile_OutOfBounds_Ignored()
    {
        var map = new TileMap(8, 8, MakeTileSet());
        map.SetTile(-1, 0, 2);
        map.SetTile(0, 99, 2);
        Assert.Empty(map.DirtyChunks);
        Assert.Equal(0, map.Get(-1, 0));
    }

    [Fact]
    public void VisibleChunks_InsideAndOutside()
    {
        var map = new TileMap(64, 64, MakeTileSet(), chunkTiles: 32);   // チャンク世界幅 = 512, ChunksX=2
        // 500px 幅は 1 チャンク目に収まる
        Assert.Equal((0, 0, 0, 0), map.VisibleChunks(new RectF(0, 0, 500, 500)));
        // 600px は 2 チャンク跨ぐ
        Assert.Equal((0, 0, 1, 1), map.VisibleChunks(new RectF(0, 0, 600, 600)));
        // マップ外は空範囲 (X1 < X0)
        (int x0, _, int x1, _) = map.VisibleChunks(new RectF(2000, 2000, 100, 100));
        Assert.True(x1 < x0);
    }

    [Fact]
    public void AppendChunk_EncodesNonEmptyTilesOnly()
    {
        var map = new TileMap(4, 4, MakeTileSet(), chunkTiles: 32);   // 1 チャンク
        map.SetTile(0, 0, 1);
        map.SetTile(1, 0, 2);
        map.SetTile(2, 2, 1);
        map.SetTile(3, 3, 9);   // 未定義 id → 描かない

        var scene = new Scene2D();
        map.AppendChunk(0, 0, scene);
        Assert.Equal(3, scene.CountEncoded().Paths);   // 空 + 未定義を除いた 3 枚
    }

    // ---- QueryAabb ----

    [Fact]
    public void QueryAabb_ReturnsOnlySolidOverlaps()
    {
        var map = new TileMap(8, 8, MakeTileSet());
        map.SetTile(2, 2, 2);   // wall (world 32..48)
        map.SetTile(3, 2, 1);   // grass 非衝突

        // (32,32)-(48,48) にかかる矩形 → wall のみ
        Assert.Equal([(2, 2)], map.QueryAabb(new RectF(34, 34, 4, 4)).ToArray());
        // grass だけにかかる → 空
        Assert.Empty(map.QueryAabb(new RectF(50, 34, 4, 4)));
    }

    // ---- Sweep ----

    [Fact]
    public void Sweep_IntoWallRight_ClampsAndFlagsHit()
    {
        var map = new TileMap(8, 4, MakeTileSet());
        map.SetTile(3, 0, 2);   // wall world x[48,64]
        Vector2 d = map.Sweep(new RectF(0, 0, 16, 16), new Vector2(100, 0), out bool hx, out bool hy);
        Assert.Equal(32f, d.X, 3);   // 16 → 48 の左端まで
        Assert.Equal(0f, d.Y, 3);
        Assert.True(hx);
        Assert.False(hy);
    }

    [Fact]
    public void Sweep_IntoWallLeft_Clamps()
    {
        var map = new TileMap(8, 4, MakeTileSet());
        map.SetTile(3, 0, 2);   // wall world x[48,64]
        Vector2 d = map.Sweep(new RectF(80, 0, 16, 16), new Vector2(-100, 0), out bool hx, out _);
        Assert.Equal(-16f, d.X, 3);   // 80 → 64 の右端まで
        Assert.True(hx);
    }

    [Fact]
    public void Sweep_IntoFloorDown_Clamps()
    {
        var map = new TileMap(4, 8, MakeTileSet());
        map.SetTile(0, 3, 2);   // floor world y[48,64]
        Vector2 d = map.Sweep(new RectF(0, 0, 16, 16), new Vector2(0, 100), out _, out bool hy);
        Assert.Equal(32f, d.Y, 3);
        Assert.True(hy);
    }

    [Fact]
    public void Sweep_AlongOpenSpace_NoHit()
    {
        var map = new TileMap(8, 8, MakeTileSet());
        map.SetTile(3, 5, 2);   // 経路外の壁
        Vector2 d = map.Sweep(new RectF(0, 0, 16, 16), new Vector2(10, 0), out bool hx, out bool hy);
        Assert.Equal(10f, d.X, 3);
        Assert.False(hx);
        Assert.False(hy);
    }

    [Fact]
    public void Sweep_ZeroDelta_ReturnsZero()
    {
        var map = new TileMap(8, 8, MakeTileSet());
        map.SetTile(1, 0, 2);
        Vector2 d = map.Sweep(new RectF(0, 0, 16, 16), Vector2.Zero, out bool hx, out bool hy);
        Assert.Equal(Vector2.Zero, d);
        Assert.False(hx);
        Assert.False(hy);
    }

    [Fact]
    public void Sweep_FlushAgainstWall_CannotMove()
    {
        var map = new TileMap(8, 4, MakeTileSet());
        map.SetTile(1, 0, 2);   // wall world x[16,32]
        // box 右端がちょうど 16 (壁の左端) に接している → 右へ動けない
        Vector2 d = map.Sweep(new RectF(0, 0, 16, 16), new Vector2(20, 0), out bool hx, out _);
        Assert.Equal(0f, d.X, 3);
        Assert.True(hx);
    }

    // ---- FromCsv ----

    [Fact]
    public void FromCsv_ParsesGridAndSize()
    {
        TileMap map = TileMap.FromCsv(MakeTileSet(), "1,1,2\n0,2,0\n", chunkTiles: 32);
        Assert.Equal(3, map.Width);
        Assert.Equal(2, map.Height);
        Assert.Equal(1, map.Get(0, 0));
        Assert.Equal(2, map.Get(2, 0));
        Assert.Equal(2, map.Get(1, 1));
        Assert.Equal(0, map.Get(0, 1));
        Assert.Empty(map.DirtyChunks);   // 初期ロードは dirty 扱いしない
    }

    // ---- Tiled (.tmj) import ----

    [Fact]
    public void FromTiledJson_ReadsFirstTileLayer()
    {
        const string tmj = """
        {
          "width": 3, "height": 2,
          "layers": [
            { "type": "objectgroup", "objects": [] },
            { "type": "tilelayer", "data": [1, 0, 2, 0, 2147483650, 0] }
          ]
        }
        """;
        // 2147483650 = 0x80000002 (水平フリップフラグ + gid 2) → フラグ除去で 2
        TileMap map = TileMap.FromTiledJson(MakeTileSet(), tmj, chunkTiles: 32);
        Assert.Equal(3, map.Width);
        Assert.Equal(2, map.Height);
        Assert.Equal(1, map.Get(0, 0));
        Assert.Equal(2, map.Get(2, 0));
        Assert.Equal(2, map.Get(1, 1));   // フリップフラグを落として gid 2
        Assert.Equal(0, map.Get(0, 1));
    }

    // ---- TileMapLayer (headless) ----

    [Fact]
    public void Layer_RealizesOnlyVisibleChunks_AndRebuildsOnEdit()
    {
        using var canvas = new RetainedCanvas();   // headless (rasterizer なし)
        var map = new TileMap(64, 64, MakeTileSet(), chunkTiles: 32);   // チャンク世界幅 512
        map.SetTile(0, 0, 1);
        map.SetTile(1, 0, 2);
        map.ClearAllDirty();

        var layer = new TileMapLayer(canvas, canvas.Root, map);
        layer.Update(new RectF(0, 0, 400, 400));   // 400 < 512 → チャンク (0,0) だけ可視

        Assert.Equal(1, layer.RealizedChunkCount);
        UiNode node = layer.Root.Children[0];
        Assert.Equal(2, node.Content!.CountEncoded().Paths);

        // 可視チャンク内のタイルを編集 → dirty → Update で再構築
        map.SetTile(2, 0, 1);
        layer.Update(new RectF(0, 0, 400, 400));
        Assert.Equal(3, node.Content!.CountEncoded().Paths);
        Assert.True(node.Visible);

        // マップ外へスクロール → ノードは破棄せず非表示、実体化数は維持
        layer.Update(new RectF(5000, 5000, 400, 400));
        Assert.Equal(1, layer.RealizedChunkCount);
        Assert.False(node.Visible);
    }
}
