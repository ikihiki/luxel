using Luxel.Platform.Abstraction;

namespace Luxel.Platform;

/// <summary>OS固有バックエンドを包む、プロセス共有クリップボードの公開API。</summary>
public sealed class Clipboard : IDisposable
{
    private readonly IClipboardBackend _backend;

    public Clipboard(IClipboardBackend backend)
        => _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    public string Name => _backend.Name;
    public string? GetText() => _backend.GetText();
    public void SetText(string text) => _backend.SetText(text ?? string.Empty);
    public void Dispose() => _backend.Dispose();
}

/// <summary>UIコントロールが使用する現在のプロセス共有クリップボード。nullならコピー/貼り付け無効。</summary>
public static class PlatformClipboard
{
    public static Clipboard? Current { get; set; }
}
