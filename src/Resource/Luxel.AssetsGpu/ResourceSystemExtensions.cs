using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

public sealed class AssetGpuResourceSystemOptions
{
    public string DomainId { get; set; } = "asset.gpu";
    public string ManagerId { get; set; } = "asset.gpu-manager";
}

public readonly record struct AssetGpuResourceSystemRegistration(
    ResourceExecutionDomainHandle Domain,
    ResourceManagerHandle Manager,
    AssetGpuRegistry Registry);

/// <summary>Registers the AssetsGpu domain, manager, type bindings, and steps before ResourceSystem build.</summary>
public static class ResourceSystemExtensions
{
    public static AssetGpuResourceSystemRegistration AddAssetGpu(
        this ResourceSystemBuilder builder,
        GpuDevice device,
        Action<AssetGpuResourceSystemOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(device);
        var options = new AssetGpuResourceSystemOptions();
        configure?.Invoke(options);

        ResourceExecutionDomainHandle domain = builder.Domains.Add(options.DomainId).UseSerial().Register();
        var registry = new AssetGpuRegistry(device);
        ResourceManagerHandle manager = builder.Managers.Add(options.ManagerId)
            .RunOn(domain)
            .Use(context => new AssetGpuResourceManager(context.Id, device, registry))
            .Register();

        BindTypes(builder, manager);
        RegisterSteps(builder, device, registry, domain, manager);
        return new(domain, manager, registry);
    }

    private static void BindTypes(ResourceSystemBuilder builder, ResourceManagerHandle manager)
    {
        builder.Managers.Manage<GpuTexture>().With(manager).Register();
        builder.Managers.Manage<GpuSampler>().With(manager).Register();
        builder.Managers.Manage<GpuMaterial>().With(manager).Register();
        builder.Managers.Manage<GpuMesh>().With(manager).Register();
        builder.Managers.Manage<GpuSkin>().With(manager).Register();
        builder.Managers.Manage<GpuPipeline>().With(manager).Register();
        builder.Managers.Manage<GpuBuffer>().With(manager).Register();
    }

    private static void RegisterSteps(
        ResourceSystemBuilder builder,
        GpuDevice device,
        AssetGpuRegistry registry,
        ResourceExecutionDomainHandle domain,
        ResourceManagerHandle manager)
    {
        builder.Steps.Add<CpuImage, GpuTexture>(new TextureUploaderStep(device)).RunOn(domain).ManagedBy(manager).Register();
        builder.Steps.Add<AssetTexture, GpuTexture>(new AssetTextureToGpuStep(registry)).RunOn(domain).ManagedBy(manager).Borrowed().Register();
        builder.Steps.Add<AssetSampler, GpuSampler>(new AssetSamplerToGpuStep(registry)).RunOn(domain).ManagedBy(manager).Borrowed().Register();
        builder.Steps.Add<AssetMaterial, GpuMaterial>(new AssetMaterialToGpuStep(registry)).RunOn(domain).ManagedBy(manager).Borrowed().Register();
        builder.Steps.Add<AssetMesh, GpuMesh>(new AssetMeshToGpuStep(registry)).RunOn(domain).ManagedBy(manager).Borrowed().Register();
        builder.Steps.Add<AssetSkin, GpuSkin>(new AssetSkinToGpuStep(registry)).RunOn(domain).ManagedBy(manager).Borrowed().Register();
        builder.Steps.Add<GpuPipelineRequest, GpuPipeline>(new GpuPipelineCreationStep(registry)).RunOn(domain).ManagedBy(manager).Register();
        builder.Steps.Add<GpuTextureRequest, GpuTexture>(new GpuTextureCreationStep(registry)).RunOn(domain).ManagedBy(manager).Register();
        builder.Steps.Add<GpuSamplerRequest, GpuSampler>(new GpuSamplerCreationStep(registry)).RunOn(domain).ManagedBy(manager).Register();
        builder.Steps.Add<GpuBufferRequest, GpuBuffer>(new GpuBufferCreationStep(registry)).RunOn(domain).ManagedBy(manager).Register();
        builder.Steps.Add<float[], GpuBuffer>(new Float32ArrayToGpuBufferStep(registry)).RunOn(domain).ManagedBy(manager).Register();
    }
}

internal sealed class AssetGpuResourceManager : IResourceManager
{
    private readonly GpuDevice _device;
    private readonly AssetGpuRegistry _registry;
    private long _adopted;
    private long _retired;

    public AssetGpuResourceManager(ResourceManagerId id, GpuDevice device, AssetGpuRegistry registry)
    {
        Id = id;
        _device = device;
        _registry = registry;
    }

    public ResourceManagerId Id { get; }
    public ResourceManagerCapabilities Capabilities => ResourceManagerCapabilities.AsyncRetirement;

    public ValueTask<ResourceManagementRecord> AdoptAsync(object value, ResourceAdoptionContext context)
    {
        Interlocked.Increment(ref _adopted);
        return ValueTask.FromResult(new ResourceManagementRecord(Id, context.Ownership, Context: context.ManagementContext));
    }

    public async ValueTask RetireAsync(object value, ResourceManagementRecord record, ResourceRetireReason reason,
        CancellationToken cancellationToken = default)
    {
        if (record.Ownership == ResourceOwnership.Owned)
        {
            await _device.MainQueue.WaitIdleAsync(cancellationToken).ConfigureAwait(false);
            if (value is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (value is IDisposable disposable) disposable.Dispose();
        }
        Interlocked.Increment(ref _retired);
    }

    public ResourceManagerSnapshot CaptureSnapshot() => new(Id, Interlocked.Read(ref _adopted), Interlocked.Read(ref _retired), 0, 0);
    public ValueTask ShutdownAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public async ValueTask DisposeAsync()
    {
        await _device.MainQueue.WaitIdleAsync().ConfigureAwait(false);
        _registry.Dispose();
    }
}
