using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.Assets.Gltf;

/// <summary>ResourceSystem 用 adapter。外部参照を元 ResourceUri から相対解決し、依存 load として登録する。</summary>
public sealed class GltfResourceStep : IResourceStep<byte[], AssetDocument>
{
    public Executor Executor => Executor.Cpu;
    public IEnumerable<string> Extensions => [".gltf", ".glb"];

    public async Task<AssetDocument> RunAsync(byte[] input, ResourceUri uri, LoadContext context)
    {
        return await GltfDecoder.DecodeAsync(input, ResolveExternalAsync, context.Token).ConfigureAwait(false);

        async ValueTask<ReadOnlyMemory<byte>> ResolveExternalAsync(string reference, CancellationToken cancellationToken)
        {
            var dependencyUri = ResourceUriResolver.ResolveRelative(uri, reference);
            var handle = context.Load<byte[]>(dependencyUri.Url);
            return await context.Require(handle).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

internal static class ResourceUriResolver
{
    public static ResourceUri ResolveRelative(ResourceUri origin, string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if (reference.Contains("://", StringComparison.Ordinal) || HasScheme(reference)) return new ResourceUri(reference);

        string cleanReference = reference.Replace('\\', '/');
        int queryStart = cleanReference.IndexOfAny(['?', '#']);
        string suffix = queryStart >= 0 ? cleanReference[queryStart..] : "";
        string referenceSegments = queryStart >= 0 ? cleanReference[..queryStart] : cleanReference;
        int slash = origin.Path.LastIndexOf('/');
        string prefix = slash >= 0 ? origin.Path[..(slash + 1)] : "";
        string normalized = Normalize(prefix + referenceSegments);
        string value = origin.Scheme is "file" or "" ? normalized + suffix : $"{origin.Scheme}://{normalized}{suffix}";
        return new ResourceUri(value);
    }

    private static bool HasScheme(string value)
    {
        int colon = value.IndexOf(':');
        if (colon <= 0) return false;
        for (int i = 0; i < colon; i++)
            if (!char.IsAsciiLetterOrDigit(value[i]) && value[i] is not '+' and not '-' and not '.') return false;
        return true;
    }

    private static string Normalize(string value)
    {
        bool rooted = value.StartsWith('/');
        var segments = new List<string>();
        foreach (string segment in value.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..") { if (segments.Count > 0) segments.RemoveAt(segments.Count - 1); continue; }
            segments.Add(segment);
        }
        return (rooted ? "/" : "") + string.Join('/', segments);
    }
}
