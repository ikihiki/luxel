using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.Assets.Gltf;

/// <summary>ResourceSystem 用 adapter。外部参照を元 ResourceUri から相対解決し、依存 load として登録する。</summary>
public sealed class GltfResourceStep : IResourceStep<byte[], AssetDocument>
{
    public IEnumerable<string> Extensions => [".gltf", ".glb"];

    public async Task<AssetDocument> RunAsync(byte[] input, ResourceUri uri, LoadContext context)
    {
        return await GltfDecoder.DecodeAsync(input, ResolveExternalAsync, context.Token).ConfigureAwait(false);

        async ValueTask<ReadOnlyMemory<byte>> ResolveExternalAsync(string reference, CancellationToken cancellationToken)
        {
            ResourceUri resolved = uri.Resolve(reference);
            try
            {
                using ResourceHandle<byte[]> handle = context.LoadRelative<byte[]>(reference);
                byte[] bytes = await context.Require(handle).WaitAsync(cancellationToken).ConfigureAwait(false);
                return bytes;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new InvalidDataException(
                    $"Failed to load external glTF resource '{reference}' resolved to '{resolved.Url}'.", error);
            }
        }
    }
}
