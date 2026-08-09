using Luxel.Gallery;
using Luxel.Resources;
using Luxel.UI;

namespace Luxel.Gallery.Site.Tests;

public sealed class StoryContextResourceObservationTests
{
    [Fact]
    public void ObserveUpdatesSignalFromResourcePumpAndDetachesOnDispose()
    {
        using ResourceSystem resources = ResourceSystemDefaults.CreateBuilder().Build();
        using ResourceHandle<object> handle = resources.Publish(
            "published://story-resource", new object(), ResourceOwnership.Borrowed);
        var context = new StoryContext(resources);
        Signal<ResourceState> state = context.Observe(handle);
        int changes = 0;
        state.Changed += _ => changes++;

        resources.Republish("published://story-resource", new object());
        Assert.Equal(0, changes);
        resources.Pump();
        Assert.Equal(1, changes);
        Assert.Equal(1, state.Value.Version);

        context.Dispose();
        resources.Republish("published://story-resource", new object());
        resources.Pump();
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task ObservePrivateSystemPumpsCompletionIntoSignalOnHostThread()
    {
        using ResourceSystem hostResources = ResourceSystemDefaults.CreateBuilder().Build();
        using ResourceSystem privateResources = ResourceSystemDefaults.CreateBuilder().Build();
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        using ResourceHandle<object> handle = privateResources.Load(
            "generated://private-resource", _ => completion.Task, ResourceOwnership.Borrowed);
        using var context = new StoryContext(hostResources);
        Signal<ResourceState> state = context.Observe(privateResources, handle);

        completion.SetResult(new object());
        await handle.Ready;
        Assert.Equal(ResourceStatus.Loading, state.Value.Status);

        context.PumpObservedResources();

        Assert.Equal(ResourceStatus.Ready, state.Value.Status);
        Assert.True(state.Value.HasValue);
    }

    [Fact]
    public void PumpObservedResourcesForgetsDisposedPrivateSystems()
    {
        using ResourceSystem hostResources = ResourceSystemDefaults.CreateBuilder().Build();
        var privateResources = ResourceSystemDefaults.CreateBuilder().Build();
        using ResourceHandle<object> handle = privateResources.Publish(
            "published://private-resource", new object(), ResourceOwnership.Borrowed);
        using var context = new StoryContext(hostResources);
        _ = context.Observe(privateResources, handle);
        privateResources.Dispose();

        context.PumpObservedResources();
        context.PumpObservedResources();
    }
}
