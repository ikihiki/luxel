using System.Numerics;
using Luxel.Graphics.TwoD;

namespace LuxelCavern.Core;

/// <summary>
/// ステージ (縦切り 1 枚) の定義 — タイルアトラス (矩形)・タイルセット・アトラス束縛のヘルパ。純データ (GPU 非依存)。
/// アトラスのピクセルは描画側 (Gallery / exe) が焼いて <see cref="SpriteAtlas.Bind"/> する
/// (32×32 の 4 セル: grass(0,0) / dirt(16,0) / wall(0,16) / spike(16,16))。
/// **レベル配置 (タイル + エンティティ) は Tiled (.tmj) から** — <see cref="CavernLevelLoader"/> が
/// <see cref="Luxel.Resources.ResourceSystem"/> 経由で埋め込み <c>levels/cavern1.tmj</c> を読み、
/// <see cref="CavernTiled"/> が <see cref="CavernSim"/> に組む。
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

    /// <summary>プレイヤーのスポーン位置 (Tiled 非対象 — コード側の定数)。</summary>
    public static Vector2 Spawn => new(3 * Tile, (Height - 8) * Tile);
}
