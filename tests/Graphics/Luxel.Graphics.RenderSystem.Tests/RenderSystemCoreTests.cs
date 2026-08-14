using Luxel.Graphics.RenderSystem;

namespace Luxel.Graphics.RenderSystem.Tests;

public sealed class RenderSystemCoreTests
{
    [Fact]
    public void Register_UnionsMultipleCallsUsingReferenceIdentity()
    {
        var set = new RenderFeatureSetId("opaque");
        var first = new StubFeature();
        var second = new StubFeature();
        var assignments = new RenderFeatureAssignmentBuilder();

        assignments.Register(set, first);
        assignments.Register(set, first, second);

        IReadOnlySet<IRenderFeature> features = assignments.Build()[set].Features;
        Assert.Equal(2, features.Count);
        Assert.Contains(first, features);
        Assert.Contains(second, features);
    }

    [Fact]
    public void Register_EmptyIsNoOp_AndNullElementIsRejected()
    {
        var assignments = new RenderFeatureAssignmentBuilder();
        var set = new RenderFeatureSetId("opaque");
        assignments.Register(set);
        Assert.Empty(assignments.Build());
        Assert.Throws<ArgumentNullException>(() => assignments.Register(set, [null!]));
    }

    [Fact]
    public void Order_IsIdempotent_AndImmutableAfterSeal()
    {
        var opaque = new RenderFeatureSetId("opaque");
        var post = new RenderFeatureSetId("post");
        var order = new RenderFeatureSetOrder().Add(opaque).Add(opaque).InsertAfter(opaque, post);
        Assert.Equal([opaque, post], order);
        order.Seal();
        Assert.Throws<InvalidOperationException>(() => order.Add(new("present")));
    }

    [Fact]
    public void Generation_CommitsOnlyObservedValue()
    {
        var set = new RenderFeatureSetId("ui");
        var registry = new RenderFeatureSetStateRegistry();
        ulong observed = registry.Invalidate(set);
        registry.Invalidate(set);
        registry.Commit(set, observed);
        RenderFeatureSetGeneration state = registry.Read(set);
        Assert.Equal<ulong>(2, state.CurrentGeneration);
        Assert.Equal<ulong>(1, state.CommittedGeneration);
        Assert.True(state.IsDirty);
    }

    private sealed class StubFeature : IRenderFeature
    {
        public void AddPasses(RenderFeatureContext context) { }
    }
}
