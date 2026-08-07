using Luxel.Assets;

namespace Luxel.Assets.Gltf;

/// <summary>ファイルシステム非依存の glTF 2.0 decoder。入力 bytes と外部参照 resolver だけを使用する。</summary>
public static class GltfDecoder
{
    public static GltfIndexDocument ParseIndex(ReadOnlySpan<byte> bytes) => GltfParser.Parse(bytes).Document;

    public static async Task<AssetDocument> DecodeAsync(ReadOnlyMemory<byte> bytes,
        Func<string, CancellationToken, ValueTask<ReadOnlyMemory<byte>>>? externalResolver = null,
        CancellationToken cancellationToken = default)
    {
        var parsed = GltfParser.Parse(bytes.Span);
        var buffers = new byte[parsed.Document.Buffers.Count][];
        for (int i = 0; i < buffers.Length; i++)
        {
            var buffer = parsed.Document.Buffers[i];
            byte[] content;
            if (buffer.Uri is null)
                content = parsed.Binary ?? throw new InvalidDataException("A buffer has no URI, but the input has no GLB BIN chunk.");
            else if (TryDecodeDataUri(buffer.Uri, out var embedded))
                content = embedded;
            else
                content = (await ResolveAsync(buffer.Uri, externalResolver, cancellationToken).ConfigureAwait(false)).ToArray();
            if (content.Length < buffer.ByteLength)
                throw new InvalidDataException($"Buffer {i} contains {content.Length} bytes; {buffer.ByteLength} are required.");
            buffers[i] = content;
        }

        ValidateRanges(parsed.Document, buffers);
        var images = new byte[parsed.Document.Images.Count][];
        for (int i = 0; i < images.Length; i++)
        {
            var image = parsed.Document.Images[i];
            if (image.Uri is { } uri)
            {
                images[i] = TryDecodeDataUri(uri, out var embedded)
                    ? embedded
                    : (await ResolveAsync(uri, externalResolver, cancellationToken).ConfigureAwait(false)).ToArray();
            }
            else if (image.BufferView is { } viewIndex)
            {
                var view = parsed.Document.BufferViews[viewIndex.Value];
                images[i] = buffers[view.Buffer.Value].AsSpan(view.ByteOffset, view.ByteLength).ToArray();
            }
            else
            {
                throw new InvalidDataException($"Image {i} has neither uri nor bufferView.");
            }
        }

        return GltfAssetConverter.Convert(parsed.Document, buffers, images, parsed.SourceFormat);
    }

    private static ValueTask<ReadOnlyMemory<byte>> ResolveAsync(string uri,
        Func<string, CancellationToken, ValueTask<ReadOnlyMemory<byte>>>? resolver,
        CancellationToken cancellationToken) => resolver is null
        ? ValueTask.FromException<ReadOnlyMemory<byte>>(new InvalidDataException($"External glTF resource '{uri}' requires a resolver."))
        : resolver(uri, cancellationToken);

    private static bool TryDecodeDataUri(string uri, out byte[] bytes)
    {
        bytes = [];
        if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        int comma = uri.IndexOf(',');
        if (comma < 0) throw new InvalidDataException("Malformed data URI.");
        string metadata = uri[5..comma];
        string payload = uri[(comma + 1)..];
        bytes = metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(payload)
            : System.Text.Encoding.UTF8.GetBytes(global::System.Uri.UnescapeDataString(payload));
        return true;
    }

    private static void ValidateRanges(GltfIndexDocument document, byte[][] buffers)
    {
        for (int i = 0; i < document.BufferViews.Count; i++)
        {
            var view = document.BufferViews[i];
            int available = buffers[view.Buffer.Value].Length;
            if ((long)view.ByteOffset + view.ByteLength > available)
                throw new InvalidDataException($"bufferView {i} exceeds buffer {view.Buffer.Value}.");
        }
        for (int i = 0; i < document.Accessors.Count; i++)
        {
            var accessor = document.Accessors[i];
            var view = document.BufferViews[accessor.BufferView!.Value.Value];
            int elementSize = checked(GltfValidator.ComponentSize(accessor.ComponentType) * GltfValidator.ComponentCount(accessor.Type));
            int stride = view.ByteStride ?? elementSize;
            if (stride < elementSize) throw new InvalidDataException($"accessor {i} byteStride is smaller than its element.");
            long end = accessor.Count == 0 ? accessor.ByteOffset : (long)accessor.ByteOffset + (long)(accessor.Count - 1) * stride + elementSize;
            if (end > view.ByteLength) throw new InvalidDataException($"accessor {i} exceeds its bufferView.");
        }
    }
}
