using System.Runtime.CompilerServices;
using Luxel.Graphics.RenderGraph;
using Luxel.Graphics.RenderSystem;
using Luxel.Graphics.TwoD;

namespace Luxel.UI.Tests;

public sealed class UiRenderingTests
{
    [Fact]
    public void RetainedCanvasChangeGenerationIsMonotonicForRetainedMutations()
    {
        using var canvas = new RetainedCanvas();
        ulong generation = canvas.ChangeGeneration;

        UiNode node = canvas.AddChild(canvas.Root);
        AssertGenerationAdvanced(canvas, ref generation);
        node.Content = new Scene2D().FillRect(Color2D.White, 0, 0, 10, 10);
        AssertGenerationAdvanced(canvas, ref generation);
        node.Transform = Affine2D.Translate(2, 3);
        AssertGenerationAdvanced(canvas, ref generation);
        node.Color = Color2D.Red;
        AssertGenerationAdvanced(canvas, ref generation);
        node.Clip = new RectClip(0, 0, 5, 5);
        AssertGenerationAdvanced(canvas, ref generation);
        node.Z = 2;
        AssertGenerationAdvanced(canvas, ref generation);
        node.ContentColors = true;
        AssertGenerationAdvanced(canvas, ref generation);
        node.ReserveContent(32, 8);
        AssertGenerationAdvanced(canvas, ref generation);
        node.Touch();
        AssertGenerationAdvanced(canvas, ref generation);
        canvas.Remove(node);
        AssertGenerationAdvanced(canvas, ref generation);

        canvas.Flush(10, 10);
        Assert.Equal(generation, canvas.ChangeGeneration);
    }

    [Fact]
    public void SurfaceTickPropagatesRetainedDirtyWithoutPublishingOutput()
    {
        using var canvas = new RetainedCanvas();
        var registry = new RenderFeatureSetStateRegistry();
        using var source = new RenderFeatureSetInvalidationSource(RenderFeatureSets.UiContent, registry);
        using var output = new PersistentUiOutput<GpuBuffer>();
        using var surface = new UiSurfaceState(
            "test", UiSurfaceRole.Content, canvas, output, source,
            _ => { }, (_, _) => { });
        ulong initialInvalidation = registry.Read(RenderFeatureSets.UiContent).CurrentGeneration;

        canvas.Root.Touch();
        surface.Tick(1f / 60f);

        Assert.True(surface.IsDirty);
        Assert.Equal(initialInvalidation + 1, registry.Read(RenderFeatureSets.UiContent).CurrentGeneration);
        canvas.Flush(1, 1);
        Assert.True(surface.IsDirty);
    }

    [Fact]
    public void PersistentOutputPublishesOnlySuccessfulPendingResource()
    {
        var disposed = new List<object>();
        using var output = new PersistentUiOutput<object>(disposed.Add);
        object current = new();
        object failed = new();
        object succeeded = new();
        output.SetCurrent(current);

        output.Stage(failed);
        Assert.False(output.Complete(succeeded: false));
        Assert.Same(current, output.Current);
        Assert.Contains(failed, disposed);

        output.Stage(succeeded);
        Assert.True(output.Complete(succeeded: true));
        Assert.Same(succeeded, output.Current);
        Assert.Contains(current, disposed);
    }

    [Fact]
    public void RestagingPublishedPresentationTargetDoesNotInvalidateUiAgain()
    {
        using var canvas = new RetainedCanvas();
        var registry = new RenderFeatureSetStateRegistry();
        using var source = new RenderFeatureSetInvalidationSource(RenderFeatureSets.PresentUi, registry);
        using var output = new PersistentUiOutput<GpuBuffer>();
        GpuBuffer target = (GpuBuffer)RuntimeHelpers.GetUninitializedObject(typeof(GpuBuffer));
        using var surface = new UiSurfaceState(
            "present", UiSurfaceRole.Present, canvas, output, source,
            _ => { }, (graph, _) => graph.AddPass("present-ui").SideEffect().Execute(_ => { }));

        surface.StagePending(target);
        using (var graph = new RenderGraph()) Assert.True(surface.AddPasses(graph));
        surface.CompleteBatch(succeeded: true);
        ulong generation = registry.Read(RenderFeatureSets.PresentUi).CurrentGeneration;

        surface.StagePending(target);

        Assert.Equal(generation, registry.Read(RenderFeatureSets.PresentUi).CurrentGeneration);
        Assert.False(surface.IsDirty);
    }

    [Fact]
    public void DirtyPublishedPresentationTargetIsRasterizedAgainInPlace()
    {
        using var canvas = new RetainedCanvas();
        var registry = new RenderFeatureSetStateRegistry();
        using var source = new RenderFeatureSetInvalidationSource(RenderFeatureSets.PresentUi, registry);
        using var output = new PersistentUiOutput<GpuBuffer>();
        GpuBuffer target = (GpuBuffer)RuntimeHelpers.GetUninitializedObject(typeof(GpuBuffer));
        int rasterPasses = 0;
        using var surface = new UiSurfaceState(
            "present", UiSurfaceRole.Present, canvas, output, source,
            _ => { }, (graph, _) =>
            {
                rasterPasses++;
                graph.AddPass("present-ui").SideEffect().Execute(_ => { });
            });

        surface.StagePending(target);
        using (var graph = new RenderGraph()) Assert.True(surface.AddPasses(graph));
        surface.CompleteBatch(succeeded: true);

        canvas.Root.Touch();
        surface.ObserveChanges();
        using (var graph = new RenderGraph()) Assert.True(surface.AddPasses(graph));
        surface.CompleteBatch(succeeded: true);

        Assert.Equal(2, rasterPasses);
        Assert.Same(target, output.Current);
        Assert.False(surface.IsDirty);
    }

