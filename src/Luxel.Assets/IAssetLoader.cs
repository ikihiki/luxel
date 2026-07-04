namespace Luxel.Assets;

/// <summary>
/// 3D asset ファイル loader の規約。glTF / .glb / MMD / FBX 等の各形式が実装、
/// <see cref="AssetLoaders.Register"/> で登録後 <see cref="AssetLoaders.LoadAsync"/> から拡張子で自動 dispatch。
/// </summary>
public interface IAssetLoader
{
    /// <summary>パス (拡張子) を受け付けるか。</summary>
    bool CanLoad(string path);
    Task<AssetDocument> LoadAsync(string path);
}

/// <summary>登録済み <see cref="IAssetLoader"/> の dispatch ハブ。</summary>
public static class AssetLoaders
{
    private static readonly List<IAssetLoader> _loaders = new();

    public static void Register(IAssetLoader loader) => _loaders.Add(loader);
    public static void Clear() => _loaders.Clear();

    public static Task<AssetDocument> LoadAsync(string path)
    {
        foreach (var l in _loaders)
            if (l.CanLoad(path)) return l.LoadAsync(path);
        throw new NotSupportedException($"No loader registered for: {path}");
    }
}
