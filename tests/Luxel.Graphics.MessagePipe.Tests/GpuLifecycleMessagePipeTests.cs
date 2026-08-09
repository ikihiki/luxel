using Luxel.Graphics.MessagePipe;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;

namespace Luxel.Graphics.MessagePipe.Tests;

public sealed class GpuLifecycleMessagePipeTests
{
    [Fact]
    public void Registration_is_idempotent_and_publishes_only_when_pumped()
    {
        var services = new ServiceCollection();
        services.AddGpuLifecycleMessagePipe();
        services.AddGpuLifecycleMessagePipe();
        using ServiceProvider provider = services.BuildServiceProvider();

        var received = new List<GpuDeviceLifecycleEvent>();
        using IDisposable subscription = provider.GetRequiredService<ISubscriber<GpuDeviceLifecycleEvent>>()
            .Subscribe(received.Add);
        var source = new GpuLifecycleSource(GpuBackendKind.WebGpu, "test", provider.GetRequiredService<IGpuLifecycleSink>(), "device", 2);

        source.DeviceEvent(GpuDeviceLifecycleState.Lost, GpuLifecycleReason.DeviceRemoved);
        Assert.Empty(received);
        Assert.Equal(1, provider.GetRequiredService<GpuLifecycleMessagePump>().Pump());
        Assert.Single(received);
        Assert.Equal(new GpuDeviceGeneration("device", 2), received[0].Device);
    }

    [Fact]
    public void Queue_preserves_typed_event_order_and_sequence()
    {
        var queue = new GpuLifecycleEventQueue();
        var collector = new Collector();
        var source = new GpuLifecycleSource(GpuBackendKind.Vulkan, "vk", queue, "gpu", 1);

        source.Validation(GpuValidationSeverity.Warning, "warning");
        source.Surface("surface", GpuSurfaceLifecycleState.Resized, 800, 600);
        source.DeviceEvent(GpuDeviceLifecycleState.Lost, GpuLifecycleReason.DeviceReset);

        Assert.Equal(3, queue.Pump(collector));
        Assert.Collection(collector.Messages,
            value => Assert.IsType<GpuValidationEvent>(value),
            value => Assert.IsType<GpuSurfaceLifecycleEvent>(value),
            value => Assert.IsType<GpuDeviceLifecycleEvent>(value));
        Assert.Equal(new long[] { 1, 2, 3 }, collector.Messages.Select(GetSequence));
    }

    private static long GetSequence(object value) => value switch
    {
        GpuDeviceLifecycleEvent message => message.Sequence,
        GpuValidationEvent message => message.Sequence,
        GpuSurfaceLifecycleEvent message => message.Sequence,
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private sealed class Collector : IGpuLifecycleSink
    {
        public List<object> Messages { get; } = [];
        public void Publish(GpuDeviceLifecycleEvent message) => Messages.Add(message);
        public void Publish(GpuValidationEvent message) => Messages.Add(message);
        public void Publish(GpuSurfaceLifecycleEvent message) => Messages.Add(message);
    }
}
