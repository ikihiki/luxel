using System.Text;
using System.Text.Json;
using Luxel.Gallery;
using Luxel.UI;

string output = args.FirstOrDefault() ?? throw new ArgumentException("Output manifest path is required.");
StoryCatalog catalog = CoreUiStoryProject.CreateCatalog();
RuntimeStoryDescriptor[] stories =
[
    .. CoreUiStoryProject.RuntimeStories(catalog).Select(story => new RuntimeStoryDescriptor(
        story.Path,
        story.Width,
        story.Height,
        story.ArgDefinitions ?? Array.Empty<StoryArgDefinition>(),
        story.CapabilityNote,
        story.ProductionComponent?.ComponentType)),
    new(CanonicalTriangleRecipe.Story, CanonicalTriangleRecipe.Width, CanonicalTriangleRecipe.Height,
        Array.Empty<StoryArgDefinition>(), "Specialized browser WebGPU validation route.", null),
];
stories = stories.OrderBy(story => story.Path, StringComparer.Ordinal).ToArray();
var manifest = new BrowserRuntimeManifest(
    CoreUiStoryProject.RuntimeBundleId,
    ProtocolVersion: 2,
    EntryUrl: "./",
    Stories: stories);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
}) + "\n", new UTF8Encoding(false));
Console.WriteLine($"browser-runtime-manifest: stories={stories.Length}, output={output}");

internal sealed record BrowserRuntimeManifest(string BundleId, int ProtocolVersion, string EntryUrl,
    IReadOnlyList<RuntimeStoryDescriptor> Stories);
internal sealed record RuntimeStoryDescriptor(string Path, int Width, int Height,
    IReadOnlyList<StoryArgDefinition> Args, string? CapabilityNote, string? ComponentType);
