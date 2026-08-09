using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Graphics.MessagePipe;

/// <summary>Publishes transport-neutral graphics lifecycle messages through typed MessagePipe publishers.</summary>
public sealed class MessagePipeGpuLifecycleSink(
    IPublisher<GpuDeviceLifecycleEvent> devicePublisher,
    IPublisher<GpuValidationEvent> validationPublisher,
    IPublisher<GpuSurfaceLifecycleEvent> surfacePublisher) : IGpuLifecycleSink
{
    public void Publish(GpuDeviceLifecycleEvent message) => devicePublisher.Publish(message);
    public void Publish(GpuValidationEvent message) => validationPublisher.Publish(message);
    public void Publish(GpuSurfaceLifecycleEvent message) => surfacePublisher.Publish(message);
}

/// <summary>Owns the callback queue and forwards it to MessagePipe on the calling frame/Pump thread.</summary>
public sealed class GpuLifecycleMessagePump(
    GpuLifecycleEventQueue queue,
    MessagePipeGpuLifecycleSink destination)
{
    public int Pump(int maximumCount = int.MaxValue) => queue.Pump(destination, maximumCount);
}

public static class GpuLifecycleMessagePipeServiceCollectionExtensions
{
    private sealed class RegistrationMarker;

    /// <summary>Adds MessagePipe and the queued Graphics lifecycle adapter once.</summary>
    public static IServiceCollection AddGpuLifecycleMessagePipe(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(RegistrationMarker))) return services;

        services.AddSingleton<RegistrationMarker>();
        services.AddMessagePipe();
        services.AddSingleton<GpuLifecycleEventQueue>();
        services.AddSingleton<IGpuLifecycleSink>(static provider => provider.GetRequiredService<GpuLifecycleEventQueue>());
        services.AddSingleton<MessagePipeGpuLifecycleSink>();
        services.AddSingleton<GpuLifecycleMessagePump>();
        return services;
    }
}
