using System.Numerics;
using Luxel.TwoD;

namespace LuxelCavern.Core;

/// <summary>
/// ステージ (縦切り 1 枚) の定義 — タイルアトラス (矩形)・タイルセット・レベル配置・エンティティ配置。純データ
/// (GPU 非依存)。アトラスのピクセルは描画側 (Gallery / exe) が焼いて <see cref="SpriteAtlas.Bind"/> する
/// (32×32 の 4 セル: grass(0,0) / dirt(16,0) / wall(0,16) / spike(16,16))。将来 Tiled (.tmj) 読み込みに置換予定。
/// </summary>
public static class CavernLevel
{
    public const int Tile = 16;
    public const int Width = 44, Height = 24;

    // タイル id
    public const int Grass = 1, Dirt = 2, Wall = 3, Spike = 4;
    /// <summary>地面の上端タイル行。</summary>
    public const int Floor = Height - 5;   // 19

    public static SpriteAtlas BuildAtlas() => new("proc://cavern", [
        new("grass", new SpriteRect(0, 0, Tile, Tile)),
        new("dirt", new SpriteRect(Tile, 0, Tile, Tile)),
        new("wall", new SpriteRect(0, Tile, Tile, Tile)),
        new("spike", new SpriteRect(Tile, Tile, Tile, Tile)),
    ]);

    public static TileSet BuildTileSet(SpriteAtlas atlas) => new(atlas, Tile, Tile, [
        new(Grass, new TileDef("grass", Solid: true)),
        new(Dirt, new TileDef("dirt", Solid: true)),
        new(Wall, new TileDef("wall", Solid: true)),
        new(Spike, new TileDef("spike", Solid: false)),   // トゲは通過するが接触ダメージ
    ]);

    public static TileMap Build(TileSet ts)
    {
        var map = new TileMap(Width, Height, ts);
        for (int x = 0; x < Width; x++)
        {
            map.SetTile(x, Floor, Grass);
            for (int y = Floor + 1; y < Height; y++) map.SetTile(x, y, Dirt);
        }
        for (int x = 12; x <= 17; x++) map.SetTile(x, Floor - 4, Grass);      // 浮き床
        for (int y = Floor - 3; y < Floor; y++) map.SetTile(24, y, Wall);     // 壁柱
        for (int x = 34; x < Width; x++)                                       // 右の段差
        {
            map.SetTile(x, Floor - 2, Grass);
            map.SetTile(x, Floor - 1, Dirt);
        }
        map.SetTile(31, Floor, Spike);   // トゲ (地面に埋め込み)
        map.SetTile(32, Floor, Spike);
        map.ClearAllDirty();
        return map;
    }

    public static Vector2 Spawn => new(3 * Tile, (Height - 8) * Tile);

    /// <summary>マップ + エンティティ (コイン/鍵/敵/扉) を配置した完全な <see cref="CavernSim"/> を作る。</summary>
    public static CavernSim CreateSim()
    {
        TileMap map = Build(BuildTileSet(BuildAtlas()));
        var sim = new CavernSim(map, Spawn, new Vector2(12, 22));

        float groundTop = Floor * Tile;          // 304
        float platTop = (Floor - 4) * Tile;      // 240
        float raisedTop = (Floor - 2) * Tile;    // 272

        // コイン (地面 + 浮き床)
        AddCoin(sim, 90, groundTop - 16);
        AddCoin(sim, 130, groundTop - 16);
        AddCoin(sim, 170, groundTop - 16);
        AddCoin(sim, 208, platTop - 14);
        AddCoin(sim, 244, platTop - 14);
        AddCoin(sim, 470, raisedTop - 16);

        // 鍵 3 個 (浮き床・地面・段差)
        AddKey(sim, 226, platTop - 16);
        AddKey(sim, 300, groundTop - 16);
        AddKey(sim, 560, raisedTop - 16);

        // 扉 (段差の上、ゴール)
        sim.DoorPos = new Vector2(41 * Tile, raisedTop - sim.DoorSize.Y);

        // 巡回敵 (序盤の地面を左右に歩く)
        sim.Enemies.Add(new Walker
        {
            Pos = new Vector2(250, groundTop - 14),
            VelX = -42f,
            MinX = 190, MaxX = 310,
        });

        return sim;
    }

    private static void AddCoin(CavernSim sim, float x, float y)
        => sim.Pickups.Add(new Pickup { Pos = new Vector2(x, y), Size = 10, IsKey = false });

    private static void AddKey(CavernSim sim, float x, float y)
        => sim.Pickups.Add(new Pickup { Pos = new Vector2(x, y), Size = 12, IsKey = true });
}
