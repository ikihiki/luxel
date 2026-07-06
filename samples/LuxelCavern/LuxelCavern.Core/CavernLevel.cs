using System.Numerics;
using Luxel.TwoD;

namespace LuxelCavern.Core;

/// <summary>
/// ステージ (縦切り 1 枚) の定義 — タイルアトラス (矩形)・タイルセット・レベル配置・スポーン。純データ
/// (GPU 非依存)。アトラスのピクセルは描画側 (Gallery ストーリー / exe) が焼いて <see cref="SpriteAtlas.Bind"/> する
/// (セル配置は <see cref="BuildAtlas"/> の矩形と一致させること)。将来 Tiled (.tmj) 読み込みに置換予定。
/// </summary>
public static class CavernLevel
{
    public const int Tile = 16;
    public const int Width = 44, Height = 24;

    // タイル id
    public const int Grass = 1, Dirt = 2, Wall = 3;

    /// <summary>アトラス矩形 (32×32 の 3 セル: grass=(0,0) / dirt=(16,0) / wall=(0,16))。未バインド。</summary>
    public static SpriteAtlas BuildAtlas() => new("proc://cavern", [
        new("grass", new SpriteRect(0, 0, Tile, Tile)),
        new("dirt", new SpriteRect(Tile, 0, Tile, Tile)),
        new("wall", new SpriteRect(0, Tile, Tile, Tile)),
    ]);

    public static TileSet BuildTileSet(SpriteAtlas atlas) => new(atlas, Tile, Tile, [
        new(Grass, new TileDef("grass", Solid: true)),
        new(Dirt, new TileDef("dirt", Solid: true)),
        new(Wall, new TileDef("wall", Solid: true)),
    ]);

    /// <summary>コード定義のレベル: 地面 + 浮き床 + 壁柱 + 右側の段差。</summary>
    public static TileMap Build(TileSet ts)
    {
        var map = new TileMap(Width, Height, ts);
        int floor = Height - 5;   // 地面の上端タイル行 (y=19)

        for (int x = 0; x < Width; x++)
        {
            map.SetTile(x, floor, Grass);
            for (int y = floor + 1; y < Height; y++) map.SetTile(x, y, Dirt);   // 土で埋める
        }
        // 浮き床
        for (int x = 12; x <= 17; x++) map.SetTile(x, floor - 4, Grass);
        // 壁柱 (ジャンプで越える障害、3 タイル高)
        for (int y = floor - 3; y < floor; y++) map.SetTile(24, y, Wall);
        // 右側の段差 (1 段高い地面)
        for (int x = 34; x < Width; x++)
        {
            map.SetTile(x, floor - 2, Grass);
            map.SetTile(x, floor - 1, Dirt);
        }
        map.ClearAllDirty();
        return map;
    }

    /// <summary>プレイヤーの初期位置 (AABB 左上、地面の少し上)。</summary>
    public static Vector2 Spawn => new(3 * Tile, (Height - 8) * Tile);
}
