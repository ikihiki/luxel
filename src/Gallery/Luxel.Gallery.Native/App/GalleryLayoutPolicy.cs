using Luxel.Controls;

namespace Luxel.Gallery;

internal enum GalleryLayoutMode
{
    Wide,
    Compact,
}

internal readonly record struct GalleryPreviewExtent(int Width, int Height);

/// <summary>Native Gallery の logical-pixel layout policy。960x640 を compact baseline とする。</summary>
internal static class GalleryLayoutPolicy
{
    public const float BaselineWidth = 960f;
    public const float BaselineHeight = 640f;
    public const float CompactBreakpointWidth = 1040f;
    public const float CompactBreakpointHeight = 700f;
    public const float ToolbarHeight = 148f;
    public const float MinimumPreviewWidth = 280f;
    public const float MinimumPreviewHeight = 180f;
    public const float CompactToolsHeight = 190f;
    public const float MinimumToolsHeight = 96f;
    public const float MaximumToolsHeight = 440f;

    public static GalleryLayoutMode Select(float width, float height)
        => width <= CompactBreakpointWidth || height <= CompactBreakpointHeight
            ? GalleryLayoutMode.Compact
            : GalleryLayoutMode.Wide;

    public static float SidebarWidth(float windowWidth, float desiredWidth)
    {
        float maximum = MathF.Max(0, windowWidth - MinimumPreviewWidth - Splitter.Thickness);
        return Math.Clamp(desiredWidth, MathF.Min(220f, maximum), MathF.Min(420f, maximum));
    }

    public static float MainWidth(GalleryLayoutMode mode, float windowWidth, float sidebarWidth)
    {
        float reserved = mode == GalleryLayoutMode.Wide
            ? SidebarWidth(windowWidth, sidebarWidth) + Splitter.Thickness
            : 0f;
        return MathF.Max(1f, windowWidth - reserved);
    }

    public static float ToolsHeight(GalleryLayoutMode mode, float windowHeight, float desiredHeight)
    {
        float maximum = MathF.Max(0f,
            windowHeight - ToolbarHeight - MinimumPreviewHeight - Splitter.Thickness);
        float desired = mode == GalleryLayoutMode.Compact
            ? MathF.Min(desiredHeight, CompactToolsHeight)
            : desiredHeight;
        float minimum = MathF.Min(MinimumToolsHeight, maximum);
        return Math.Clamp(desired, minimum, MathF.Min(MaximumToolsHeight, maximum));
    }

    public static GalleryPreviewExtent PreviewExtent(
        GalleryLayoutMode mode,
        float windowWidth,
        float windowHeight,
        float sidebarWidth,
        float toolsHeight,
        bool zen,
        float maximumSurfaceWidth,
        float maximumSurfaceHeight)
    {
        float width = MainWidth(mode, windowWidth, sidebarWidth);
        float occupiedByTools = zen
            ? 0f
            : Splitter.Thickness + ToolsHeight(mode, windowHeight, toolsHeight);
        float height = MathF.Max(1f, windowHeight - ToolbarHeight - occupiedByTools);
        return new GalleryPreviewExtent(
            Math.Max(1, (int)MathF.Min(maximumSurfaceWidth, width)),
            Math.Max(1, (int)MathF.Min(maximumSurfaceHeight, height)));
    }
}
