using Luxel.AssetsGpu;
using Luxel.Graphics;
using Luxel.Resources;

namespace Luxel.Tests;

internal static class ResourceTestSystem
{
    public static ResourceSystem Create(
        IEnumerable<IResourceSource>? sources = null,
        IEnumerable<IResourceStep>? steps = null,
        Action<ResourceSystemBuilder, ResourceSystemDefaultHandles>? configure = null)
    {
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaultHandles handles = ResourceSystemDefaults.AddCore(builder);
        if (sources is not null)
            foreach (IResourceSource source in sources)
                builder.Sources.Add(source).RunOn(handles.IoDomain).ManagedBy(handles.IoManager).Register();
        if (steps is not null)
            foreach (IResourceStep step in steps)
                builder.Steps.Add(step).RunOn(handles.CpuDomain).ManagedBy(handles.CpuManager).Register();
        configure?.Invoke(builder, handles);
        return builder.Build();
    }

    public static ResourceSystem CreateGpu(GpuDevice device, out AssetGpuResourceSystemRegistration registration)
    {
        var builder = new ResourceSystemBuilder();
        ResourceSystemDefaults.AddCore(builder);
        registration = builder.AddAssetGpu(device);
        return builder.Build();
    }
}
