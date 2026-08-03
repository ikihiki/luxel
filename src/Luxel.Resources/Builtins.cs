using System.Buffers.Binary;

namespace Luxel.Resources;

/// <summary>メインメモリ上のデコード済み画像 (RGBA8 密)。Disk→RAM の中間表現。</summary>
public sealed class CpuImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;
}

/// <summary>素の RGBA 画像形式 (.tex): 8B ヘッダ(w,h int32 LE) + RGBA。</summary>
public static class ImageCodec
{
    public static byte[] EncodeTex(int w, int h, ReadOnlySpan<byte> rgba)
    {
        var b = new byte[8 + w * h * 4];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), w);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), h);
        rgba[..(w * h * 4)].CopyTo(b.AsSpan(8));
        return b;
    }

    public static CpuImage DecodeTex(byte[] data)
    {
        int w = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0));
        int h = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        var px = new byte[w * h * 4];
        data.AsSpan(8, w * h * 4).CopyTo(px);
        return new CpuImage(w, h, px);
    }
}

/// <summary>file:// (および既定スキーム) を VFS 経由で読む。変更監視対応。</summary>
public sealed class FileSource(IVirtualFileSystem vfs) : IResourceSource
{
    public IEnumerable<string> Schemes => ["file", ""];
    public Task<byte[]> ReadAsync(ResourceUri uri, LoadContext ctx) => vfs.ReadAsync(uri.Path, ctx.Token);
    public IReloadToken? Watch(ResourceUri uri, Action onChanged) => vfs.Watch(uri.Path, onChanged);
}

/// <summary>http:// / https:// を HttpClient で取得。</summary>
public sealed class HttpSource(HttpClient http) : IResourceSource
{
    public IEnumerable<string> Schemes => ["http", "https"];
    public Task<byte[]> ReadAsync(ResourceUri uri, LoadContext ctx) => http.GetByteArrayAsync(uri.Url, ctx.Token);
}

/// <summary>byte[] → CpuImage (.tex デコード、CPU ステージ)。</summary>
public sealed class TexDecoder : IResourceStep<byte[], CpuImage>
{
    public Executor Executor => Executor.Cpu;
    public IEnumerable<string> Extensions => [".tex"];
    public Task<CpuImage> RunAsync(byte[] input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(ImageCodec.DecodeTex(input));
}

// 組込み Source/Step は呼び出し側が ResourceSystem のコンストラクタまたは AddSource/AddStep で登録する。

/// <summary>Reads only workspace:// resources from a shared mutable workspace VFS.</summary>
public sealed class WorkspaceSource(WorkspaceFileSystem workspace) : IResourceSource
{
    public IEnumerable<string> Schemes => ["workspace"];
    public Task<byte[]> ReadAsync(ResourceUri uri, LoadContext ctx) => workspace.ReadAsync(uri.Path, ctx.Token);
    public IReloadToken? Watch(ResourceUri uri, Action onChanged) => workspace.Watch(uri.Path, onChanged);
}
