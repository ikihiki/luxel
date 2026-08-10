using Luxel.Assets;
using Luxel.Resources;

namespace Luxel.AssetsGpu;

public sealed class GpuResourceInstallationOptions
{
    public string DomainId { get; set; } = "gpu.device-0.create";
    public string ManagerId { get; set; } = "gpu.device-0";
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public ulong DeviceGeneration { get; set; } = 1;
    public long SoftBudgetBytes { get; set; } = long.MaxValue;
    public long HardBudgetBytes { get; set; } = long.MaxValue;
    public Action<ResourceDomainRegistrationBuilder>? ConfigureDomain { get; set; }
}

public readonly record struct AssetGpuResourceSystemRegistration(GpuResourceManagerHandle Gpu)
{
    public ResourceExecutionDomainHandle Domain => Gpu.CreateDomain;
    public ResourceManagerHandle Manager => Gpu.Manager;
    public AssetGpuRegistry Registry => Gpu.Registry;
}

/// <summary>Build-time GPU manager and Asset GPU capability composition.</summary>
public static class ResourceSystemExtensions
{
    public static GpuResourceManagerHandle InstallGpuResources(this ResourceSystemBuilder builder, GpuDevice device,
        Action<GpuResourceInstallationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(device);
        var options = new GpuResourceInstallationOptions();
        configure?.Invoke(options);
        if (options.DeviceGeneration == 0) throw new InvalidOperationException("GPU device generation must be greater than zero.");
        if (options.SoftBudgetBytes < 0 || options.HardBudgetBytes < options.SoftBudgetBytes)
            throw new InvalidOperationException("GPU budgets must satisfy 0 <= soft <= hard.");

        var generation = new GpuResourceGeneration(device, new(options.DeviceId, options.DeviceGeneration));
        var registry = new AssetGpuRegistry(device, generation.Identity);
        var managerOptions = new GpuResourceManagerOptions
        {
            SoftBudgetBytes = options.SoftBudgetBytes,
            HardBudgetBytes = options.HardBudgetBytes,
        };

        GpuResourceManagerHandle? installation = null;
        ResourceDomainRegistrationBuilder domainBuilder = builder.Domains.Add(options.DomainId);
        if (options.ConfigureDomain is null)
        {
            domainBuilder.UseFactory(
                context => new SerialResourceExecutionDomain(context.Id),
                new(1, ResourceThreadAffinity.DeviceThread, ResourceProgressModel.Serialized));
        }
        else
        {
            options.ConfigureDomain(domainBuilder);
        }
        ResourceExecutionDomainHandle domain = domainBuilder
            .Decorate((context, inner) =>
            {
                var value = new GpuResourceExecutionDomain(inner, context.Capabilities);
                installation!.Attach(value);
                return value;
            })
            .Register();
        ResourceManagerHandle manager = builder.Managers.Add(options.ManagerId)
            .RunOn(domain)
            .Use(context =>
            {
                var value = new GpuResourceManager(context.Id, generation, installation!.Policies, managerOptions, registry);
                installation.Attach(value);
                return value;
            })
            .ValidateManagedTypes(type => installation!.Validate(type))
            .Register();
        installation = new(domain, manager, registry, generation, managerOptions);
        return installation;
    }

    public static AssetGpuResourceSystemRegistration AddAssetGpu(this ResourceSystemBuilder builder, GpuDevice device,
        Action<GpuResourceInstallationOptions>? configure = null)
    {
        GpuResourceManagerHandle gpu = builder.InstallGpuResources(device, configure);
        RegisterBuiltInPolicies(builder, gpu);
        RegisterSteps(builder, gpu);
        return new(gpu);
    }

    public static void RegisterBuiltInGpuPolicies(this ResourceSystemBuilder builder, GpuResourceManagerHandle gpu)
        => RegisterBuiltInPolicies(builder, gpu);

    private static void RegisterBuiltInPolicies(ResourceSystemBuilder builder, GpuResourceManagerHandle gpu)
    {
        gpu.Manage<GpuTexture>(builder)
            .DescribeAllocation(texture =>
            {
                long bytes = checked((long)texture.Width * texture.Height * 4);
                return new("", bytes, bytes, bytes, "device-local", Pinned: true);
            })
            .WithIndexSpace("sampled-texture")
            .RetireAsync((value, _) => { value.Dispose(); return ValueTask.CompletedTask; })
            .Register();
        gpu.Manage<GpuSampler>(builder)
            .WithIndexSpace("sampler")
            .RetireAsync((value, _) => { value.Dispose(); return ValueTask.CompletedTask; })
            .Register();
        gpu.Manage<GpuPipeline>(builder)
            .RetireAsync((value, _) => { value.Dispose(); return ValueTask.CompletedTask; })
            .Register();
        gpu.Manage<GpuBuffer>(builder)
            .DescribeAllocation(buffer => new("", checked((long)buffer.Size), checked((long)buffer.Size),
                checked((long)buffer.Size), buffer.IsMapped ? "upload" : "device-local", Pinned: true))
            .WithIndexSpace("storage-buffer")
            .RetireAsync((value, _) => { value.Dispose(); return ValueTask.CompletedTask; })
            .Register();
        gpu.Manage<GpuMesh>(builder)
            .RetireAsync((value, _) => { value.Dispose(); return ValueTask.CompletedTask; })
            .Register();
        gpu.Manage<GpuSkin>(builder)
            .RetireAsync((value, _) => { value.Dispose(); return ValueTask.CompletedTask; })
            .FlushAsync((value, _) => { value.JointMatrices?.FlushImmediate(); return ValueTask.CompletedTask; })
            .Register();
        gpu.Manage<GpuMaterial>(builder).Register();
    }

    private static void RegisterSteps(ResourceSystemBuilder builder, GpuResourceManagerHandle gpu)
    {
        AssetGpuRegistry registry = gpu.Registry;
        ResourceExecutionDomainHandle domain = gpu.CreateDomain;
        ResourceManagerHandle manager = gpu.Manager;
        builder.Steps.Add<CpuImage, GpuTexture>(new TextureUploaderStep(registry)).RunOn(domain).ManagedBy(manager).Register();
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
