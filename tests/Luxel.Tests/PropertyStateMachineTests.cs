using Luxel.Animation;
using Xunit;

namespace Luxel.Tests;

/// <summary>AS-M2: PropertyStateMachine + TransitionTable — 8 段優先度 / 瞬時 Start /
/// 途中 Goto の連続性 / 動的状態 / 欠落プロパティの base 戻し / 静定ゼロ書き込み / delay / 並走。</summary>
public class PropertyStateMachineTests
{
    private static readonly TransitionSpec Lin1 = new(1f, LinearCurve.Instance);

    // ---- TransitionTable ----

    [Fact]
    public void Resolve_PriorityOrder_AllTiers()
    {
        var t = new TransitionTable()
            .Default(new TransitionSpec(8))
            .On("p", new TransitionSpec(7))
            .From("a", new TransitionSpec(6))
            .From("a", "p", new TransitionSpec(5))
            .To("b", new TransitionSpec(4))
            .To("b", "p", new TransitionSpec(3))
            .Between("a", "b", new TransitionSpec(2))
            .Between("a", "b", "p", new TransitionSpec(1));

        Assert.Equal(1, t.Resolve("a", "b", "p")!.Value.Duration);   // pair+prop
        Assert.Equal(2, t.Resolve("a", "b", "q")!.Value.Duration);   // pair
        Assert.Equal(3, t.Resolve("x", "b", "p")!.Value.Duration);   // to+prop
        Assert.Equal(4, t.Resolve("x", "b", "q")!.Value.Duration);   // to
        Assert.Equal(5, t.Resolve("a", "y", "p")!.Value.Duration);   // from+prop
        Assert.Equal(6, t.Resolve("a", "y", "q")!.Value.Duration);   // from
        Assert.Equal(7, t.Resolve("x", "y", "p")!.Value.Duration);   // prop
        Assert.Equal(8, t.Resolve("x", "y", "q")!.Value.Duration);   // 既定
    }

    [Fact]
    public void Resolve_NoRules_ReturnsNull()
        => Assert.Null(new TransitionTable().Resolve("a", "b", "p"));

    // ---- PropertyStateMachine ----

    private static PropertyStateMachine OnOff(TransitionTable table)
        => new PropertyStateMachine(table)
            .AddState("off", new Dictionary<string, object> { ["t"] = 0f })
            .AddState("on", new Dictionary<string, object> { ["t"] = 1f });

    [Fact]
    public void Start_IsInstant_AndPushesToSink()
    {
        var m = OnOff(new TransitionTable().Default(Lin1));
        float seen = -1;
        m.Bind<float>("t", v => seen = v);
        m.Start("on");
        Assert.Equal(1f, seen);            // Snap で即配信
        Assert.Equal(1f, m.Get<float>("t"));
        Assert.False(m.IsTransitioning);
    }

    [Fact]
    public void Goto_TransitionsOverDuration()
    {
        var clock = new ManualClock();
        var m = OnOff(new TransitionTable().Default(Lin1));
        m.Start("off");
        m.Goto("on", clock);
        Assert.True(m.IsTransitioning);
        clock.Advance(0.5f); m.Tick(clock);
        Assert.Equal(0.5f, m.Get<float>("t"), 3);
        clock.Advance(0.5f); m.Tick(clock);
        Assert.Equal(1f, m.Get<float>("t"));
        Assert.False(m.IsTransitioning);
        Assert.Equal("on", m.Current);
    }

    [Fact]
    public void MidFlight_Goto_StartsFromCurrentValue()
    {
        var clock = new ManualClock();
        var m = OnOff(new TransitionTable().Default(Lin1));
        m.Start("off");
        m.Goto("on", clock);
        clock.Advance(0.5f); m.Tick(clock);     // t = 0.5 まで進んだ
        m.Goto("off", clock);                   // 途中で引き返す — 現在値 0.5 起点
        clock.Advance(0.5f); m.Tick(clock);
        Assert.Equal(0.25f, m.Get<float>("t"), 3);   // 0.5 → 0 の中間 (ジャンプなし)
    }

    [Fact]
    public void PairRule_Beats_ToRule()
    {
        var clock = new ManualClock();
        var table = new TransitionTable()
            .To("on", Lin1)
            .Between("off", "on", new TransitionSpec(0f));   // off→on だけ瞬時
        var m = OnOff(table);
        m.Start("off");
        m.Goto("on", clock);
        Assert.Equal(1f, m.Get<float>("t"));   // pair 0ms が勝つ → 瞬時
        Assert.False(m.IsTransitioning);
    }

