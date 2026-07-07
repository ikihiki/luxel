using Luxel.DevTools;

namespace Luxel.Tests;

/// <summary>
/// <see cref="DevToolsOptions.Parse"/> (Q05 E: WithDevTools のコマンドライン解釈) の GPU 不要テスト。
/// </summary>
public class DevToolsOptionsTests
{
    [Fact]
    public void NoFlags_IsDisabled()
    {
        var o = DevToolsOptions.Parse(new[] { "vk", "--frames", "30" });
        Assert.True(o.IsDisabled);
        Assert.Null(o.BrowserPort);
        Assert.False(o.Native);
    }

    [Fact]
    public void Devtools_BareEnablesBrowserAutoPort()
    {
        var o = DevToolsOptions.Parse(new[] { "--devtools" });
        Assert.False(o.IsDisabled);
        Assert.Equal(0, o.BrowserPort);   // 0 = 自動割当
        Assert.False(o.Native);
    }

    [Fact]
    public void Devtools_WithExplicitPort()
    {
        Assert.Equal(8080, DevToolsOptions.Parse(new[] { "--devtools", "8080" }).BrowserPort);
        Assert.Equal(9001, DevToolsOptions.Parse(new[] { "--devtools-port", "9001" }).BrowserPort);
    }

    [Fact]
    public void DevtoolsNative_NeedsFactory_AndCombines()
    {
        // --devtools-native は factory を要求し、ブラウザと併用できる
        bool called = false;
        Func<Luxel.GpuDevice> factory = () => { called = true; return null!; };
        var o = DevToolsOptions.Parse(new[] { "--devtools", "7000", "--devtools-native" }, factory);
        Assert.Equal(7000, o.BrowserPort);
        Assert.True(o.Native);
        Assert.NotNull(o.NativeDeviceFactory);
        Assert.False(called);   // factory は Parse 時には呼ばれない (起動時に島スレッドで呼ぶ)
    }

    [Fact]
    public void Native_WithoutFactory_LeavesFactoryNull()
    {
        var o = DevToolsOptions.Parse(new[] { "--devtools-native" });
        Assert.True(o.Native);
        Assert.Null(o.NativeDeviceFactory);   // 起動時に警告を出して内蔵版はスキップされる
    }
}
