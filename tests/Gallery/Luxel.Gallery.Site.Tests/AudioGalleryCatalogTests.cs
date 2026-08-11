using Luxel.Audio.Gallery;
using Luxel.Gallery;

namespace Luxel.Gallery.Site.Tests;

public sealed class AudioGalleryCatalogTests
{
    [Fact]
    public void Audio_gallery_owns_its_browser_safe_learn_and_example_routes()
    {
        StoryCatalog catalog = AudioGalleryProject.CreateCatalog();

        Assert.Equal(13, catalog.All.Count);
        Assert.Equal(8, catalog.All.Count(story => story.Path.StartsWith("Learn/Audio/", StringComparison.Ordinal)));
        Assert.Equal(5, catalog.All.Count(story => story.Path.StartsWith("Examples/Audio/", StringComparison.Ordinal)));
        Assert.All(catalog.All, story => Assert.True(
            story.Path.StartsWith("Learn/Audio/", StringComparison.Ordinal)
            || story.Path.StartsWith("Examples/Audio/", StringComparison.Ordinal)));
    }
}