    [Fact]
    public void DynamicState_SameName_RetargetsOnNewValues()
    {
        var clock = new ManualClock();
        var m = new PropertyStateMachine(new TransitionTable().Default(Lin1));
        m.Goto("sel", clock, new Dictionary<string, object> { ["y"] = 0f });   // 未 Start → 瞬時
        Assert.Equal(0f, m.Get<float>("y"));
        m.Goto("sel", clock, new Dictionary<string, object> { ["y"] = 100f }); // 同名・別値 → retarget
        clock.Advance(0.5f); m.Tick(clock);
        Assert.Equal(50f, m.Get<float>("y"), 2);
    }

    [Fact]
    public void MissingProp_FallsBackToBaseValue()
    {
        var clock = new ManualClock();
        var m = new PropertyStateMachine(new TransitionTable())   // ルールなし = 全部瞬時
            .AddState("normal", new Dictionary<string, object> { ["bg"] = 0u, ["scale"] = 1f })
            .AddState("hover", new Dictionary<string, object> { ["scale"] = 1.1f });   // bg なし
        m.Start("normal");
        m.Goto("hover", clock);
        Assert.Equal(1.1f, m.Get<float>("scale"));
        Assert.Equal(0u, m.Get<uint>("bg"));      // base のまま
        m.Goto("hover", clock, new Dictionary<string, object> { ["bg"] = 5u });   // 動的で bg 変更
        Assert.Equal(5u, m.Get<uint>("bg"));
        m.Goto("normal", clock);
        Assert.Equal(0u, m.Get<uint>("bg"));      // base へ戻る
    }

    [Fact]
    public void Idle_Tick_DoesNotWriteSink()
    {
        var clock = new ManualClock();
        var m = OnOff(new TransitionTable().Default(Lin1));
        int writes = 0;
        m.Bind<float>("t", _ => writes++);
        m.Start("on");                       // 1 回 (Snap)
        int after = writes;
        clock.Advance(1f); m.Tick(clock);
        clock.Advance(1f); m.Tick(clock);    // 静定中 — 書き込みなし
        Assert.Equal(after, writes);
    }

    [Fact]
    public void Delay_HoldsThenRuns()
    {
        var clock = new ManualClock();
        var m = OnOff(new TransitionTable().Default(new TransitionSpec(1f, LinearCurve.Instance, Delay: 1f)));
        m.Start("off");
        m.Goto("on", clock);
        clock.Advance(0.5f); m.Tick(clock);
        Assert.Equal(0f, m.Get<float>("t"));           // delay 中は保持
        clock.Advance(1.0f); m.Tick(clock);            // t = 1.5 → 遷移の 0.5
        Assert.Equal(0.5f, m.Get<float>("t"), 3);
    }

    [Fact]
    public void PerProp_IndependentDurations_RunInParallel()
    {
        var clock = new ManualClock();
        var table = new TransitionTable()
            .On("a", Lin1)
            .On("b", new TransitionSpec(2f, LinearCurve.Instance));
        var m = new PropertyStateMachine(table)
            .AddState("s0", new Dictionary<string, object> { ["a"] = 0f, ["b"] = 0f })
            .AddState("s1", new Dictionary<string, object> { ["a"] = 10f, ["b"] = 10f });
        m.Start("s0");
        m.Goto("s1", clock);
        clock.Advance(1f); m.Tick(clock);
        Assert.Equal(10f, m.Get<float>("a"));          // a は完了
        Assert.Equal(5f, m.Get<float>("b"), 3);        // b は半分 — 並走
        Assert.True(m.IsTransitioning);
    }

    [Fact]
    public void SameTarget_Goto_DoesNotRestart()
    {
        var clock = new ManualClock();
        var m = OnOff(new TransitionTable().Default(Lin1));
        m.Start("off");
        m.Goto("on", clock);
        clock.Advance(0.9f); m.Tick(clock);
        m.Goto("on", clock);                 // 同一状態・同一値 — 再スタートしない
        clock.Advance(0.1f); m.Tick(clock);
        Assert.Equal(1f, m.Get<float>("t"));
        Assert.False(m.IsTransitioning);
    }

    [Fact]
    public void UnknownType_UsesStepTween()
    {
        var clock = new ManualClock();
        var m = new PropertyStateMachine(new TransitionTable().Default(Lin1));
        m.Goto("a", clock, new Dictionary<string, object> { ["s"] = (object)"left" });
        m.Goto("b", clock, new Dictionary<string, object> { ["s"] = (object)"right" });
        clock.Advance(0.25f); m.Tick(clock);
        Assert.Equal("left", m.Get<object>("s"));      // t<0.5 は from
        clock.Advance(0.5f); m.Tick(clock);
        Assert.Equal("right", m.Get<object>("s"));     // t>=0.5 で to
    }
}
