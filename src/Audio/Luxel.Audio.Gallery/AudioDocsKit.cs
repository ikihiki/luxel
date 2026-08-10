using System.Reflection;
using Luxel.Controls;
using Luxel.Gallery.DocKit;

namespace Luxel.Audio.Gallery;

internal static class AudioDocsKit
{
    internal static DocEmbed StoryRef(StoryContext ctx, string path, bool knobs = false)
        => DocsKit.StoryRef(ctx, path, knobs);

    internal static DocMarkdown SampleSource(string relativePath, string? region = null, string? language = null)
        => DocsKit.SampleSource(typeof(AudioDocsKit).Assembly, relativePath, region, language, searchLoadedAssemblies: true);
}
