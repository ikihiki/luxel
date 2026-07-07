using System.Numerics;
using LuxelCavern.Core;
using Luxel.TwoD;

namespace Luxel.Tests;

/// <summary>
/// テスト用のレベル生成ヘルパ。レベル読み込みは <see cref="CavernLevelLoader"/> (ResourceSystem 経由) に一本化する
/// (各呼び出しで使い捨てローダを作る — 決定的・共有 static 無し)。
/// </summary>
internal static class CavernTestLevel
{
    public static CavernSim CreateSim() => new CavernLevelLoader().CreateSim();

    public static TileMap BuildMap(TileSet ts) => new CavernLevelLoader().BuildMap(ts);

    public static GameFlow NewFlow() => new(new CavernLevelLoader());

    public static string Json() => new CavernLevelLoader().LoadJson();

    public static CavernSim CreateSim(out Vector2[] torches)
    {
        var loader = new CavernLevelLoader();
        CavernSim sim = loader.CreateSim();
        torches = loader.Torches;
        return sim;
    }
}
