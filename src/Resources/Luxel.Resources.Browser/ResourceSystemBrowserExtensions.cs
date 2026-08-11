using System.Runtime.Versioning;

namespace Luxel.Resources.Browser;

[SupportedOSPlatform("browser")]
public static class ResourceSystemBrowserExtensions
{
    /// <summary>Runs this domain through an independent cooperative FIFO scheduler on browser-WASM.</summary>
    public static ResourceDomainRegistrationBuilder UseBrowserCooperative(this ResourceDomainRegistrationBuilder registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var capabilities = new ResourceExecutionDomainCapabilities(
            1, ResourceThreadAffinity.HostThread, ResourceProgressModel.Cooperative);
        return registration.UseFactory(
            context => new BrowserResourceExecutionDomain(context.Id, context.Capabilities.OperationBudget),
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
        ResourceExecutionDomainHandle ioDomain = builder.Domains.Add(options.IoDomainId).UseBrowserCooperative().Register();
        ResourceExecutionDomainHandle cpuDomain = builder.Domains.Add(options.CpuDomainId).UseBrowserCooperative().Register();
        ResourceManagerHandle ioManager = builder.Managers.Add(options.IoManagerId).RunOn(ioDomain).UseIo().Register();
        ResourceManagerHandle cpuManager = builder.Managers.Add(options.CpuManagerId).RunOn(cpuDomain).UseCpu().AsDefault().Register();
        builder.Managers.Manage<byte[]>().With(ioManager).Register();
        return new(ioDomain, cpuDomain, ioManager, cpuManager);
    }
}
