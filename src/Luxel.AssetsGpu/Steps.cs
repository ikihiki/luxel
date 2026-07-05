using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// <see cref="AssetTexture"/> → <see cref="GpuTexture"/>。ctor で <see cref="GpuDevice"/> と
/// <see cref="AssetGpuRegistry"/> を DI で受け取り、registry の cache 経由で dedup upload。
/// </summary>
public sealed class AssetTextureToGpuStep(GpuDevice device, AssetGpuRegistry registry)
    : IResourceStep<AssetTexture, GpuTexture>
{
    public Executor Executor => Executor.Gpu;
    public Task<GpuTexture> RunAsync(AssetTexture input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(registry.Register(input));
}

/// <summary><see cref="AssetSampler"/> → <see cref="GpuSampler"/> (Registry 経由)。</summary>
public sealed class AssetSamplerToGpuStep(GpuDevice device, AssetGpuRegistry registry)
    : IResourceStep<AssetSampler, GpuSampler>
{
    public Executor Executor => Executor.Gpu;
    public Task<GpuSampler> RunAsync(AssetSampler input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(registry.Register(input));
}

/// <summary><see cref="AssetMaterial"/> → <see cref="GpuMaterial"/> (Registry 経由、
/// BaseColorTexture 等のネストした Asset* は Registry が再帰 upload)。</summary>
public sealed class AssetMaterialToGpuStep(GpuDevice device, AssetGpuRegistry registry)
    : IResourceStep<AssetMaterial, GpuMaterial>
{
    public Executor Executor => Executor.Gpu;
    public Task<GpuMaterial> RunAsync(AssetMaterial input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(registry.Register(input));
}

/// <summary><see cref="AssetMesh"/> → <see cref="GpuMesh"/> (Registry 経由、primitive の Material も再帰)。</summary>
public sealed class AssetMeshToGpuStep(GpuDevice device, AssetGpuRegistry registry)
    : IResourceStep<AssetMesh, GpuMesh>
{
    public Executor Executor => Executor.Gpu;
    public Task<GpuMesh> RunAsync(AssetMesh input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(registry.Register(input));
}

/// <summary><see cref="AssetSkin"/> → <see cref="GpuSkin"/> (Registry 経由)。</summary>
public sealed class AssetSkinToGpuStep(GpuDevice device, AssetGpuRegistry registry)
    : IResourceStep<AssetSkin, GpuSkin>
{
    public Executor Executor => Executor.Gpu;
    public Task<GpuSkin> RunAsync(AssetSkin input, ResourceUri uri, LoadContext ctx)
        => Task.FromResult(registry.Register(input));
}
