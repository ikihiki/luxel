using Luxel.UI;
using Xunit;

namespace Luxel.Tests;

/// <summary>DW4: ControlApiRegistry — ジェネレーターが [UiComponent] から /// コメントごと焼き込む。</summary>
public class ControlApiTests
{
    static ControlApiTests()
    {
        // module initializer (ControlApiRegistration_*) を確実に走らせる
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(Luxel.Controls.Kit).Module.ModuleHandle);
    }

    [Fact]
    public void Button_Registered_WithDocsAndKinds()
    {
        ControlApi? api = ControlApiRegistry.Find("Button");
        Assert.NotNull(api);
        Assert.NotEqual("", api!.Summary);   // クラス /// summary が入る

        ApiMember bg = api.Members.First(m => m.Name == "Background" && m.Kind == "param");
        Assert.True(bg.Stateable);
        Assert.False(bg.Inherited);
        Assert.Contains("背景", bg.Description);   // /// コメント由来の説明

        ApiMember ev = api.Members.First(m => m.Name == "OnClick" && m.Kind == "event");
        Assert.Contains("Button", ev.Type);        // UiEvent<Button>
        Assert.NotEqual("", ev.Description);

        Assert.Contains(api.Members, m => m.Inherited);   // Widget 共通パラメータも (Inherited 印付き)
    }

    [Fact]
    public void TreeView_CtorParams_WithShortTypes()
    {
        ControlApi? api = ControlApiRegistry.Find("TreeView");
        Assert.NotNull(api);
        ApiMember roots = api!.Members.First(m => m.Kind == "ctor" && m.Name == "roots");
        Assert.DoesNotContain("global::", roots.Type);    // 表示用の短い型名
        Assert.Contains(api.Members, m => m.Kind == "param" && m.Name == "Filter" && !m.Inherited);
    }
}
