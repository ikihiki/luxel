using System.Text;
using Luxel.Platform;
using Luxel.Resources;

namespace Luxel.Tests;

public sealed class PlatformResourceIoTests
{
    [Theory]
    [InlineData("assets/models/scene.gltf", "../textures/albedo.png", "file", "assets/textures/albedo.png")]
    [InlineData("https://example.test/assets/models/scene.gltf", "../textures/albedo.png", "https", "https://example.test/assets/textures/albedo.png")]
    [InlineData("workspace://project/models/scene.gltf", "../buffers/mesh.bin", "workspace", "workspace://project/buffers/mesh.bin")]
    [InlineData("workspace://project/models/scene.gltf", "/shared/mesh.bin", "workspace", "workspace://project/shared/mesh.bin")]
    public void ResourceUriResolvePreservesScheme(string baseUri, string relative, string scheme, string expected)
    {
        ResourceUri resolved = new ResourceUri(baseUri).Resolve(relative);

        Assert.Equal(scheme, resolved.Scheme);
        Assert.Equal(expected, resolved.Url);
    }

    [Fact]
    public void ResourceUriResolveKeepsBaseForFragmentOnlyReference()
    {
        ResourceUri resolved = new ResourceUri("https://example.test/model/scene.gltf?rev=1#old").Resolve("#mesh/0");

        Assert.Equal("https://example.test/model/scene.gltf?rev=1#mesh/0", resolved.Url);
    }

    [Fact]
    public async Task PlatformFileSourceReadsThroughPlatformAbstraction()
    {
        var files = new FakePlatformFileSystem();
        files.Set("assets/value.bin", [1, 2, 3]);
        using var resources = ResourceTestSystem.Create(sources: [new PlatformFileSource(files)]);

        using ResourceHandle<byte[]> handle = resources.Load<byte[]>("assets/value.bin");
        await handle.Ready;

        Assert.Equal([1, 2, 3], handle.Value);
        Assert.Equal("assets/value.bin", files.LastReadPath);
    }

    [Fact]
    public async Task LoadContextCanLoadRelativeWorkspaceDependency()
    {
        var workspace = new WorkspaceFileSystem();
        workspace.Set("project/models/scene.bundle", Encoding.UTF8.GetBytes("../buffers/mesh.bin"));
        workspace.Set("project/buffers/mesh.bin", Encoding.UTF8.GetBytes("mesh-data"));
        using var resources = ResourceTestSystem.Create(
            sources: [new WorkspaceSource(workspace)],
            steps: [new RelativeBundleStep()]);

        using ResourceHandle<RelativeBundle> handle = resources.Load<RelativeBundle>("workspace://project/models/scene.bundle");
        await handle.Ready;

        Assert.Equal("mesh-data", handle.Value.DependencyText);
    }

    private sealed class RelativeBundle(string dependencyText)
    {
        public string DependencyText { get; } = dependencyText;
    }

    private sealed class RelativeBundleStep : IResourceStep<byte[], RelativeBundle>
    {
        public IEnumerable<string> Extensions => [".bundle"];

        public async Task<RelativeBundle> RunAsync(byte[] input, ResourceUri uri, LoadContext context)
        {
            string relative = Encoding.UTF8.GetString(input);
            using ResourceHandle<byte[]> dependency = context.LoadRelative<byte[]>(relative);
            await dependency.Ready;
            return new RelativeBundle(Encoding.UTF8.GetString(dependency.Value));
        }
    }

    private sealed class FakePlatformFileSystem : IPlatformFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public string? LastReadPath { get; private set; }

        public void Set(string path, byte[] data) => _files[path] = data;

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastReadPath = path;
            return Task.FromResult((byte[])_files[path].Clone());
        }

        public bool Exists(string path) => _files.ContainsKey(path);
    }
}
