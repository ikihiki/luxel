using System.Runtime.CompilerServices;
using Luxel.Platform;

namespace Luxel.Gallery.Site.Tests;

internal sealed class TestPlatformFileSystem(string root) : IPlatformFileSystem
{
    private readonly string _root = Path.GetFullPath(root);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllBytesAsync(Full(path), cancellationToken);

    public bool Exists(string path) => File.Exists(Full(path));

    private string Full(string path) => Path.GetFullPath(Path.Combine(_root, path));
}

internal static class TestPlatformFileSystemRegistration
{
    [ModuleInitializer]
    internal static void Register()
        => PlatformFileSystems.RegisterPhysicalFactory(static root => new TestPlatformFileSystem(root));
}
