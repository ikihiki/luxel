using Luxel.Controls;
using Luxel.DevTools;
using Luxel.UI;

namespace Luxel.Gallery;

/// <summary>Result of one deterministic story/widget capture. Failures are returned per item.</summary>
public sealed record GallerySnapshotResult(string Id, GallerySnapshotStatus Status, byte[]? Png = null,
    int Width = 0, int Height = 0, string? Error = null);

public enum GallerySnapshotStatus
{
    Captured,
    Unavailable,
    Error,
}

/// <summary>Reusable deterministic offscreen snapshot operations shared by E2E and static export.</summary>
public static class GallerySnapshots
{
    public const int WarmupSteps = 8;
    public const float FixedDt = 1f / 60f;

    public static GallerySnapshotResult CaptureStory(GalleryHost host, StoryInfo story)
    {
        if (story.RealWindowOnly)
            return new(story.Path, GallerySnapshotStatus.Unavailable, Error: "RealWindowOnly story cannot be captured offscreen.");
        try
        {
            host.SelectExact(story.Path);
            Stabilize(host);
            return Snapshot(host, story.Path);
        }
        catch (Exception e)
        {
            return new(story.Path, GallerySnapshotStatus.Error, Error: $"{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>Captures the already selected and stabilized story without rebuilding it.</summary>
    public static GallerySnapshotResult CaptureCurrent(GalleryHost host, string id)
    {
        try { return Snapshot(host, id); }
        catch (Exception e) { return new(id, GallerySnapshotStatus.Error, Error: $"{e.GetType().Name}: {e.Message}"); }
    }

    public static GallerySnapshotResult CaptureWidget(GalleryHost host, string id, Widget widget,
        int width = 800, int height = 480, bool dark = false)
    {
        try
        {
            host.SelectWidget(widget, width, height, dark);
            Stabilize(host);
            return Snapshot(host, id);
        }
        catch (Exception e)
        {
            return new(id, GallerySnapshotStatus.Error, Error: $"{e.GetType().Name}: {e.Message}");
        }
    }

    public static void Stabilize(GalleryHost host)
    {
        for (int i = 0; i < WarmupSteps; i++) host.Step(FixedDt);
        if (!HighlightQueue.WaitIdle(15000))
            throw new TimeoutException("Syntax highlighting did not become idle within 15 seconds.");
        for (int i = 0; i < 2; i++) host.Step(0f);
    }

    public static TextEditorView? FindDocument(Widget? root)
    {
        if (root is null) return null;
        if (root is TextEditorView { DocSource: not null } editor) return editor;
        foreach (Widget child in root.DebugChildren())
            if (FindDocument(child) is { } found) return found;
        return null;
    }

    private static GallerySnapshotResult Snapshot(GalleryHost host, string id)
    {
        (byte[] rgba, int w, int h)? shot = host.SnapshotRgba();
        return shot is { } s
            ? new(id, GallerySnapshotStatus.Captured, Png.Encode(s.w, s.h, s.rgba), s.w, s.h)
            : new(id, GallerySnapshotStatus.Error, Error: "No rendered frame was available.");
    }
}
