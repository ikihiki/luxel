using Luxel.UI;
using Luxel.Workbench;
using Xunit;

namespace Luxel.Tests;

/// <summary>WB: CommandRegistry (ADR-0013) — 登録/実行/enablement/キーマップ/メニュー合成。GPU 不要。</summary>
public class CommandRegistryTests
{
    [Fact]
    public void KeyGestures_ParseAndFormat()
    {
        Assert.Equal(new KeyGesture(Key.P, Ctrl: true, Shift: true), KeyGestures.Parse("Ctrl+Shift+P"));
        Assert.Equal(new KeyGesture(Key.F3), KeyGestures.Parse("F3"));
        Assert.Equal(new KeyGesture(Key.D1, Ctrl: true), KeyGestures.Parse("Ctrl+1"));
        Assert.Null(KeyGestures.Parse("Ctrl+Nope"));
        Assert.Equal("Ctrl+Shift+P", KeyGestures.Format(new KeyGesture(Key.P, Ctrl: true, Shift: true)));
        Assert.Equal("Ctrl+1", KeyGestures.Format(new KeyGesture(Key.D1, Ctrl: true)));
    }

    [Fact]
    public void Run_ExecutesOnlyWhenEnabled()
    {
        var reg = new CommandRegistry();
        int ran = 0;
        bool enabled = false;
        reg.Register("t.run", "実行", () => ran++, enabled: () => enabled);

        Assert.False(reg.Run("t.run"));
        Assert.Equal(0, ran);
        enabled = true;
        Assert.True(reg.Run("t.run"));
        Assert.Equal(1, ran);
        Assert.False(reg.Run("nope"));
    }

    [Fact]
    public void HandleKey_DispatchesByGesture_ContributionFirst()
    {
        var reg = new CommandRegistry();
        string log = "";
        reg.Register("t.save", "保存", () => log += "base;", key: "Ctrl+S");

        Assert.True(reg.HandleKey(Key.S, KeyModifiers.Ctrl));
        Assert.Equal("base;", log);
        Assert.False(reg.HandleKey(Key.S, KeyModifiers.None));   // 修飾不一致

        // アクティブ doc の寄与が同じキーを持つ → 寄与優先
        var contrib = new[] { new CommandContribution(
            new Command("doc.save", "doc 保存", () => log += "doc;", Gesture: KeyGestures.Parse("Ctrl+S"))) };
        Assert.True(reg.HandleKey(Key.S, KeyModifiers.Ctrl, contrib));
        Assert.Equal("base;doc;", log);
    }

    [Fact]
    public void BuildMenu_PathsBecomeHierarchy_OrderedByOrderThenSeq()
    {
        var reg = new CommandRegistry();
        reg.Register("f.exit", "終了", () => { }, menuPath: "File/終了", order: 99);
        reg.Register("f.save", "保存", () => { }, menuPath: "File/保存", order: 0);
        reg.Register("e.find", "検索", () => { }, menuPath: "Edit/検索");
        reg.Register("f.recent1", "最近 1", () => { }, menuPath: "File/最近使った/one", order: 50);

        var menu = reg.BuildMenu();

        Assert.Equal(["File", "Edit"], menu.Select(n => n.Label).ToArray());
        MenuNode file = menu[0];
        Assert.Equal(["保存", "最近使った", "終了"], file.Children.Select(n => n.Label).ToArray());
        Assert.NotNull(file.Children[0].Command);          // 葉 = コマンド
        Assert.Null(file.Children[1].Command);             // フォルダ
        Assert.Equal("one", file.Children[1].Children[0].Label);
    }

    [Fact]
    public void BuildMenu_MergesActiveDocContributions()
    {
        var reg = new CommandRegistry();
        reg.Register("f.save", "保存", () => { }, menuPath: "File/保存");
        var contrib = new[] { new CommandContribution(
            new Command("g.layout", "整列", () => { }), MenuPath: "Graph/整列") };

        var menu = reg.BuildMenu(contrib);

        Assert.Equal(["File", "Graph"], menu.Select(n => n.Label).ToArray());
        Assert.Equal("整列", menu[1].Children[0].Label);

        // 寄与なしなら Graph は出ない (アクティブ doc 切替で章が消える)
        Assert.Equal(["File"], reg.BuildMenu().Select(n => n.Label).ToArray());
    }

    [Fact]
    public void ToolbarAndPalette_IncludeContributions()
    {
        var reg = new CommandRegistry();
        reg.Register("t.a", "Aaa", () => { }, toolbar: true, order: 1);
        reg.Register("t.b", "Bbb", () => { });
        var contrib = new[] { new CommandContribution(new Command("t.c", "Ccc", () => { }), Toolbar: true, Order: 0) };

        Assert.Equal(["Ccc", "Aaa"], reg.ToolbarCommands(contrib).Select(c => c.Title).ToArray());
        Assert.Equal(["Aaa", "Bbb", "Ccc"], reg.PaletteCommands(contrib).Select(c => c.Title).ToArray());
        Assert.Equal(["Aaa", "Bbb"], reg.PaletteCommands().Select(c => c.Title).ToArray());
    }

    [Fact]
    public void Version_BumpsOnRegister()
    {
        var reg = new CommandRegistry();
        int v = reg.Version.Value;
        reg.Register("x", "X", () => { });
        Assert.True(reg.Version.Value > v);
    }
}
