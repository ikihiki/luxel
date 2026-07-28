using System.Text;
using Luxel.Terminal.Session;
using Luxel.Terminal.Windows;

namespace Luxel.Terminal.Windows.Tests;

public sealed class WindowsConPtyTests
{
    [Fact]
    public async Task StartAsync_OnNonWindows_IsExplicitlyRejected()
    {
        if (OperatingSystem.IsWindows()) return;
        await using var pty = new WindowsConPty();
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => pty.StartAsync(new TerminalLaunchOptions { FileName = "ignored" }));
    }

    [Fact]
    public async Task ResizeAsync_RejectsInvalidDimensionsBeforeNativeCall()
    {
        await using var pty = new WindowsConPty();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await pty.ResizeAsync(0, 24));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await pty.ResizeAsync(80, 0));
    }

    [Fact]
    public async Task ResizeAsync_HonorsPreCanceledToken()
    {
        await using var pty = new WindowsConPty();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await pty.ResizeAsync(80, 24, new CancellationToken(canceled: true)));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task WindowsOnly_CanReadOutputAndWaitForExit()
    {
        // This integration test is intentionally guarded so the net10.0 project remains testable on Linux CI.
        if (!OperatingSystem.IsWindows()) return;

        await using var pty = new WindowsConPty();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await pty.StartAsync(new TerminalLaunchOptions
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = ["/d", "/s", "/c", "echo conpty-ready"],
            Columns = 80,
            Rows = 24
        }, timeout.Token);

        var output = new MemoryStream();
        byte[] buffer = new byte[1024];
        while (!Encoding.UTF8.GetString(output.ToArray()).Contains("conpty-ready", StringComparison.OrdinalIgnoreCase))
        {
            int read = await pty.ReadAsync(buffer, timeout.Token);
            Assert.NotEqual(0, read);
            output.Write(buffer, 0, read);
        }

        TerminalExitStatus status = await pty.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, status.ExitCode);
        Assert.False(status.Terminated);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public async Task WindowsOnly_TerminateTreeCompletesWaiter()
    {
        if (!OperatingSystem.IsWindows()) return;

        await using var pty = new WindowsConPty();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await pty.StartAsync(new TerminalLaunchOptions
        {
            FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
            Arguments = ["/d", "/s", "/c", "ping -t 127.0.0.1"],
            Columns = 80,
            Rows = 24
        }, timeout.Token);

        Task<TerminalExitStatus> waiter = pty.WaitForExitAsync(timeout.Token);
        await pty.CloseAsync(TerminalCloseMode.TerminateTree, TimeSpan.Zero, timeout.Token);
        TerminalExitStatus status = await waiter;
        Assert.True(status.Terminated);
    }
}
