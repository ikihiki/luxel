using Luxel.Controls;
using Luxel.Typography;
using Luxel.UI;
using Xunit;
using static Luxel.Controls.Kit;

namespace Luxel.Tests;

/// <summary>CC-M1: CompositeControl — Build 委譲レイアウトと Rebuild の基本動作。</summary>
public class CompositeControlTests
{
    private static LayoutContext Ctx() => new() { Font = VectorFont.LoadSystem() };

    private sealed class TwoTexts : CompositeControl
    {
        public int Builds;
        public string Second = "b";
        protected override Widget Build()
        {
            Builds++;
            return VStack(spacing: 4)[Text("a"), Text(Second)];
        }
        public void ChangeStructure(string s) { Second = s; Rebuild(); }
    }

    [Fact]
    public void Layout_DelegatesToBuiltRoot()
    {
        var c = new TwoTexts();
        c.Layout(Constraints.LooseW(200, 200), Ctx());

        Assert.Equal(1, c.Builds);                    // Build は初回レイアウトで 1 回
        Assert.NotNull(c.Root);
        Assert.Equal(c.Root!.Size, c.Size);           // サイズはルートへ委譲
        Assert.True(c.Size.Height > 0);
        Assert.Same(c.Root, Assert.Single(c.DebugChildren()));
    }

    [Fact]
    public void Layout_DoesNotRebuildEveryPass()
    {
        var c = new TwoTexts();
        c.Layout(Constraints.LooseW(200, 200), Ctx());
        c.Layout(Constraints.LooseW(300, 300), Ctx());   // 再レイアウトしても Build は 1 回のまま
        Assert.Equal(1, c.Builds);
    }

    [Fact]
    public void Rebuild_DropsRoot_AndBuildsOnNextLayout()
    {
        var c = new TwoTexts();
        c.Layout(Constraints.LooseW(200, 200), Ctx());
        Widget first = c.Root!;

        c.ChangeStructure("bbbb");                    // 構造変化 → Rebuild (MarkNeedsRealize は未実体化で no-op)
        Assert.Null(c.Root);

        c.Layout(Constraints.LooseW(200, 200), Ctx());
        Assert.Equal(2, c.Builds);
        Assert.NotSame(first, c.Root);
    }

    // ---- TrackedBuild (CC-M4): Build 中に読んだ signal の変化で自動 Rebuild ----

    private sealed class Tracked : CompositeControl
    {
        public readonly Signal<int> Count = new(1);
        public int Builds;
        private readonly bool _track;
        public Tracked(bool track = true) => _track = track;
        protected override bool TrackBuild => _track;
        protected override Widget Build()
        {
            Builds++;
            var texts = Enumerable.Range(0, Count.Value).Select(i => (Widget)Text($"row {i}")).ToArray();
            return VStack(spacing: 2)[texts];
        }
    }

    [Fact]
    public void TrackedBuild_AutoRebuildsOnSignalChange()
    {
        var c = new Tracked();
        c.Layout(Constraints.LooseW(200, 400), Ctx());
        Assert.Equal(1, c.Builds);
        float h1 = c.Size.Height;

        c.Count.Value = 3;                            // Build で読んだ signal → 自動 Rebuild
        Assert.Null(c.Root);                          // 明示 Rebuild なしで無効化されている

        c.Layout(Constraints.LooseW(200, 400), Ctx());
        Assert.Equal(2, c.Builds);
        Assert.True(c.Size.Height > h1);              // 行が増えた
    }

    [Fact]
    public void TrackedBuild_InvalidationIsOneShot_UntilNextBuild()
    {
        var c = new Tracked();
        c.Layout(Constraints.LooseW(200, 400), Ctx());
        c.Count.Value = 2;
        c.Count.Value = 3;                            // 2 回目の変化は不活性な購読 (通知後は依存なし)
        Assert.Equal(1, c.Builds);                    // Build はまだ 1 回のまま

        c.Layout(Constraints.LooseW(200, 400), Ctx());
        Assert.Equal(2, c.Builds);                    // 次のレイアウトで 1 回だけ再 Build (最新値 3 を読む)

        c.Count.Value = 4;                            // 再 Build で購読が張り直されている
        Assert.Null(c.Root);
    }

    [Fact]
    public void ManualMode_DoesNotAutoRebuild()
    {
        var c = new Tracked(track: false);            // opt-out: 手動 Rebuild のみ (性能制御)
        c.Layout(Constraints.LooseW(200, 400), Ctx());
        c.Count.Value = 5;
        Assert.NotNull(c.Root);                       // 自動では無効化されない
        c.Layout(Constraints.LooseW(200, 400), Ctx());
        Assert.Equal(1, c.Builds);
    }
}
