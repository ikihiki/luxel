using System.Reflection;
using Luxel.Resources;

namespace LuxelCavern.Core;

/// <summary>
/// アセンブリの埋め込みリソースを読む <see cref="IResourceSource"/> (スキーム <c>res://</c>)。
/// ゲームに同梱するレベル (.tmj) 等を <see cref="ResourceSystem"/> のノードとして扱えるようにする
/// (ファイル VFS と衝突しない独自スキームなので、ホストの共有 ResourceSystem に足しても安全)。
/// リソース名はパス末尾 (例 <c>levels/cavern1.tmj</c> → <c>...levels.cavern1.tmj</c>) で一致させる。
/// </summary>
public sealed class EmbeddedResourceSource : IResourceSource
{
    private readonly Assembly _asm;

    public EmbeddedResourceSource(Assembly asm) => _asm = asm;

    public IEnumerable<string> Schemes => ["res"];

    public Task<byte[]> ReadAsync(ResourceUri uri, LoadContext ctx)
    {
        string name = Resolve(uri.Path) ?? throw new FileNotFoundException($"埋め込みリソースが見つかりません: {uri}");
        using Stream s = _asm.GetManifestResourceStream(name)!;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return Task.FromResult(ms.ToArray());
    }

    /// <summary>パス末尾一致で埋め込みリソース名を解決 (区切りは '.'、'/'・'\\' は無視)。</summary>
    private string? Resolve(string path)
    {
        string tail = path.Replace('\\', '/').TrimEnd('/');
        int slash = tail.LastIndexOf('/');
        if (slash >= 0) tail = tail[(slash + 1)..];
        string[] names = _asm.GetManifestResourceNames();
        return names.FirstOrDefault(n => n.EndsWith("." + tail, StringComparison.OrdinalIgnoreCase))
            ?? names.FirstOrDefault(n => n.Equals(tail, StringComparison.OrdinalIgnoreCase));
    }
}
