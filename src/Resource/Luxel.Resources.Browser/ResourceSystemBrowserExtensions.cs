using System.Runtime.Versioning;

namespace Luxel.Resources.Browser;

[SupportedOSPlatform("browser")]
public static class ResourceSystemBrowserExtensions
{
    /// <summary>Runs this domain cooperatively on the browser event loop that owns the resource system.</summary>
    public static ResourceDomainRegistrationBuilder UseBrowserOwnerContext(this ResourceDomainRegistrationBuilder registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        SynchronizationContext ownerContext = SynchronizationContext.Current
            ?? BrowserEventLoopSynchronizationContext.Instance;
        var capabilities = new ResourceExecutionDomainCapabilities(
            1, ResourceThreadAffinity.HostThread, ResourceProgressModel.Cooperative);
        return registration.UseFactory(
            context => new BrowserResourceExecutionDomain(context.Id, ownerContext, context.Capabilities.OperationBudget),
            capabilities);
    }

    /// <summary>Adds the standard resource domains using cooperative browser owner-context scheduling.</summary>
    public static ResourceSystemDefaultHandles AddBrowserCore(
        this ResourceSystemBuilder builder,
        Action<ResourceSystemDefaultOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new ResourceSystemDefaultOptions();
        configure?.Invoke(options);
        ResourceExecutionDomainHandle ioDomain = builder.Domains.Add(options.IoDomainId).UseBrowserOwnerContext().Register();
        ResourceExecutionDomainHandle cpuDomain = builder.Domains.Add(options.CpuDomainId).UseBrowserOwnerContext().Register();
        ResourceManagerHandle ioManager = builder.Managers.Add(options.IoManagerId).RunOn(ioDomain).UseIo().Register();
        ResourceManagerHandle cpuManager = builder.Managers.Add(options.CpuManagerId).RunOn(cpuDomain).UseCpu().AsDefault().Register();
        builder.Managers.Manage<byte[]>().With(ioManager).Register();
        return new(ioDomain, cpuDomain, ioManager, cpuManager);
    }
}
