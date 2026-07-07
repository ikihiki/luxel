using System.Numerics;
using LuxelCavern.Core;
using Luxel.TwoD;

namespace Luxel.Tests;

/// <summary>
/// Tiled (.tmj) レベル読み込み <see cref="CavernTiled"/> の決定的テスト: 埋め込み解決 / タイル層 (FromTiledJson) の
/// 寸法と代表タイル / オブジェクト層のエンティティ数・種別・位置 / 松明。golden 同一性は Gallery の e2e が担保。
/// </summary>
public class CavernTiledTests
{
    private static CavernSim Load(out Vector2[] torches)
    {
        TileSet ts = CavernLevel.BuildTileSet(CavernLevel.BuildAtlas());
        return CavernTiled.BuildSim(CavernTiled.LoadEmbeddedJson(), ts,
            CavernLevel.Spawn, new Vector2(12, 22), out torches);
    }

    [Fact]
    public void EmbeddedResource_IsResolvable()
    {
        string json = CavernTiled.LoadEmbeddedJson();
        Assert.Contains("\"tilelayer\"", json);
        Assert.Contains("\"objectgroup\"", json);
    }

    [Fact]
    public void TileLayer_HasExpectedDimensionsAndTiles()
    {
        CavernSim sim = Load(out _);
        Assert.Equal(CavernLevel.Width, sim.Map.Width);
        Assert.Equal(CavernLevel.Height, sim.Map.Height);
        // 代表タイル: 地面上端 = grass、その下 = dirt、トゲ列、床上の空 = 0
        Assert.Equal(CavernLevel.Grass, sim.Map.Get(0, CavernLevel.Floor));
        Assert.Equal(CavernLevel.Dirt, sim.Map.Get(0, CavernLevel.Floor + 1));
        Assert.Equal(CavernLevel.Spike, sim.Map.Get(31, CavernLevel.Floor));
        Assert.Equal(0, sim.Map.Get(0, 0));
    }

    [Fact]
    public void ObjectLayer_YieldsExpectedEntityCounts()
    {
        CavernSim sim = Load(out Vector2[] torches);
        Assert.Equal(9, sim.Pickups.Count);                         // コイン 6 + 鍵 3
        Assert.Equal(6, sim.Pickups.Count(p => !p.IsKey));
        Assert.Equal(3, sim.Pickups.Count(p => p.IsKey));
        Assert.Single(sim.Enemies);
        Assert.Single(sim.Flyers);
        Assert.Equal(2, sim.Checkpoints.Count);
        Assert.Equal(2, torches.Length);
    }

    [Fact]
    public void Entities_LoadWithCorrectPositionsAndProperties()
    {
        CavernSim sim = Load(out _);
        Assert.Equal(new Vector2(656, 240), sim.DoorPos);           // 扉 (ゴール)

        Walker w = sim.Enemies[0];
        Assert.Equal(new Vector2(250, 290), w.Pos);
        Assert.Equal(-42f, w.VelX, 3);
        Assert.Equal(190f, w.MinX, 3);
        Assert.Equal(310f, w.MaxX, 3);

        Flyer f = sim.Flyers[0];
        Assert.Equal(new Vector2(215, 248), f.Home);
        Assert.Equal(34f, f.AmpX, 3);
        Assert.Equal(1.0f, f.Freq, 3);

        // コインは先頭、鍵は後半 (ファイル順 = レンダリング順を保つ)
        Assert.False(sim.Pickups[0].IsKey);
        Assert.Equal(new Vector2(90, 288), sim.Pickups[0].Pos);
        Assert.True(sim.Pickups[6].IsKey);
    }

    [Fact]
    public void MissingResourceName_Throws()
        => Assert.Throws<FileNotFoundException>(() => CavernTiled.LoadEmbeddedJson("does-not-exist.tmj"));
}
