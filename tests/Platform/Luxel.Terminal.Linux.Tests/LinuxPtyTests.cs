using System.Runtime.InteropServices;
using System.Text;
using Luxel.Terminal.Linux;
using Luxel.Terminal.Session;

namespace Luxel.Terminal.Linux.Tests;

public sealed class LinuxPtyTests
{
    [SkippableFact]
    public async Task InteractiveShellSupportsReadWriteAndExit()
    {
        SkipUnlessSupported();
        await using var pty = new LinuxPty();
        await pty.StartAsync(ShellOptions());

        await pty.WriteAsync(Encoding.UTF8.GetBytes("printf 'LUXEL:%s\\n' \"$TERM\"\nexit 7\n"));
        string output = await ReadUntilAsync(pty, "LUXEL:xterm-256color");
        TerminalExitStatus status = await pty.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("LUXEL:xterm-256color", output);
        Assert.Equal(7, status.ExitCode);
        Assert.False(status.Terminated);
    }

    [SkippableFact]
    public async Task ResizeUpdatesTerminalWindowSize()
    {
        SkipUnlessSupported();
        await using var pty = new LinuxPty();
        await pty.StartAsync(ShellOptions(columns: 80, rows: 24));

        await pty.ResizeAsync(132, 43);
        await pty.WriteAsync(Encoding.UTF8.GetBytes("stty size; printf 'SIZE-DONE\\n'; exit\n"));
        string output = await ReadUntilAsync(pty, "\r\nSIZE-DONE\r\n");

        Assert.Contains("43 132", output);
        Assert.Equal(0, (await pty.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5))).ExitCode);
    }

    [SkippableFact]
    public async Task IdleReadHonorsCancellation()
    {
        SkipUnlessSupported();
        await using var pty = new LinuxPty();
        await pty.StartAsync(new TerminalLaunchOptions { FileName = "/bin/cat" });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await pty.ReadAsync(new byte[32], cancellation.Token));
    }

    [SkippableFact]
    public async Task CloseTerminatesProcessGroupAndWaitCancellationDoesNotLoseExit()
    {
        SkipUnlessSupported();
        await using var pty = new LinuxPty();
        await pty.StartAsync(ShellOptions());
        await pty.WriteAsync(Encoding.UTF8.GetBytes("sleep 30 & printf 'CHILD-%s-END\\n' $!\n"));
        string output = await ReadUntilAsync(pty, "-END\r\n");
        int childPid = ParsePid(output);

        using (var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pty.WaitForExitAsync(cancellation.Token));

        await pty.CloseAsync(TerminalCloseMode.TerminateTree, TimeSpan.FromSeconds(2));
        TerminalExitStatus status = await pty.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(status.Terminated);
        await AssertProcessGoneAsync(childPid);
    }

    private static TerminalLaunchOptions ShellOptions(int columns = 80, int rows = 24) => new()
    {
        FileName = "/bin/sh",
        Arguments = ["-i"],
        Columns = columns,
        Rows = rows,
        Environment = new Dictionary<string, string?> { ["TERM"] = "xterm-256color", ["PS1"] = "" }
    };

    private static async Task<string> ReadUntilAsync(LinuxPty pty, string marker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] buffer = new byte[4096];
        var output = new StringBuilder();
        while (!output.ToString().Contains(marker, StringComparison.Ordinal))
        {
            int count = await pty.ReadAsync(buffer, timeout.Token);
            if (count == 0) break;
            output.Append(Encoding.UTF8.GetString(buffer, 0, count));
        }
        return output.ToString();
    }

    private static int ParsePid(string text)
    {
        System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(text, @"CHILD-(\d+)-END");
        Assert.True(match.Success, $"Child PID was not found in PTY output: {text}");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task AssertProcessGoneAsync(int pid)
    {
        string path = $"/proc/{pid}";
        for (int i = 0; i < 50 && Directory.Exists(path); i++) await Task.Delay(20);
        Assert.False(Directory.Exists(path), $"Descendant process {pid} was not cleaned up.");
    }

    private static void SkipUnlessSupported()
        => Skip.IfNot(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Linux PTY integration tests require Linux x64.");
}
