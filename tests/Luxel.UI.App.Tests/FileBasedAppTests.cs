using System.Diagnostics;

namespace Luxel.UI.App.Tests;

public sealed class FileBasedAppTests
{
    [Fact]
    public void DotnetRunFile_WorksFromRepositoryAndForeignWorkingDirectory()
    {
        RequireLinuxDisplay();
        string root = FindRepositoryRoot();
        string app = Path.Combine(root, "samples", "FileBasedApps", "HelloLuxel.Linux.cs");

        RunFile(app, root);
        string foreign = Path.Combine(Path.GetTempPath(), $"luxel-file-app-{Guid.NewGuid():N}");
        Directory.CreateDirectory(foreign);
        try { RunFile(app, foreign); }
        finally { Directory.Delete(foreign, recursive: true); }
    }

    private static void RunFile(string app, string workingDirectory)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--file");
        start.ArgumentList.Add(app);
        start.ArgumentList.Add("--no-launch-profile");
        start.Environment["LUXEL_RUN_FRAMES"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start dotnet run --file.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "dotnet run --file timed out.");
        Assert.True(process.ExitCode == 0,
            $"dotnet run --file failed from '{workingDirectory}' (exit {process.ExitCode}).\nstdout:\n{stdout}\nstderr:\n{stderr}");
        Assert.Contains("Luxel UI loop stopped after 1 rendered frame(s).", stdout);
    }

    private static string FindRepositoryRoot()
    {
        for (string? directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
            if (File.Exists(Path.Combine(directory, "Luxel.slnx"))) return directory;
        throw new DirectoryNotFoundException("Could not find Luxel.slnx above the test output directory.");
    }

    private static void RequireLinuxDisplay()
    {
        Assert.True(OperatingSystem.IsLinux(), "This smoke test requires Linux/X11.");
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")),
            "This smoke test requires an X11 display (for example DISPLAY=:99 from eng/desktop/start.sh or xvfb-run). ");
    }
}
