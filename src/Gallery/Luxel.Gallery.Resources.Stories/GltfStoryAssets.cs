using Luxel.Assets;
using Luxel.Controls;
using Luxel.Resources;
using Luxel.UI;

namespace Luxel.Gallery.Stories;

/// <summary>Gallery host が所有する <see cref="ResourceSystem"/> から glTF story asset を取得する。</summary>
public static class GltfStoryAssets
{
    public const string Box = "tools/khronos-samples/Box/Box.gltf";
    public const string AnimatedBox = "tools/khronos-samples/BoxAnimated/BoxAnimated.glb";
    public const string RiggedSimple = "tools/khronos-samples/RiggedSimple/RiggedSimple.glb";

    /// <summary>
    /// Resource handle の準備完了後に scene を構築する。I/O は host-owned ResourceSystem が非同期に行い、
    /// GpuView callback は未完了 task を同期 block せず Loading を返す。
    /// </summary>
    internal static Widget View(
        StoryContext context,
        string uri,
        Func<AssetDocument, GpuSceneBase> createScene,
        bool animated)
    {
        ResourceHandle<AssetDocument> document = context.ScopedResources.Load<AssetDocument>(uri);
        Signal<ResourceState> state = context.Observe(document);
        GpuSceneBase? scene = null;
        int sceneVersion = -1;

        return Luxel.Controls.Kit.GpuView(256, 256,
            (device, surface, time) =>
            {
                ResourceState snapshot = state.Value;
                if (!snapshot.HasValue)
                    return snapshot.Status == ResourceStatus.Failed
                        ? GpuViewRenderResult.Failed
                        : GpuViewRenderResult.Loading;

                if (scene is null || sceneVersion != snapshot.Version)
                {
                    scene?.Dispose();
                    scene = createScene(document.Value);
                    sceneVersion = snapshot.Version;
                }

                return scene.Render(device, surface, time);
            },
            animated: animated,
            dispose: () => scene?.Dispose());
    }
}
