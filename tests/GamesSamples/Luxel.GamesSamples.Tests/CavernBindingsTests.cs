using LuxelCavern.Core;
using Luxel.Input;
using Luxel.Settings;

namespace Luxel.Tests;

/// <summary>
/// キーバインド再割当 <see cref="CavernBindings"/>: Apply がプライマリ + 矢印セカンダリを入力アクションへ反映 /
/// Rebind がプライマリを差し替え / 変更が AutoSave で永続化され再読込で復元。
/// </summary>
public class CavernBindingsTests
{
    private static CavernSettings NewSettings() => new(new InMemoryFileStore());

    [Fact]
    public void Apply_SetsPrimaryPlusArrowSecondary()
    {
        CavernSettings s = NewSettings();   // 既定 A / D / Space
        var move = new Axis1DAction("move");
        var jump = new ButtonAction("jump");
        CavernBindings.Apply(move, jump, s);

        Assert.Equal(2, move.ButtonPairs.Count);
        Assert.Equal((KeyCode.D, KeyCode.A), move.ButtonPairs[0]);      // プライマリ (右, 左)
        Assert.Equal((KeyCode.Right, KeyCode.Left), move.ButtonPairs[1]); // 固定セカンダリ (矢印)
        Assert.Equal(new[] { KeyCode.Space, KeyCode.Up }, jump.Keys);
    }

    [Fact]
    public void Rebind_ReplacesPrimary_KeepsArrowSecondary()
    {
        CavernSettings s = NewSettings();
        CavernBindings.Rebind(s, CavernBind.Jump, KeyCode.J);
        Assert.Equal(KeyCode.J, s.BindJump.Value);

        var move = new Axis1DAction("move");
        var jump = new ButtonAction("jump");
        CavernBindings.Apply(move, jump, s);
        Assert.Contains(KeyCode.J, jump.Keys);
        Assert.DoesNotContain(KeyCode.Space, jump.Keys);   // 旧プライマリは外れる
        Assert.Contains(KeyCode.Up, jump.Keys);            // 矢印セカンダリは残る
    }

    [Fact]
    public void Rebind_Persists_AndReloads()
    {
        var files = new InMemoryFileStore();
        CavernBindings.Rebind(new CavernSettings(files), CavernBind.Left, KeyCode.Q);   // AutoSave

        var reloaded = new CavernSettings(files);
        Assert.Equal(KeyCode.Q, reloaded.BindLeft.Value);
        Assert.Equal(KeyCode.D, reloaded.BindRight.Value);   // 変更していないバインドは既定のまま
    }

    [Fact]
    public void CurrentAndLabel_MatchBind()
    {
        CavernSettings s = NewSettings();
        Assert.Equal(KeyCode.A, CavernBindings.Current(s, CavernBind.Left));
        Assert.Equal(KeyCode.Space, CavernBindings.Current(s, CavernBind.Jump));
        Assert.Equal("ジャンプ", CavernBindings.Label(CavernBind.Jump));
    }
}
