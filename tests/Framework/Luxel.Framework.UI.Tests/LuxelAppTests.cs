using Luxel.Controls;
using Luxel.UI;
using Luxel.Framework.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Framework.UI.Tests;

public sealed class LuxelAppTests
{
    [Fact]
    public void Options_DefaultsAreInteractiveAndValid()
    {
        var options = new LuxelAppOptions();
        LuxelApp.ValidateOptions(options);
        Assert.Equal("Luxel", options.Title);
        Assert.Equal("app", options.UiName);
        Assert.True(options.Width > 0);
        Assert.True(options.Height > 0);
        Assert.Equal(LuxelWindowBackend.Auto, options.WindowBackend);
        Assert.Equal(LuxelGraphicsBackend.Auto, options.GraphicsBackend);
        Assert.Equal(OperatingSystem.IsWindows() ? LuxelWindowBackend.Win32 : LuxelWindowBackend.SilkX11,
            LuxelApp.ResolveWindowBackend(options.WindowBackend));
        Assert.Equal(OperatingSystem.IsWindows() ? LuxelGraphicsBackend.Direct3D12 : LuxelGraphicsBackend.Vulkan,
            LuxelApp.ResolveGraphicsBackend(options.GraphicsBackend));
        Assert.Equal(LuxelGraphicsBackend.WebGpu, Enum.Parse<LuxelGraphicsBackend>("WebGpu"));
        Assert.Null(options.RunFrames);
        Assert.Null(options.RunDuration);
        Assert.True(options.EnableValidation);
    }

    [Fact]
    public void Options_AllowExplicitWebGpuWithNativeWindowBackend()
    {
        LuxelApp.ValidateOptions(new LuxelAppOptions
        {
            WindowBackend = OperatingSystem.IsWindows() ? LuxelWindowBackend.Win32 : LuxelWindowBackend.SilkX11,
            GraphicsBackend = LuxelGraphicsBackend.WebGpu,
        });
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
    public void Options_RejectInvalidRunDurationAndUiName()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LuxelApp.ValidateOptions(
            new LuxelAppOptions { RunDuration = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => LuxelApp.ValidateOptions(
            new LuxelAppOptions { UiName = " " }));
    }

    [Fact]
    public void Assets_ContainCompleteShaderCacheAndLicensedFont()
    {
        LuxelApp.ValidateAssets(AppContext.BaseDirectory, requireBundledFont: true);

        string shaderDirectory = Path.Combine(AppContext.BaseDirectory, "shaders");
        string[] rasterShaders = Directory.GetFiles(shaderDirectory, "raster2d_*.*")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()!;
        Assert.Equal(
        [
            "raster2d_bin.dxil",
            "raster2d_bin.spv",
            "raster2d_bin.wgsl",
            "raster2d_bounds.dxil",
            "raster2d_bounds.spv",
            "raster2d_bounds.wgsl",
            "raster2d_fine.dxil",
            "raster2d_fine.spv",
            "raster2d_fine.wgsl",
        ], rasterShaders);
        Assert.Equal(22, Directory.GetFiles(shaderDirectory, "*.wgsl").Length);
        Assert.True(File.Exists(Path.Combine(shaderDirectory, "webgpu-shaders.json")));
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
    public void Windows_backends_fail_clearly_outside_Windows()
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.Throws<PlatformNotSupportedException>(
            () => Luxel.Platform.Windows.Win32WindowBackend.Create());
        Assert.Throws<PlatformNotSupportedException>(
            () => Luxel.Graphics.DirectX12.D3D12Backend.Create());
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
                Title = "Luxel.Framework.UI smoke",
                Width = 200,
                Height = 120,
                RunFrames = 1,
                EnableValidation = true,
            });
    }

    [Fact]
    public void LinuxX11_StaticUiPresentsOnlyTheInvalidatedFrame()
    {
        RequireLinuxDisplay();
        var presented = new List<bool>();
        LuxelAppBuilder builder = LuxelApp.CreateBuilder();
        builder.Options.Title = "Luxel.Framework.UI invalidation smoke";
        builder.Options.Width = 200;
        builder.Options.Height = 120;
        builder.Options.RunFrames = 3;
        builder.Options.EnableValidation = true;
        builder.OnFrame((runtime, _) => presented.Add(runtime.MainWindow.RenderedThisFrame));
        builder.Build()
            .MapScreen("/", () => Center()[Card(Text("static"))])
            .Run();

        Assert.Equal([true, false, false], presented);
    }

    [Fact]
    public void LinuxX11_WebGpuRendersOneActualUiFrame()
    {
        RequireLinuxDisplay();
        LuxelApp.Run(
            () => Center()[Card(Text("one WebGPU frame"))],
            new LuxelAppOptions
            {
                Title = "Luxel.Framework.UI WebGPU smoke",
                Width = 200,
                Height = 120,
                GraphicsBackend = LuxelGraphicsBackend.WebGpu,
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
