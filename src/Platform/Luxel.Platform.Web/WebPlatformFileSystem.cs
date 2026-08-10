namespace Luxel.Platform.Web;

/// <summary>
/// Web/WASM 上でホスト提供の delegate を明示的に使う file-system implementation。
/// OS の physical file API は使用しない。
/// </summary>
public sealed class WebPlatformFileSystem : IPlatformFileSystem
{
    private readonly Func<string, CancellationToken, Task<byte[]>> _read;
    private readonly Func<string, bool> _exists;

    public WebPlatformFileSystem(
        Func<string, CancellationToken, Task<byte[]>> read,
        Func<string, bool>? exists = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        _read = read;
        _exists = exists ?? (_ => false);
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => _read(path, cancellationToken);

    public bool Exists(string path) => _exists(path);
}

/// <summary>Web では implicit physical file system を作らず、ホスト reader を明示する factory。</summary>
public static class WebPlatformFileSystems
{
    public static IPlatformFileSystem Create(
        Func<string, CancellationToken, Task<byte[]>> read,
        Func<string, bool>? exists = null)
        => new WebPlatformFileSystem(read, exists);
}
