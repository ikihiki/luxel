using System.Diagnostics;
using Luxel.Platform.Abstraction;

namespace Luxel.Platform.Silk.Tests;

public sealed class SilkWindowBackendTests
{
    [Fact]
    public void MissingDisplayHasActionableError()
    {
        string? display = Environment.GetEnvironmentVariable("DISPLAY");
        try
        {
            Environment.SetEnvironmentVariable("DISPLAY", null);
            PlatformNotSupportedException error = Assert.Throws<PlatformNotSupportedException>(SilkWindowBackend.Create);
            Assert.Contains("DISPLAY", error.Message, StringComparison.Ordinal);
            Assert.Contains("X11", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", display);
        }
    }

    [Fact]
    public void X11LifecycleMultiWindowAndNormalizedInput()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")),
            "This test requires DISPLAY=:99 from eng/desktop/start.sh or an xvfb-run display.");

        using SilkWindowBackend backend = SilkWindowBackend.Create();
        using var windows = new WindowSystem(backend);
        string suffix = Guid.NewGuid().ToString("N");
        Window first = windows.CreateWindow(new WindowDesc($"Luxel Silk A {suffix}", 320, 220)
        {
            X = 80,
            Y = 90,
            Visible = false,
        });
        Window second = windows.CreateWindow(new WindowDesc($"Luxel Silk B {suffix}", 280, 180)
        {
            X = 440,
            Y = 90,
        });

        Assert.Equal("Silk.NET GLFW/X11", windows.Name);
        Assert.NotEqual(0, first.Handle);
        Assert.NotEqual(0, second.Handle);
        Assert.False(first.IsVisible);
        Assert.True(second.IsVisible);
        Assert.Equal(2, windows.Windows.Count);

        var resized = new List<(int Width, int Height)>();
        var moved = new List<(int X, int Y)>();
        var focusChanged = new List<bool>();
        first.Resized += (width, height) => resized.Add((width, height));
        first.Moved += (x, y) => moved.Add((x, y));
        first.FocusChanged += focusChanged.Add;

        first.Show();
        PumpUntil(windows, () => first.IsVisible);
        first.SetTitle($"Luxel Silk A renamed {suffix}");
        resized.Clear();
        moved.Clear();
        first.SetBounds(120, 130, 360, 240);
        PumpUntil(windows, () => first.Width == 360 && first.Height == 240 && resized.Count > 0 && moved.Count > 0);
        Assert.Equal((360, 240), resized[^1]);
        Assert.InRange(first.X, 115, 125);
        Assert.InRange(first.Y, 125, 135);
        Assert.InRange(moved[^1].X, 115, 125);
        Assert.InRange(moved[^1].Y, 125, 135);

        first.Hide();
        PumpUntil(windows, () => !first.IsVisible);
        first.Show();
        PumpUntil(windows, () => first.IsVisible);
        focusChanged.Clear();
        first.Focus();
        PumpUntil(windows, () => first.IsFocused && focusChanged.Contains(true));

        CursorKind cursor = CursorKind.Arrow;
        first.CursorQuery = () => cursor;
        foreach (CursorKind kind in Enum.GetValues<CursorKind>())
        {
            cursor = kind;
            windows.Pump();
        }

        var pointerMoves = new List<WindowPointerEvent>();
        var pointerDown = new List<WindowPointerEvent>();
        var pointerUp = new List<WindowPointerEvent>();
        var wheels = new List<WindowWheelEvent>();
        var keyDown = new List<WindowKeyEvent>();
        var keyUp = new List<WindowKeyEvent>();
        var text = new List<string>();
        first.PointerMoved += pointerMoves.Add;
        first.PointerDown += pointerDown.Add;
        first.PointerUp += pointerUp.Add;
        first.Wheel += wheels.Add;
        first.KeyDown += keyDown.Add;
        first.KeyUp += keyUp.Add;
        first.TextInput += text.Add;

        // Force a position transition even when a prior failed/repeated run left the X pointer at the target.
        RunXdotool("mousemove", "--window", first.Handle.ToString(), "5", "5");
        PumpUntil(windows, () => pointerMoves.Count > 0);
        pointerMoves.Clear();
        RunXdotool("mousemove", "--window", first.Handle.ToString(), "40", "50");
        PumpUntil(windows, () => pointerMoves.Count > 0);
        Assert.InRange(pointerMoves[^1].X, 35, 45);
        Assert.InRange(pointerMoves[^1].Y, 45, 55);

        RunXdotool("click", "--window", first.Handle.ToString(), "1");
        PumpUntil(windows, () => pointerDown.Any(e => e.Button == WindowPointerButton.Left) &&
                                 pointerUp.Any(e => e.Button == WindowPointerButton.Left));

        RunXdotool("click", "--window", first.Handle.ToString(), "4");
        PumpUntil(windows, () => wheels.Count > 0);
        Assert.True(wheels[^1].Delta > 0);

        RunXdotool("keydown", "--window", first.Handle.ToString(), "ctrl");
        RunXdotool("key", "--window", first.Handle.ToString(), "a");
        RunXdotool("keyup", "--window", first.Handle.ToString(), "ctrl");
        PumpUntil(windows, () => keyDown.Any(e => e.Key == WindowKey.A) && keyUp.Any(e => e.Key == WindowKey.A));
        Assert.Contains(keyDown, e => e.Key == WindowKey.A && (e.Modifiers & WindowKeyModifiers.Control) != 0);

        // XTest/xdotool cannot portably synthesize non-BMP text across X keyboard maps.
        // Exercise a reliable committed character here; astral Unicode is covered by the pure helper test.
        RunXdotool("key", "--window", first.Handle.ToString(), "b");
        PumpUntil(windows, () => text.Contains("b"));

        int firstClosed = 0;
        int secondClosed = 0;
        first.Closed += () => firstClosed++;
        second.Closed += () => secondClosed++;
        first.Close();
        first.Close();
        Assert.Equal(1, firstClosed);
        Assert.True(windows.Pump());
        Assert.Single(windows.Windows);

        second.Close();
        Assert.Equal(1, secondClosed);
        Assert.False(windows.Pump());
        Assert.Empty(windows.Windows);

        Exception? wrongThread = null;
        using var done = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try { backend.Pump(); }
            catch (Exception ex) { wrongThread = ex; }
            finally { done.Set(); }
        });
        thread.Start();
        Assert.True(done.Wait(TimeSpan.FromSeconds(2)));
        thread.Join();
        InvalidOperationException threadError = Assert.IsType<InvalidOperationException>(wrongThread);
        Assert.Contains("creation thread", threadError.Message, StringComparison.Ordinal);
    }

    private static void PumpUntil(WindowSystem windows, Func<bool> condition, int timeoutMilliseconds = 3000)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            windows.Pump();
            if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
                throw new TimeoutException($"Condition was not reached within {timeoutMilliseconds} ms.");
            Thread.Sleep(5);
        }
    }

    private static void RunXdotool(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("xdotool")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start xdotool.");
        Assert.True(process.WaitForExit(3000), $"xdotool timed out: {string.Join(' ', arguments)}");
        Assert.Equal(0, process.ExitCode);
    }
}
