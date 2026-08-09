namespace Luxel.Platform;

/// <summary>OS 固有 API を公開せず、プラットフォーム上のファイルを読み取る抽象。</summary>
public interface IPlatformFileReader
{
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
    bool Exists(string path);
}

/// <summary>読取に加えて、対応プラットフォームでは変更監視を提供するファイルシステム抽象。</summary>
public interface IPlatformFileSystem : IPlatformFileReader
{
    /// <summary>変更監視。未対応のプラットフォームでは <see langword="null"/>。</summary>
    IDisposable? Watch(string path, Action onChanged) => null;
}

/// <summary>
/// 従来の asset-root API とプラットフォーム実装を接続する factory registry。
/// desktop 実装は起動時に factory を登録し、Web/WASM は明示的な reader を渡す。
/// </summary>
public static class PlatformFileSystems
{
    private static readonly object Gate = new();
    private static Func<string, IPlatformFileSystem>? _physicalFactory;

    /// <summary>現在のプロセスで使う physical file system factory を登録する。</summary>
    public static void RegisterPhysicalFactory(Func<string, IPlatformFileSystem> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (Gate) _physicalFactory = factory;
    }

    /// <summary>登録済み desktop factory から rooted physical file system を作成する。</summary>
    public static IPlatformFileSystem CreatePhysical(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException(
                "Physical files are not available implicitly in WebAssembly. Pass an explicit IPlatformFileSystem or virtual file system.");

        Func<string, IPlatformFileSystem>? factory;
        lock (Gate) factory = _physicalFactory;
        return factory?.Invoke(root)
            ?? throw new InvalidOperationException(
                "No physical file-system factory is registered. Reference a desktop Luxel.Platform implementation or pass an explicit IPlatformFileSystem.");
    }
}
