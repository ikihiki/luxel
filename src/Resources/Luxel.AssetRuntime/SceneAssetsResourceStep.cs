using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.AssetRuntime;

/// <summary>AssetDocumentをECSとGPUのSceneAssetsへ展開するformat非依存Resource step。</summary>
public sealed class SceneAssetsResourceStep(GpuDevice device, Luxel.Ecs.World world)
    : IResourceStep<AssetDocument, SceneAssets>
{
    public Task<SceneAssets> RunAsync(AssetDocument input, ResourceUri uri, LoadContext context)
        => Task.FromResult(SceneBuilder.Build(world, input, device));
}
