using Luxel.AssetRuntime;
using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.Gltf;

/// <summary>
/// <c>foo.glb#mesh/N/vertex</c>、<c>foo.glb#mesh/N/index</c>、<c>foo.glb#materials</c> のような URI を処理する
/// GpuBuffer プロバイダ。N は SceneAssets.OrderedPrimitives の index (primitive の登場順、SceneBuilder が確定)。
/// </summary>
public sealed class GltfBufferStep(GpuDevice device) : IResourceStep<SceneAssets, GpuBuffer>
{
    private readonly GpuDevice _device = device;
    /// <summary>GPU executor で実行 (buffer 確保のため)。</summary>
    public Executor Executor => Executor.External;
    /// <summary>処理対象の fragment パターン。</summary>
    public IEnumerable<string> FragmentPatterns => new[] { "mesh/*", "materials" };

    /// <summary>fragment に応じて vertex/index buffer (borrowed) または materials buffer (owned) を返す。</summary>
    public Task<GpuBuffer> RunAsync(SceneAssets assets, ResourceUri uri, LoadContext ctx)
    {
        string frag = uri.Fragment;
        if (string.IsNullOrEmpty(frag))
            throw new InvalidOperationException($"GltfBufferStep: URI '{uri}' には fragment が必要です。");

        string[] parts = frag.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // #mesh/N/vertex or #mesh/N/index → 登場順 primitive の GPU buffer を borrowed で返す
        if (parts.Length >= 3 && parts[0] == "mesh")
        {
            if (!int.TryParse(parts[1], out int primIdx))
                throw new InvalidOperationException($"GltfBufferStep: mesh index が数値でない '{parts[1]}'。");
            if (primIdx < 0 || primIdx >= assets.OrderedPrimitives.Count)
                throw new InvalidOperationException($"GltfBufferStep: mesh[{primIdx}] は存在しない (OrderedPrimitives.Count={assets.OrderedPrimitives.Count})。");
            var prim = assets.OrderedPrimitives[primIdx];
            var gpu = assets.Primitives[prim];
            return Task.FromResult(parts[2] switch
            {
                "vertex" => gpu.VertexBuffer,
                "index" => gpu.IndexBuffer,
                _ => throw new InvalidOperationException($"GltfBufferStep: 不明な mesh part '{parts[2]}' (vertex|index)。"),
            });
        }

        // #materials → MaterialGpuData 配列を作成 owned
        if (parts.Length == 1 && parts[0] == "materials")
        {
            int n = Math.Max(1, assets.MaterialArray.Count);
            var data = new MaterialGpuData[n];
            for (int i = 0; i < assets.MaterialArray.Count; i++)
            {
                var m = assets.MaterialArray[i];
                uint texIdx = 0, sampIdx = 0, flags = 0;
                if (m.BaseColorTexture is not null)
                {
                    texIdx = m.BaseColorTexture.BindlessIndex;
                    sampIdx = assets.DefaultSampler?.BindlessIndex ?? 0;
                    flags = MaterialGpuData.FlagHasTexture;
                }
                data[i] = new MaterialGpuData
                {
                    BaseColor = m.BaseColor,
                    BaseColorTexIndex = texIdx,
                    SamplerIndex = sampIdx,
                    Flags = flags,
                };
            }
            var buf = _device.Malloc((ulong)(n * MaterialGpuData.Stride), GpuMemoryKind.HostMapped);
            data.AsSpan().CopyTo(buf.Span<MaterialGpuData>(n));
            ctx.MarkOwned();
            return Task.FromResult(buf);
        }

        throw new InvalidOperationException($"GltfBufferStep: 未対応 fragment '{frag}'。");
    }
}

/// <summary>
/// <c>foo.glb#material/N/baseColor</c> で SceneAssets 内の baseColor texture を borrowed 取得する step。
/// N は MaterialArray の index。
/// </summary>
public sealed class MaterialTextureStep : IResourceStep<SceneAssets, GpuTexture>
{
    /// <summary>CPU executor で実行 (参照を返すだけで GPU 操作なし)。</summary>
    public Executor Executor => Executor.Cpu;
    /// <summary>処理対象の fragment パターン。</summary>
    public IEnumerable<string> FragmentPatterns => new[] { "material/*" };

    /// <summary>fragment で指定された material の baseColor texture を borrowed で返す。</summary>
    public Task<GpuTexture> RunAsync(SceneAssets assets, ResourceUri uri, LoadContext ctx)
    {
        string frag = uri.Fragment;
        if (string.IsNullOrEmpty(frag))
            throw new InvalidOperationException($"MaterialTextureStep: URI '{uri}' には fragment が必要です。");
        string[] parts = frag.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != "material")
            throw new InvalidOperationException($"MaterialTextureStep: 不正な fragment '{frag}'。");
        if (!int.TryParse(parts[1], out int matIdx))
            throw new InvalidOperationException($"MaterialTextureStep: material index が数値でない '{parts[1]}'。");
        if (matIdx < 0 || matIdx >= assets.MaterialArray.Count)
            throw new InvalidOperationException($"MaterialTextureStep: material[{matIdx}] は存在しない。");
        var mat = assets.MaterialArray[matIdx];
        return parts[2] switch
        {
            "baseColor" => Task.FromResult(mat.BaseColorTexture
                ?? throw new InvalidOperationException($"material[{matIdx}] に baseColor texture がない。")),
            _ => throw new InvalidOperationException($"MaterialTextureStep: 不明な material part '{parts[2]}'。"),
        };
    }
}
