using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Text.Json;
using Luxel.Gallery;
using Luxel.UI;

string output = args.FirstOrDefault() ?? throw new ArgumentException("Output manifest path is required.");
var services = new ServiceCollection();
services.AddCoreUiStory();
using ServiceProvider provider = services.BuildServiceProvider();
StoryCatalog catalog = provider.GetRequiredService<StoryCatalog>();
RuntimeStoryDescriptor[] stories = CoreUiStoryProject.RuntimeStories(catalog)
    .Select(story => new RuntimeStoryDescriptor(
        story.Path,
        story.Width,
        story.Height,
        story.ArgDefinitions ?? Array.Empty<StoryArgDefinition>(),
        story.CapabilityNote,
        story.ProductionComponent?.ComponentType))
    .ToArray();
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
