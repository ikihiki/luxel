using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// <see cref="AssetTexture"/> → <see cref="GpuTexture"/>。ctor で device-bound
/// <see cref="AssetGpuRegistry"/> を受け取り、registry の cache 経由で dedup upload。
/// </summary>
public sealed class AssetTextureToGpuStep(AssetGpuRegistry registry)
    : IResourceStep<AssetTexture, GpuTexture>
{
    public Executor Executor => Executor.External;
    public Task<GpuTexture> RunAsync(AssetTexture input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(registry.Register(input));
}

/// <summary><see cref="AssetSampler"/> → <see cref="GpuSampler"/> (Registry 経由)。</summary>
public sealed class AssetSamplerToGpuStep(AssetGpuRegistry registry)
    : IResourceStep<AssetSampler, GpuSampler>
{
    public Executor Executor => Executor.External;
    public Task<GpuSampler> RunAsync(AssetSampler input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(registry.Register(input));
}

/// <summary><see cref="AssetMaterial"/> → <see cref="GpuMaterial"/> (Registry 経由、
/// BaseColorTexture 等のネストした Asset* は Registry が再帰 upload)。</summary>
public sealed class AssetMaterialToGpuStep(AssetGpuRegistry registry)
    : IResourceStep<AssetMaterial, GpuMaterial>
{
    public Executor Executor => Executor.External;
    public Task<GpuMaterial> RunAsync(AssetMaterial input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(registry.Register(input));
}

/// <summary><see cref="AssetMesh"/> → <see cref="GpuMesh"/> (Registry 経由、primitive の Material も再帰)。</summary>
public sealed class AssetMeshToGpuStep(AssetGpuRegistry registry)
    : IResourceStep<AssetMesh, GpuMesh>
{
    public Executor Executor => Executor.External;
    public Task<GpuMesh> RunAsync(AssetMesh input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(registry.Register(input));
}

/// <summary><see cref="AssetSkin"/> → <see cref="GpuSkin"/> (Registry 経由)。</summary>
public sealed class AssetSkinToGpuStep(AssetGpuRegistry registry)
    : IResourceStep<AssetSkin, GpuSkin>
{
    public Executor Executor => Executor.External;
    public Task<GpuSkin> RunAsync(AssetSkin input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(registry.Register(input));
}
