using Luxel.Platform.Web;

namespace Luxel.Platform.Web.Tests;

public sealed class WebEventQueueTests
{
    [Fact]
    public void Queue_preserves_event_order_and_payloads()
    {
        var queue = new WebEventQueue();
        var resize = new WebEvent(3, WebEventKind.Resize, A: 2, I0: 800, I1: 600);
        var text = new WebEvent(3, WebEventKind.TextInput, Text: "😀");
        var close = new WebEvent(3, WebEventKind.Close);

        queue.Enqueue(resize);
        queue.Enqueue(text);
        queue.Enqueue(close);

        Assert.Equal(3, queue.Count);
        Assert.True(queue.TryDequeue(out WebEvent first));
        Assert.Equal(resize, first);
        Assert.True(queue.TryDequeue(out WebEvent second));
        Assert.Equal(text, second);
        Assert.True(queue.TryDequeue(out WebEvent third));
        Assert.Equal(close, third);
        Assert.False(queue.TryDequeue(out _));
    }

    [Theory]
    [InlineData(110, 70, 10, 20, 200, 100, 400, 200, 200, 100)]
    [InlineData(35, 45, 10, 20, 100, 100, 300, 200, 75, 50)]
    [InlineData(-10, -20, 0, 0, 100, 100, 200, 300, -20, -60)]
    public void Coordinates_are_scaled_from_css_to_backing_pixels(
        double clientX, double clientY, double left, double top,
        double cssWidth, double cssHeight, int backingWidth, int backingHeight,
        float expectedX, float expectedY)
    {
        (float x, float y) = WebCoordinateNormalizer.ToBackingPixels(
            clientX, clientY, left, top, cssWidth, cssHeight, backingWidth, backingHeight);

        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

#pragma warning disable CA1416 // The test intentionally verifies the browser-only API's off-browser guard.
    [Fact]
    public async Task Browser_factories_are_guarded_off_browser()
    {
        if (OperatingSystem.IsBrowser()) return;

        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await WebWindowBackend.CreateAsync(new WebWindowBackendOptions
            {
                Canvases = [WebCanvasOptions.FromId("canvas")],
            }));
        await Assert.ThrowsAsync<PlatformNotSupportedException>(async () =>
            await WebClipboardBackend.CreateAsync());
    }

#pragma warning restore CA1416

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(double.NaN, 100)]
    public void Invalid_coordinate_geometry_returns_origin(double cssWidth, double cssHeight)
    {
        Assert.Equal((0f, 0f), WebCoordinateNormalizer.ToBackingPixels(
            10, 20, 0, 0, cssWidth, cssHeight, 200, 200));
    }
}
