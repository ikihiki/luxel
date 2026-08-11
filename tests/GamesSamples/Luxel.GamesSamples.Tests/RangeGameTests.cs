using System.Numerics;
using Luxel.Settings;
using LuxelRange.Core;
using Xunit;

namespace Luxel.Tests;

/// <summary>ゲームフロー (Title/Play/Result) + ハイスコア永続化 (SettingsStore、タスク 15)。GPU 不要・決定的。</summary>
public class RangeGameTests
{
    [Fact]
    public void Flow_TitlePlayResult_SubmitsHighScore()
    {
        var files = new InMemoryFileStore();
        using var game = new RangeGame(files);
        Assert.Equal(RangeState.Title, game.State);
        Assert.Equal(0, game.HighScore);

        game.StartRound();
        Assert.Equal(RangeState.Play, game.State);

        // 前列に小物/Fox が無く弾道の地形も開けている的 (x=-6) をその中心高さへ当てる → +100
        float ty = RangeTerrain.Height(-6f, -8f) + 0.8f;
        Assert.True(game.Fire(new Vector3(-6f, ty, 5f), new Vector3(0, 0, -1)));
        for (int i = 0; i < 30; i++) game.Step();
        Assert.True(game.Score >= 100);   // 命中した

        // 残弾を撃ち切る (真下 = miss)
        while (game.Sim.AmmoLeft > 0) game.Fire(new Vector3(0, 8, 5), new Vector3(0, -1, 0));
        // settle (2s) 経過で Result へ
        for (int i = 0; i < 300; i++) game.Step();

        Assert.Equal(RangeState.Result, game.State);
        int finalScore = game.Score;
        Assert.True(finalScore >= 100);            // 少なくとも 1 的
        Assert.Equal(finalScore, game.HighScore);  // ハイスコア更新
    }

    [Fact]
    public void Fire_OnlyInPlayState()
    {
        var files = new InMemoryFileStore();
        using var game = new RangeGame(files);
        Assert.False(game.Fire(Vector3.Zero, -Vector3.UnitZ));   // Title では撃てない
        game.StartRound();
        Assert.True(game.Fire(new Vector3(0, 1.3f, 5f), -Vector3.UnitZ));
    }

    [Fact]
    public void SfxBank_SynthesizesAllCuesAndBgm()
    {
        var clips = RangeSfxBank.Build();
        Assert.Equal(4, clips.Count);
        foreach (RangeSfx cue in Enum.GetValues<RangeSfx>())
            Assert.True(clips.ContainsKey(cue), $"cue {cue} が無い");
        Assert.NotNull(RangeSfxBank.BuildBgm());   // BGM 合成 (device 不要)
    }

    [Fact]
    public void HighScore_PersistsAcrossGames()
    {
        var files = new InMemoryFileStore();
        using (var g1 = new RangeGame(files))
        {
            Assert.True(g1.Settings.SubmitScore(1234));
            Assert.False(g1.Settings.SubmitScore(1000));   // 低いスコアは更新しない
        }
        using var g2 = new RangeGame(files);   // 同じ store から読み直す
        Assert.Equal(1234, g2.HighScore);
    }
}
