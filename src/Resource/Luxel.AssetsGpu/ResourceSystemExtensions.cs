using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

/// <summary>
/// <see cref="ResourceSystem"/> に AssetsGpu の Step 一式を追加登録するヘルパ + GPU リソース publish 用の extension methods。
/// GPU Step は device-bound <see cref="AssetGpuRegistry"/> をコンストラクタで受け取る。
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
            new AssetTextureToGpuStep(registry),
            new AssetSamplerToGpuStep(registry),
            new AssetMaterialToGpuStep(registry),
            new AssetMeshToGpuStep(registry),
            new AssetSkinToGpuStep(registry),
            new GpuPipelineCreationStep(registry),
            new GpuTextureCreationStep(registry),
            new GpuSamplerCreationStep(registry),
            new GpuBufferCreationStep(registry),
            new Float32ArrayToGpuBufferStep(registry),
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
        var installation = new AssetGpuInstallation(device, registry);
        AddAssetGpuSteps(resources, device, registry);
        resources.SetDeferredDisposeIdleHook(installation.WaitIdle);
        return installation;
    }

    private static void AddAssetGpuSteps(ResourceSystem resources, GpuDevice device, AssetGpuRegistry registry)
    {
        // Use the generic registration path so browser-WASM trimming does not remove interface
        // property metadata required by the legacy reflection-based adapter.
        resources.AddStep<CpuImage, GpuTexture>(new TextureUploaderStep(device));
        resources.AddStep<AssetTexture, GpuTexture>(new AssetTextureToGpuStep(registry));
        resources.AddStep<AssetSampler, GpuSampler>(new AssetSamplerToGpuStep(registry));
        resources.AddStep<AssetMaterial, GpuMaterial>(new AssetMaterialToGpuStep(registry));
        resources.AddStep<AssetMesh, GpuMesh>(new AssetMeshToGpuStep(registry));
        resources.AddStep<AssetSkin, GpuSkin>(new AssetSkinToGpuStep(registry));
        resources.AddStep<GpuPipelineRequest, GpuPipeline>(new GpuPipelineCreationStep(registry));
        resources.AddStep<GpuTextureRequest, GpuTexture>(new GpuTextureCreationStep(registry));
        resources.AddStep<GpuSamplerRequest, GpuSampler>(new GpuSamplerCreationStep(registry));
        resources.AddStep<GpuBufferRequest, GpuBuffer>(new GpuBufferCreationStep(registry));
        resources.AddStep<float[], GpuBuffer>(new Float32ArrayToGpuBufferStep(registry));
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
