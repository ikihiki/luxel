using System.Diagnostics;
using Luxel.UI;

namespace Luxel.Gallery;

public sealed record SampleVerificationResult(string BundleId, string Root, string Project, string Stdout, string Stderr);

/// <summary>Materializes a bundle into an external temp directory, then restores, builds, and runs its smoke contract.</summary>
public static class SampleBundleVerifier
{
    public static async Task<SampleVerificationResult> VerifyAsync(string repositoryRoot, string bundleId, bool runSmoke = true, CancellationToken cancellationToken = default)
    {
        SampleBundleInfo bundle = SampleBundleRegistry.Find(bundleId)
            ?? throw new InvalidOperationException($"Unknown sample bundle: {bundleId}");
        string platform = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "macOS" : "Unknown";
        if (bundle.Platforms is { Count: > 0 } && !bundle.Platforms.Contains(platform, StringComparer.OrdinalIgnoreCase))
            throw new PlatformNotSupportedException($"Bundle '{bundleId}' does not support {platform}.");

        string root = Path.Combine(Path.GetTempPath(), "luxel-verify-" + bundleId.Replace('.', '-') + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            SampleBundleMaterializer.Materialize(repositoryRoot, bundleId, root);
            SampleFileInfo projectFile = SampleBundleMaterializer.DependencyClosure(bundleId)
                .SelectMany(item => item.Files).LastOrDefault(file => file.Kind == SampleFileKind.Project && file.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Bundle '{bundleId}' does not declare a project file.");
            string project = projectFile.OutputPath;
            (string restoreOut, string restoreErr, int restoreExit) = await RunDotnet(root, ["restore", project], bundle, cancellationToken);
            if (restoreExit != 0) throw new InvalidOperationException($"Bundle '{bundleId}' clean restore failed.\n{restoreOut}\n{restoreErr}");
            (string buildOut, string buildErr, int buildExit) = await RunDotnet(root, ["build", project, "--configuration", "Release", "--no-restore"], bundle, cancellationToken);
            if (buildExit != 0) throw new InvalidOperationException($"Bundle '{bundleId}' clean build failed.\n{buildErr}");
            if (!runSmoke) return new SampleVerificationResult(bundleId, root, project, buildOut, buildErr);
            string[] smoke = ParseDotnet(bundle.SmokeCommand ?? throw new InvalidOperationException($"Bundle '{bundleId}' has no smoke command."));
            (string stdout, string stderr, int exitCode) = await RunDotnet(root, smoke, bundle, cancellationToken);
            if (exitCode != bundle.ExpectedExitCode)
                throw new InvalidOperationException($"Bundle '{bundleId}' exited {exitCode}, expected {bundle.ExpectedExitCode}.\n{stderr}");
            if (bundle.ExpectedStdoutMarker is { } marker && !stdout.Contains(marker, StringComparison.Ordinal))
                throw new InvalidOperationException($"Bundle '{bundleId}' stdout did not contain '{marker}'.\n{stdout}");
            foreach (string artifact in bundle.ExpectedArtifacts ?? [])
                if (!File.Exists(Path.Combine(root, artifact))) throw new FileNotFoundException($"Expected bundle artifact is missing: {artifact}");
            return new SampleVerificationResult(bundleId, root, project, stdout, stderr);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string[] ParseDotnet(string command)
    {
        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !string.Equals(parts[0], "dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Only dotnet smoke commands are supported: {command}");
        return parts[1..];
    }

    private static async Task<(string Stdout, string Stderr, int ExitCode)> RunDotnet(
        string workingDirectory, IReadOnlyList<string> arguments, SampleBundleInfo bundle, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(bundle.TimeoutSeconds));
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // MSBuild worker nodes inherit redirected stdout/stderr handles. With node reuse enabled they can
        // outlive the dotnet command, so ReadToEndAsync never observes EOF even though the command exited.
        // Verification uses isolated temp trees and gains nothing from persistent build servers.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr).WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException($"Bundle '{bundle.Id}' exceeded {bundle.TimeoutSeconds}s.");
        }
        return (await stdout, await stderr, process.ExitCode);
    }
}
