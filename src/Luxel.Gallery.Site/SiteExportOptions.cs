using System.Diagnostics;

namespace Luxel.Gallery.Site;

public enum StaticCaptureMode
{
    All,
    GoldenOnly,
    None,
}

public sealed record SiteExportOptions
{
    public StaticCaptureMode StaticCapture { get; init; } = StaticCaptureMode.All;
    public bool Incremental { get; init; }
    public Action<string>? Log { get; init; }
}

public sealed record SiteExportMetrics(
    TimeSpan Setup,
    TimeSpan StoryGeneration,
    TimeSpan NativeRealization,
    TimeSpan Validation,
    int RuntimeStories,
    int DocumentStories,
    int GoldenImages,
    int DynamicCaptures,
    int PolicySkips,
    int FilesWritten,
    int FilesReused)
{
    public override string ToString()
        => $"setup={Setup.TotalMilliseconds:F0}ms, stories={StoryGeneration.TotalMilliseconds:F0}ms, native={NativeRealization.TotalMilliseconds:F0}ms, validation={Validation.TotalMilliseconds:F0}ms, runtime={RuntimeStories}, documents={DocumentStories}, goldens={GoldenImages}, captures={DynamicCaptures}, skipped={PolicySkips}, written={FilesWritten}, reused={FilesReused}";
}

internal sealed class SiteExportMetricsBuilder
{
    private readonly Stopwatch _total = Stopwatch.StartNew();
    public bool Incremental;
    public TimeSpan Setup;
    public TimeSpan NativeRealization;
    public TimeSpan Validation;
    public int RuntimeStories;
    public int DocumentStories;
    public int GoldenImages;
    public int DynamicCaptures;
    public int PolicySkips;
    public int FilesWritten;
    public readonly HashSet<string> ManagedFiles = new(StringComparer.Ordinal);

    public int FilesReused;

    public SiteExportMetrics Build()
        => new(Setup, _total.Elapsed - Setup - Validation, NativeRealization, Validation,
            RuntimeStories, DocumentStories, GoldenImages, DynamicCaptures, PolicySkips, FilesWritten, FilesReused);
}
