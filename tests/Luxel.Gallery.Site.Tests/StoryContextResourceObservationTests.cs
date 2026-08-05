using Luxel.Gallery;
using Luxel.Resources;
using Luxel.UI;

namespace Luxel.Gallery.Site.Tests;

public sealed class StoryContextResourceObservationTests
{
    [Fact]
    public void ObserveUpdatesSignalFromResourcePumpAndDetachesOnDispose()
    {
        using var resources = new ResourceSystem();
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
}
