using Luxel.Controls;
using Luxel.UI;
using Luxel.UI.App;
using static Luxel.Controls.Kit;

namespace Luxel.UI.App.Tests;

public sealed class LuxelAppTests
{
    [Fact]
    public void Options_DefaultsAreInteractiveAndValid()
    {
        var options = new LuxelAppOptions();
        LuxelApp.ValidateOptions(options);
        Assert.Equal("Luxel", options.Title);
        Assert.True(options.Width > 0);
        Assert.True(options.Height > 0);
        Assert.Null(options.RunFrames);
        Assert.True(options.EnableValidation);
    }

    [Theory]
    [InlineData(0, 640, null)]
    [InlineData(960, 0, null)]
    [InlineData(960, 640, 0)]
    [InlineData(960, 640, -1)]
    public void Options_RejectInvalidSizesAndFrameLimits(int width, int height, int? frames)
    {
        var options = new LuxelAppOptions { Width = width, Height = height, RunFrames = frames };
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => LuxelApp.ValidateOptions(options));
    }

    [Fact]
    public void Assets_ContainExactlyRasterizerSpirvAndLicensedFont()
    {
        LuxelApp.ValidateAssets(AppContext.BaseDirectory, requireBundledFont: true);

        string shaderDirectory = Path.Combine(AppContext.BaseDirectory, "shaders");
        string[] shaders = Directory.GetFiles(shaderDirectory, "*.spv")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;
        Assert.Equal(
        [
            "raster2d_bin.spv",
            "raster2d_bounds.spv",
            "raster2d_fine.spv",
        ], shaders);
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "BIZUDGothic-Regular.ttf")));
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "assets", "fonts", "OFL.txt")));
    }

    [Fact]
    public void Assets_ReportResolvedPathAndRemediationWhenMissing()
    {
        string empty = Path.Combine(Path.GetTempPath(), $"luxel-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        try
        {
            FileNotFoundException error = Assert.Throws<FileNotFoundException>(
                () => LuxelApp.ValidateAssets(empty, requireBundledFont: true));
            Assert.Contains("shaders/raster2d_bounds.spv", error.Message, StringComparison.Ordinal);
            Assert.Contains(empty, error.Message, StringComparison.Ordinal);
            Assert.Contains("Build or publish", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public void InputMapping_CoversPortableKeysButtonsAndModifiers()
    {
        Assert.Equal(Key.J, LuxelInput.MapKey(WindowKey.J));
        Assert.Equal(Key.R, LuxelInput.MapKey(WindowKey.R));
        Assert.Equal(Key.D7, LuxelInput.MapKey(WindowKey.D7));
        Assert.Equal(Key.F12, LuxelInput.MapKey(WindowKey.F12));
        Assert.Equal(Key.None, LuxelInput.MapKey(WindowKey.Insert));

        KeyModifiers modifiers = LuxelInput.MapModifiers(
            WindowKeyModifiers.Control | WindowKeyModifiers.Shift | WindowKeyModifiers.Alt | WindowKeyModifiers.Meta);
        Assert.Equal(KeyModifiers.Ctrl | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta, modifiers);
        Assert.True(LuxelInput.TryMapButton(WindowPointerButton.Right, out PointerButton right));
        Assert.Equal(PointerButton.Right, right);
        Assert.False(LuxelInput.TryMapButton(WindowPointerButton.X1, out _));
    }

    [Fact]
    public void LinuxX11_RendersOneActualUiFrame()
    {
        RequireLinuxDisplay();
        LuxelApp.Run(
            () => Center()[Card(Text("one frame"))],
            new LuxelAppOptions
            {
                Title = "Luxel.UI.App smoke",
                Width = 200,
                Height = 120,
                RunFrames = 1,
                EnableValidation = true,
            });
    }

    private static void RequireLinuxDisplay()
    {
        Assert.True(OperatingSystem.IsLinux(), "This smoke test requires Linux/X11.");
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")),
            "This smoke test requires an X11 display (for example DISPLAY=:99 from eng/desktop/start.sh or xvfb-run). ");
    }
}
