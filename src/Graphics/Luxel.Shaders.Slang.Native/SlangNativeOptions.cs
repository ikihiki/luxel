using System.Runtime.InteropServices;
using Luxel.Shaders;

namespace Luxel.Shaders.Slang.Native;

public sealed class SlangNativeOptions
{
    public string? SlangcPath { get; init; }
    public string? ToolRoot { get; init; }
    public string? TemporaryRoot { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; } = new Dictionary<string, string?>();
}

public static class SlangToolDiscovery
{
    public static string GetRuntimeIdentifier()
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        };
        if (OperatingSystem.IsWindows()) return $"win-{architecture}";
        if (OperatingSystem.IsLinux()) return $"linux-{architecture}";
        if (OperatingSystem.IsMacOS()) return $"osx-{architecture}";
        return $"unknown-{architecture}";
    }

    public static string Resolve(SlangNativeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SlangcPath))
            return RequireFile(options.SlangcPath);

        string? environmentPath = Environment.GetEnvironmentVariable("SLANGC_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath))
            return RequireFile(environmentPath);

        string executable = OperatingSystem.IsWindows() ? "slangc.exe" : "slangc";
        string relative = Path.Combine("tools", "slang", SlangToolchain.Version, GetRuntimeIdentifier(), "bin", executable);
        if (!string.IsNullOrWhiteSpace(options.ToolRoot))
            return RequireFile(Path.Combine(options.ToolRoot, SlangToolchain.Version, GetRuntimeIdentifier(), "bin", executable));

        foreach (string start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, relative);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            $"Pinned slangc {SlangToolchain.Version} was not found. Set SlangcPath/SLANGC_PATH or install it at '{relative}'.");
    }

    private static string RequireFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException($"slangc was not found at '{fullPath}'.", fullPath);
    }
}
