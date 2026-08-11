using Luxel.Assets;
using Luxel.AssetsGpu;

namespace Luxel.Tests;

public partial class AssetsGpuTests
{
    [Fact]
    public void DocumentIndexes_RemainStableAcrossListReordering()
    {
        var first = new AssetPrimitive();
        var second = new AssetPrimitive();
        var mesh = new AssetMesh { Name = "multi" };
        mesh.Primitives.Add(first);
        mesh.Primitives.Add(second);
        var otherMesh = new AssetMesh { Name = "other" };
        var document = new AssetDocument();
        document.Meshes.Add(mesh);
        document.Meshes.Add(otherMesh);

        AssetMeshIndex meshIndex = document.Indices.GetIndex(mesh);
        AssetPrimitiveIndex firstIndex = document.Indices.GetIndex(first);
        AssetPrimitiveIndex secondIndex = document.Indices.GetIndex(second);
        Assert.Equal(new[] { firstIndex, secondIndex }, document.Indices.GetPrimitiveIndices(meshIndex));

        document.Meshes.Reverse();
        mesh.Primitives.Reverse();

        Assert.Equal(meshIndex, document.Indices.GetIndex(mesh));
        Assert.Equal(firstIndex, document.Indices.GetIndex(first));
        Assert.Equal(secondIndex, document.Indices.GetIndex(second));
        Assert.Same(mesh, document.Indices.Resolve(meshIndex));
        Assert.Same(first, document.Indices.Resolve(firstIndex));
        Assert.Equal(new[] { firstIndex, secondIndex }, document.Indices.GetPrimitiveIndices(meshIndex));
        Assert.Equal(meshIndex, document.Indices.GetMeshIndex(secondIndex));
    }

    [Fact]
    public void DocumentIndexes_RejectAssetsFromAnotherDocument()
    {
        var texture = new AssetTexture();
        var first = new AssetDocument();
        var second = new AssetDocument();
        first.Textures.Add(texture);

        Assert.Equal(new AssetTextureIndex(0), first.Indices.GetIndex(texture));
        Assert.False(second.Indices.TryGetIndex(texture, out _));
        Assert.Throws<ArgumentException>(() => second.Indices.GetIndex(texture));
    }

    [Fact]
    public void TypedRegistry_DeduplicatesWithinDocumentAndSeparatesDocuments()
    {
        var backend = new FakeGpuBackend();
        using var device = new GpuDevice(backend);
        using var registry = new AssetGpuRegistry(device);
        var sharedAsset = new AssetTexture
        {
            Name = "shared-object",
            Width = 1,
            Height = 1,
            PixelData = [255, 255, 255, 255],
        };
        var firstDocument = new AssetDocument();
        var secondDocument = new AssetDocument();
        firstDocument.Textures.Add(sharedAsset);
        secondDocument.Textures.Add(sharedAsset);
        AssetTextureIndex firstIndex = firstDocument.Indices.GetIndex(sharedAsset);
        AssetTextureIndex secondIndex = secondDocument.Indices.GetIndex(sharedAsset);

        GpuTexture firstUpload = registry.Register(firstDocument.GetHandle(sharedAsset));
        GpuTexture duplicate = registry.Register(firstDocument, firstIndex);
        GpuTexture secondUpload = registry.Register(secondDocument, secondIndex);

        Assert.Same(firstUpload, duplicate);
        Assert.NotSame(firstUpload, secondUpload);
        Assert.Same(firstUpload, registry.Resolve(sharedAsset));
        Assert.Same(firstUpload, registry.Resolve(firstDocument, firstIndex));
        Assert.Same(secondUpload, registry.Resolve(secondDocument, secondIndex));
        Assert.NotSame(registry.GetDocumentState(firstDocument), registry.GetDocumentState(secondDocument));
        Assert.Equal(4, backend.LiveResources); // registry defaults + one texture per document

        registry.Register(firstDocument);
        Assert.True(registry.GetDocumentState(firstDocument).IsInstalled);
        Assert.Equal(4, backend.LiveResources);
    }
}
