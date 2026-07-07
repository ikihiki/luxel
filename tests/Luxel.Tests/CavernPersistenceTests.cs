using LuxelCavern.Core;
using Luxel.Settings;

namespace Luxel.Tests;

/// <summary>
/// セーブ永続化 <see cref="CavernPersistence"/> の決定的テスト (<see cref="InMemoryFileStore"/> で file IO 非依存):
/// 保存→読込の往復 / 未保存は null / 消去後は null / 壊れた JSON は例外にせず null。
/// </summary>
public class CavernPersistenceTests
{
    private static CavernSave SampleSave()
    {
        var sim = CavernLevel.CreateSim();
        sim.Coins = 7;
        sim.Keys = 2;
        sim.Hp = 2;
        return sim.Export();
    }

    [Fact]
    public void TryLoad_WhenEmpty_ReturnsNull()
    {
        var files = new InMemoryFileStore();
        Assert.Null(CavernPersistence.TryLoad(files));
        Assert.False(CavernPersistence.HasSave(files));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsProgress()
    {
        var files = new InMemoryFileStore();
        CavernSave save = SampleSave();
        CavernPersistence.Save(files, save);

        Assert.True(CavernPersistence.HasSave(files));
        CavernSave? loaded = CavernPersistence.TryLoad(files);
        Assert.NotNull(loaded);
        Assert.Equal(save.Coins, loaded!.Coins);
        Assert.Equal(save.Keys, loaded.Keys);
        Assert.Equal(save.Hp, loaded.Hp);
        Assert.Equal(save.PlayerX, loaded.PlayerX);
        Assert.Equal(save.PickupsCollected.Length, loaded.PickupsCollected.Length);
    }

    [Fact]
    public void Clear_MakesSaveDisappear()
    {
        var files = new InMemoryFileStore();
        CavernPersistence.Save(files, SampleSave());
        Assert.True(CavernPersistence.HasSave(files));

        CavernPersistence.Clear(files);
        Assert.False(CavernPersistence.HasSave(files));
        Assert.Null(CavernPersistence.TryLoad(files));
    }

    [Fact]
    public void TryLoad_WhenCorrupt_ReturnsNullWithoutThrowing()
    {
        var files = new InMemoryFileStore();
        files.Write(CavernPersistence.SaveName, "{ this is not valid json ]");
        Assert.Null(CavernPersistence.TryLoad(files));   // 例外を投げない
        Assert.False(CavernPersistence.HasSave(files));
    }

    [Fact]
    public void Continue_FromLoadedSave_RestoresIntoGameFlow()
    {
        var files = new InMemoryFileStore();
        CavernPersistence.Save(files, SampleSave());

        CavernSave? loaded = CavernPersistence.TryLoad(files);
        var flow = new GameFlow();
        flow.Continue(loaded!);
        Assert.Equal(GameState.Playing, flow.State);
        Assert.Equal(7, flow.Sim!.Coins);
        Assert.Equal(2, flow.Sim.Keys);
    }
}
