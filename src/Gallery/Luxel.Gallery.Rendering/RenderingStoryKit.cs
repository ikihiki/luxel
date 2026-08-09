using Luxel.Typography;

namespace Luxel.Gallery;

internal static class RenderingStoryKit
{
    internal static readonly Lazy<FontCollection> JpFallback = new(() =>
        new FontCollection(GalleryFonts.Load(GalleryFonts.Regular)));

    internal static readonly Lazy<(VectorFont? Bold, VectorFont? Italic, VectorFont? BoldItalic, VectorFont? Mono)> EditorFaces = new(() =>
        (GalleryFonts.Load(GalleryFonts.Bold), null, null, GalleryFonts.Load(GalleryFonts.Mono)));
}
