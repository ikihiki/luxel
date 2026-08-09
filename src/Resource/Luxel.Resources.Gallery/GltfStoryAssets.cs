using System.Reflection;
using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.Controls;
using Luxel.Resources;
using Luxel.UI;

namespace Luxel.Resources.Gallery.Stories;

/// <summary>Deterministic embedded glTF fixtures loaded through a ResourceSystem owned by each GPU story.</summary>
public static class GltfStoryAssets
{
    public const string Box = "story://fixtures/Box.gltf";
    public const string AnimatedBox = "story://fixtures/BoxAnimated.glb";
    public const string RiggedSimple = "story://fixtures/RiggedSimple.glb";
    private static int _createdSystemCount;

    internal static int CreatedSystemCount => Volatile.Read(ref _createdSystemCount);

    internal static Widget View(
        StoryContext context,
        string uri,
        Func<AssetDocument, GpuSceneBase> createScene,
        bool animated)
    {
        ResourceSystem resources = CreateFixtureSystem();
        ResourceHandle<AssetDocument> document = resources.Load<AssetDocument>(uri);
        return ViewOwned(resources, document, createScene, animated);
    }

    internal static Widget ViewGenerated(
        StoryContext context,
        AssetDocument document,
        Func<AssetDocument, GpuSceneBase> createScene,
        bool animated)
    {
        Interlocked.Increment(ref _createdSystemCount);
        var resources = new ResourceSystem(steps: [new DocumentIdentityStep()]);
        ResourceScope scope = resources.CreateScope("gpu-story/generated-document");
        ResourceHandle<AssetDocument> handle = scope.Create<AssetDocumentSeed, AssetDocument>("generated.gltf", new AssetDocumentSeed(document));
        return ViewOwned(resources, handle, createScene, animated, scope);
    }

    internal static async Task<AssetDocument> LoadFixtureForTestAsync(string uri)
    {
        using ResourceSystem resources = CreateFixtureSystem();
        using ResourceHandle<AssetDocument> handle = resources.Load<AssetDocument>(uri);
        await handle.Ready;
        return handle.Value;
    }

    private static ResourceSystem CreateFixtureSystem()
    {
        Interlocked.Increment(ref _createdSystemCount);
        return new ResourceSystem(
            sources: [new EmbeddedFixtureSource()],
            steps: [new GltfResourceStep()]);
    }

    private static Widget ViewOwned(
        ResourceSystem resources,
        ResourceHandle<AssetDocument> document,
        Func<AssetDocument, GpuSceneBase> createScene,
        bool animated,
        IDisposable? additionalOwner = null)
    {
        GpuSceneBase? scene = null;
        int sceneVersion = -1;

        return Luxel.Controls.Kit.GpuView(256, 256,
            (device, surface, time) =>
            {
                ResourceState snapshot = document.State;
                if (!snapshot.HasValue)
                    return snapshot.Status == ResourceStatus.Failed
                        ? GpuViewRenderResult.Failed
                        : GpuViewRenderResult.Loading;

                if (scene is null || sceneVersion != snapshot.Version)
                {
                    scene?.Dispose();
                    scene = createScene(document.Value);
                    sceneVersion = snapshot.Version;
                }

                return scene.Render(device, surface, time);
            },
            animated: animated,
            dispose: () =>
            {
                scene?.Dispose();
                document.Dispose();
                additionalOwner?.Dispose();
                resources.Dispose();
            });
    }

    private sealed record AssetDocumentSeed(AssetDocument Document);

    private sealed class DocumentIdentityStep : IResourceStep<AssetDocumentSeed, AssetDocument>
    {
        public Executor Executor => Executor.Cpu;
        public Task<AssetDocument> RunAsync(AssetDocumentSeed input, ResourceUri uri, LoadContext context)
            => Task.FromResult(input.Document);
    }

    private sealed class EmbeddedFixtureSource : IResourceSource
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
