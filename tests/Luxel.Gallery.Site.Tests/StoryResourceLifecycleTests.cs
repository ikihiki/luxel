using System.Text.Json;
using Luxel.Controls;
using Luxel.Gallery;
using Luxel.Graphics.TwoD.Skia;
using Luxel.Resources;
using Luxel.Typography;
using Luxel.UI;

namespace Luxel.Gallery.Site.Tests;

public sealed class StoryResourceLifecycleTests
{
    private sealed class TrackedResource : IDisposable
    {
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }

    [Fact]
    public async Task StoryContext_owns_and_idempotently_disposes_scoped_resources()
    {
        using var resources = new ResourceSystem();
        var context = new StoryContext(resources);
        var value = new TrackedResource();
        ResourceHandle<TrackedResource> handle = context.ScopedResources.Create(
            "owned", _ => Task.FromResult(value));
        await handle.Ready;

        Assert.Same(resources, context.Resources);
        Assert.Same(context.ScopedResources, context.ScopedResourcesOrNull);

        context.Dispose();
        context.Dispose();
        resources.Pump();

        Assert.Equal(1, value.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => context.ScopedResources.Create(
            "late", _ => Task.FromResult(new TrackedResource())));
    }

    [Fact]
    public void GalleryHost_selection_and_dispose_release_each_story_scope()
    {
        var instances = new List<TrackedResource>();
        Widget Build(StoryContext context)
        {
            var value = new TrackedResource();
            ResourceHandle<TrackedResource> handle = context.ScopedResources.Create(
                "owned", _ => Task.FromResult(value));
            handle.Ready.GetAwaiter().GetResult();
            instances.Add(value);
            return Kit.Text("scoped");
        }

        var story = new StoryInfo("Test/Scoped", 120, 60, null, Build);
        var builder = new StoryCatalogBuilder();
        builder.Add(story);
        using VectorFont font = GalleryFonts.Load(GalleryFonts.Regular);
        using var rasterizer = new SkiaRasterizer2D();
        var host = new GalleryHost(rasterizer, font, builder.Build());

        host.SelectExact(story.Path);
        host.Commands.Enqueue("story.resize", JsonSerializer.SerializeToElement(new { w = 180, h = 90 }));
        host.Step(1f / 60f);

        Assert.Equal(2, instances.Count);
        Assert.Equal(1, instances[0].DisposeCount);
        Assert.Equal(0, instances[1].DisposeCount);

        host.SelectExact(story.Path);
        host.Step(1f / 60f);

        Assert.Equal(3, instances.Count);
        Assert.Equal(1, instances[1].DisposeCount);
        Assert.Equal(0, instances[2].DisposeCount);

        host.Dispose();
        host.Dispose();

        Assert.Equal(1, instances[2].DisposeCount);
    }
}
