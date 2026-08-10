using Luxel.Platform;

namespace Luxel.Resources;

public sealed class ResourceSystemDefaultOptions
{
    public string IoDomainId { get; set; } = "resource.io";
    public string CpuDomainId { get; set; } = "resource.cpu";
    public string IoManagerId { get; set; } = "resource.io-manager";
    public string CpuManagerId { get; set; } = "resource.cpu-manager";
    public int IoConcurrency { get; set; } = Math.Max(4, Environment.ProcessorCount);
    public int CpuConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount);
}

public readonly record struct ResourceSystemDefaultHandles(
    ResourceExecutionDomainHandle IoDomain,
    ResourceExecutionDomainHandle CpuDomain,
    ResourceManagerHandle IoManager,
    ResourceManagerHandle CpuManager);

public static class ResourceSystemDefaults
{
    public static ResourceSystemDefaultHandles AddCore(ResourceSystemBuilder builder, Action<ResourceSystemDefaultOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new ResourceSystemDefaultOptions();
        configure?.Invoke(options);
        ResourceExecutionDomainHandle ioDomain = builder.Domains.Add(options.IoDomainId).UseThreadPool(options.IoConcurrency).Register();
        ResourceExecutionDomainHandle cpuDomain = builder.Domains.Add(options.CpuDomainId).UseThreadPool(options.CpuConcurrency).Register();
        ResourceManagerHandle ioManager = builder.Managers.Add(options.IoManagerId).RunOn(ioDomain).UseIo().Register();
        ResourceManagerHandle cpuManager = builder.Managers.Add(options.CpuManagerId).RunOn(cpuDomain).UseCpu().AsDefault().Register();
        builder.Managers.Manage<byte[]>().With(ioManager).Register();
        return new(ioDomain, cpuDomain, ioManager, cpuManager);
    }

    public static void AddBuiltinSources(ResourceSystemBuilder builder, ResourceSystemDefaultHandles handles,
        string? assetRoot = null, IVirtualFileSystem? vfs = null, HttpClient? http = null)
    {
        vfs ??= new PhysicalFileSystem(assetRoot ?? AppContext.BaseDirectory);
        http ??= new HttpClient();
        builder.Sources.Add(new FileSource(vfs)).RunOn(handles.IoDomain).ManagedBy(handles.IoManager).Register();
        builder.Sources.Add(new HttpSource(http)).RunOn(handles.IoDomain).ManagedBy(handles.IoManager).Register();
    }

    public static void AddBuiltinSourcesForWeb(ResourceSystemBuilder builder, ResourceSystemDefaultHandles handles,
        IPlatformFileReader files, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        http ??= new HttpClient();
        builder.Sources.Add(new PlatformFileSource(files)).RunOn(handles.IoDomain).ManagedBy(handles.IoManager).Register();
        builder.Sources.Add(new HttpSource(http)).RunOn(handles.IoDomain).ManagedBy(handles.IoManager).Register();
    }

    public static void AddBuiltinSteps(ResourceSystemBuilder builder, ResourceSystemDefaultHandles handles)
        => builder.Steps.Add<byte[], CpuImage>(new TexDecoder()).RunOn(handles.CpuDomain).ManagedBy(handles.CpuManager).ForExtensions(".tex").Register();

    public static ResourceSystemBuilder CreateBuilder(Action<ResourceSystemDefaultOptions>? configure = null)
    {
        var builder = new ResourceSystemBuilder();
        AddCore(builder, configure);
        return builder;
    }
}
