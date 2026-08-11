using System.Diagnostics;

namespace Luxel.Shaders.Slang.Native;

internal sealed record SlangProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?> EnvironmentVariables);

internal sealed record SlangProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal interface ISlangProcessRunner
{
    Task<SlangProcessResult> RunAsync(SlangProcessRequest request, CancellationToken cancellationToken);
}

internal sealed class SlangProcessRunner : ISlangProcessRunner
{
    public async Task<SlangProcessResult> RunAsync(SlangProcessRequest request, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(request.FileName)
        {
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (string argument in request.Arguments) start.ArgumentList.Add(argument);
        foreach ((string name, string? value) in request.EnvironmentVariables) start.Environment[name] = value;

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Failed to start '{request.FileName}'.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new FileNotFoundException($"Unable to start slangc at '{request.FileName}'.", request.FileName, exception);
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new SlangProcessResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch (OperationCanceledException) { }
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
