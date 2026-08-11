using System.Reflection;
using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.Resources;
using Luxel.Resources.Browser;

namespace Luxel.Resources.Gallery.Stories;

/// <summary>Deterministic embedded glTF fixtures loaded through a ResourceSystem owned by each GPU story.</summary>
public static class GltfStoryAssets
{
    public const string Box = "story://fixtures/Box.gltf";
    public const string AnimatedBox = "story://fixtures/BoxAnimated.glb";
    public const string RiggedSimple = "story://fixtures/RiggedSimple.glb";
    internal static async Task<AssetDocument> LoadFixtureForTestAsync(string uri)
    {
        using ResourceSystem resources = CreateFixtureSystem();
        using ResourceHandle<AssetDocument> handle = resources.Load<AssetDocument>(uri);
        await handle.Ready;
        return handle.Value;
    }

    private static ResourceSystem CreateFixtureSystem()
    {
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaultHandles defaults = AddPlatformCore(builder);
        builder.Sources.Add(new EmbeddedFixtureSource()).RunOn(defaults.IoDomain).ManagedBy(defaults.IoManager).Register();
        builder.Steps.Add<byte[], AssetDocument>(new GltfResourceStep())
            .RunOn(defaults.CpuDomain).ManagedBy(defaults.CpuManager).Register();
        return builder.Build();
    }

    private static ResourceSystemDefaultHandles AddPlatformCore(ResourceSystemBuilder builder)
        => OperatingSystem.IsBrowser() ? builder.AddBrowserCore() : ResourceSystemDefaults.AddCore(builder);

    internal sealed class EmbeddedFixtureSource : IResourceSource
    {
        private static readonly Assembly Assembly = typeof(GltfStoryAssets).Assembly;
        private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fixtures/Box.gltf"] = "Luxel.Gallery.Resources.Fixtures.Box.gltf",
            ["fixtures/Box0.bin"] = "Luxel.Gallery.Resources.Fixtures.Box0.bin",
            ["fixtures/BoxAnimated.glb"] = "Luxel.Gallery.Resources.Fixtures.BoxAnimated.glb",
            ["fixtures/RiggedSimple.glb"] = "Luxel.Gallery.Resources.Fixtures.RiggedSimple.glb",
        };

        public IEnumerable<string> Schemes => ["story"];

        public async Task<byte[]> ReadAsync(ResourceUri uri, LoadContext context)
        {
            string path = uri.Path.TrimStart('/');
            if (!Names.TryGetValue(path, out string? name)) throw new FileNotFoundException(path);
            await using Stream stream = Assembly.GetManifestResourceStream(name)
                ?? throw new FileNotFoundException($"Embedded fixture '{name}' was not found.");
            using var output = new MemoryStream();
            await stream.CopyToAsync(output, context.Token);
            return output.ToArray();
        }
    }
}