    [Fact]
    public void LogicalTickContinuesWhileSurfaceIsCleanAndRasterIsThrottled()
    {
        using var canvas = new RetainedCanvas();
        var registry = new RenderFeatureSetStateRegistry();
        using var source = new RenderFeatureSetInvalidationSource(RenderFeatureSets.UiContent, registry);
        using var output = new PersistentUiOutput<GpuBuffer>();
        GpuBuffer buffer = (GpuBuffer)RuntimeHelpers.GetUninitializedObject(typeof(GpuBuffer));
        output.Stage(buffer);
        int ticks = 0;
        using var surface = new UiSurfaceState(
            "clean", UiSurfaceRole.Content, canvas, output, source,
            _ => ticks++,
            (graph, _) => graph.AddPass("ui-test").SideEffect().Execute(_ => { }));
        using (var graph = new RenderGraph())
        {
            Assert.True(surface.AddPasses(graph));
            surface.CompleteBatch(succeeded: true);
        }
        Assert.False(surface.IsDirty);
        ulong generation = registry.Read(RenderFeatureSets.UiContent).CurrentGeneration;

        surface.Tick(1f / 60f);
        surface.Tick(1f / 60f);
        surface.Tick(1f / 60f);

        Assert.Equal(3, ticks);
        Assert.False(surface.IsDirty);
        Assert.Equal(generation, registry.Read(RenderFeatureSets.UiContent).CurrentGeneration);
    }

    [Fact]
    public void FailedSurfaceBatchKeepsCurrentOutputAndDirtyState()
    {
        using var canvas = new RetainedCanvas();
        var registry = new RenderFeatureSetStateRegistry();
        using var source = new RenderFeatureSetInvalidationSource(RenderFeatureSets.UiContent, registry);
        using var output = new PersistentUiOutput<GpuBuffer>();
        GpuBuffer current = (GpuBuffer)RuntimeHelpers.GetUninitializedObject(typeof(GpuBuffer));
        GpuBuffer pending = (GpuBuffer)RuntimeHelpers.GetUninitializedObject(typeof(GpuBuffer));
        output.SetCurrent(current);
        output.Stage(pending);
        using var surface = new UiSurfaceState(
            "failure", UiSurfaceRole.Content, canvas, output, source,
            _ => { }, (graph, _) => graph.AddPass("ui-test").SideEffect().Execute(_ => { }));

        using (var graph = new RenderGraph()) Assert.True(surface.AddPasses(graph));
        surface.CompleteBatch(succeeded: false);

        Assert.Same(current, output.Current);
        Assert.True(surface.IsDirty);
    }

    [Fact]
    public void CompositedFeatureRendersNestedContentBeforePresentSurface()
    {
        using var renderer = new UiRendererState();
        using var presentCanvas = new RetainedCanvas();
        using var contentCanvas = new RetainedCanvas();
        using var nestedCanvas = new RetainedCanvas();
        using var presentOutput = new PersistentUiOutput<GpuBuffer>();
        using var contentOutput = new PersistentUiOutput<GpuBuffer>();
        using var nestedOutput = new PersistentUiOutput<GpuBuffer>();
        GpuBuffer buffer = (GpuBuffer)RuntimeHelpers.GetUninitializedObject(typeof(GpuBuffer));
        presentOutput.Stage(buffer);
        contentOutput.Stage(buffer);
        nestedOutput.Stage(buffer);
        var order = new List<string>();
        using var present = new UiSurfaceState("present", UiSurfaceRole.Present, presentCanvas, presentOutput,
            renderer.CreateInvalidationSource(UiSurfaceRole.Present), _ => { },
            (graph, _) => { order.Add("present"); graph.AddPass("present").SideEffect().Execute(_ => { }); });
        using var content = new UiSurfaceState("content", UiSurfaceRole.Content, contentCanvas, contentOutput,
            renderer.CreateInvalidationSource(UiSurfaceRole.Content), _ => { },
            (graph, _) => { order.Add("content"); graph.AddPass("content").SideEffect().Execute(_ => { }); });
        using var nested = new UiSurfaceState("nested", UiSurfaceRole.Content, nestedCanvas, nestedOutput,
            renderer.CreateInvalidationSource(UiSurfaceRole.Content), _ => { },
            (graph, _) => { order.Add("nested"); graph.AddPass("nested").SideEffect().Execute(_ => { }); });
        renderer.Add(present);
        renderer.Add(content);
        renderer.Add(nested);

        var feature = new CompositedUiRenderFeature(renderer);
        using var graph = new RenderGraph();
        feature.AddPasses(new RenderFeatureContext(graph));
        feature.CompleteBatch(succeeded: true);

        Assert.Equal(["nested", "content", "present"], order);
    }

    private static void AssertGenerationAdvanced(RetainedCanvas canvas, ref ulong previous)
    {
        Assert.True(canvas.ChangeGeneration > previous);
        previous = canvas.ChangeGeneration;
    }
}
