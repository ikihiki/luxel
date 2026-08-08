using Luxel.Assets;
using Luxel.Assets.Gltf;
using Luxel.Resources;

namespace Luxel.Gallery.Stories;

/// <summary>Gallery のdesktop composition rootからglTFをResourceSystem経由で読み込む。</summary>
public static class GltfStoryAssets
{
    public static AssetDocument LoadDocument(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"glTF path has no directory: {path}");

        using var resources = new ResourceSystem(
            sources: ResourceSystemDefaults.BuiltinSources(assetRoot: root),
            steps: [new GltfResourceStep()]);
        using ResourceHandle<AssetDocument> handle = resources.Load<AssetDocument>(Path.GetFileName(fullPath));
        handle.Ready.GetAwaiter().GetResult();
        return handle.Value;
    }
}
