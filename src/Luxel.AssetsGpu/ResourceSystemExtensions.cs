using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// <see cref="ResourceSystem"/> に AssetsGpu の Step 一式を追加登録するヘルパ + GPU リソース publish 用の extension methods。
/// Step は <see cref="AssetGpuRegistry"/> と <see cref="GpuDevice"/> をコンストラクタで受け取る。
/// </summary>
public static class ResourceSystemExtensions
{
    /// <summary>AssetsGpu 系 Step 一式 + <see cref="TextureUploaderStep"/> を構築して返す。
    /// <c>new ResourceSystem(steps: CreateAssetGpuSteps(device, out var registry))</c> のように
    /// ResourceSystem のコンストラクタに直接渡せる。</summary>
    public static IResourceStep[] CreateAssetGpuSteps(GpuDevice device, out AssetGpuRegistry registry)
    {
        registry = new AssetGpuRegistry(device);
        return new IResourceStep[]
        {
            new TextureUploaderStep(device),
            new AssetTextureToGpuStep(device, registry),
            new AssetSamplerToGpuStep(device, registry),
            new AssetMaterialToGpuStep(device, registry),
            new AssetMeshToGpuStep(device, registry),
            new AssetSkinToGpuStep(device, registry),
        };
    }

    /// <summary>既存 <see cref="ResourceSystem"/> に AssetsGpu 系 Step 一式を <see cref="ResourceSystem.AddStep"/> で追加。
    /// GPU deferred dispose 用 hook もあわせて設定する。</summary>
    /// <remarks>
    /// Compatibility API: the caller owns the returned registry and must dispose it before the device.
    /// New code can use <see cref="InstallAssetGpuLifecycle"/> to make that ownership explicit.
    /// </remarks>
    public static AssetGpuRegistry InstallAssetGpu(this ResourceSystem resources, GpuDevice device)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(device);
        var registry = new AssetGpuRegistry(device);
        AddAssetGpuSteps(resources, device, registry);
        resources.SetDeferredDisposeIdleHook(() => device.MainQueue.WaitIdle());
        return registry;
    }

    /// <summary>
    /// Installs AssetsGpu steps and returns an explicit lifecycle token that owns the
    /// <see cref="AssetGpuRegistry"/> and supplies the deferred-dispose queue-idle hook.
    /// Dispose the token before disposing <paramref name="device"/>.
    /// </summary>
    public static AssetGpuInstallation InstallAssetGpuLifecycle(this ResourceSystem resources, GpuDevice device)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(device);
        var registry = new AssetGpuRegistry(device);
        AssetGpuInstallation installation;
        try
        {
            installation = new AssetGpuInstallation(resources, device, registry);
        }
        catch
        {
            device.MainQueue.WaitIdle();
            registry.Dispose();
            throw;
        }
        AddAssetGpuSteps(resources, device, registry);
        resources.SetDeferredDisposeIdleHook(installation.WaitIdle);
        return installation;
    }

    private static void AddAssetGpuSteps(ResourceSystem resources, GpuDevice device, AssetGpuRegistry registry)
    {
        resources.AddStep(new TextureUploaderStep(device));
        resources.AddStep(new AssetTextureToGpuStep(device, registry));
        resources.AddStep(new AssetSamplerToGpuStep(device, registry));
        resources.AddStep(new AssetMaterialToGpuStep(device, registry));
        resources.AddStep(new AssetMeshToGpuStep(device, registry));
        resources.AddStep(new AssetSkinToGpuStep(device, registry));
    }

    // ==================== Publish 系 GPU リソース ====================

    /// <summary>差替可能 GPU バッファを uri で publish。</summary>
    public static ResourceHandle<RenderBuffer<T>> PublishRenderBuffer<T>(
        this ResourceSystem resources, GpuDevice device, string uri, int count) where T : unmanaged
    {
        var rb = new RenderBuffer<T>(device, count, uri);
        var handle = resources.Publish(uri, rb);
        resources.RegisterPumpFlush(handle, rb.Flush);
        return handle;
    }

    /// <summary>差替可能 GPU テクスチャを uri で publish。</summary>
    public static ResourceHandle<RenderTarget> PublishRenderTarget(
        this ResourceSystem resources, GpuDevice device, string uri, uint width, uint height,
        GpuFormat format = GpuFormat.Rgba8Unorm)
    {
        var rt = new RenderTarget(device, width, height, format);
        var handle = resources.Publish(uri, rt);
        resources.RegisterPumpFlush(handle, rt.Flush);
        return handle;
    }
}
