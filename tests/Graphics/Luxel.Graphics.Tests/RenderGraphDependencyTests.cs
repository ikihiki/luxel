using Luxel.Graphics.RenderGraph;
using Rg = Luxel.Graphics.RenderGraph.RenderGraph;

namespace Luxel.Graphics.Tests;

public sealed class RenderGraphDependencyTests
{
    private static BufferDesc Desc() => new(64, GpuMemoryKind.HostMapped);
    private static RenderResourceSlotId Slot(string value) => new(value);
    private static RenderResourceVersionId Version(RenderResourceSlotId slot, string value) => new(slot, value);
    private static RenderPassKey Pass(string value) => new(value);

    [Fact]
    public void SymbolicConsumerBeforeProducer_IsTopologicallyOrdered()
    {
        var graph = new Rg();
        var resource = graph.CreateBufferForTest(Desc(), "resource");
        var output = graph.ImportBufferForTest("output");
        var slot = Slot("scene-color");
        var produced = Version(slot, "opaque");
        graph.DeclareBuffer(slot, resource);

        graph.AddPass(Pass("consumer"), "Consumer", PassQueue.Compute)
            .Read(produced)
            .Write(output)
            .Execute(_ => { });
        graph.AddPass(Pass("producer"), "Producer", PassQueue.Compute)
            .Write(produced)
            .Execute(_ => { });

        var compiled = graph.CompileForTest();

        Assert.Equal(new[] { "Producer", "Consumer" }, compiled.Order.Select(pass => pass.Name));
        Assert.Equal((0, 1), graph.GetLifetime(resource));
    }

    [Fact]
    public void ExplicitControlDependency_IsResolvedByStablePassKey()
    {
        var graph = new Rg();
        graph.AddPass(Pass("after"), "After")
            .DependsOn(Pass("before"))
            .SideEffect()
            .Execute(_ => { });
        graph.AddPass(Pass("before"), "Before")
            .Execute(_ => { });

        var compiled = graph.CompileForTest();

        Assert.Equal(new[] { "Before", "After" }, compiled.Order.Select(pass => pass.Name));
        Assert.False(graph.IsPassCulled(0));
        Assert.False(graph.IsPassCulled(1));
    }

    [Fact]
    public void ControlDependencyCycle_IsRejected()
    {
        var graph = new Rg();
        graph.AddPass(Pass("a"), "A").DependsOn(Pass("b")).SideEffect().Execute(_ => { });
        graph.AddPass(Pass("b"), "B").DependsOn(Pass("a")).Execute(_ => { });

        var error = Assert.Throws<InvalidOperationException>(() => graph.CompileForTest());
        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SymbolicReadWithMissingProducer_IsRejected()
    {
        var graph = new Rg();
        var resource = graph.CreateBufferForTest(Desc(), "resource");
        var slot = Slot("slot");
        graph.DeclareBuffer(slot, resource);
        graph.AddPass(Pass("consumer"), "Consumer").Read(Version(slot, "missing")).SideEffect().Execute(_ => { });

        var error = Assert.Throws<InvalidOperationException>(() => graph.CompileForTest());
        Assert.Contains("no producer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SymbolicVersionWithDuplicateProducer_IsRejected()
    {
        var graph = new Rg();
        var resource = graph.CreateBufferForTest(Desc(), "resource");
        var slot = Slot("slot");
        var version = Version(slot, "v1");
        graph.DeclareBuffer(slot, resource);
        graph.AddPass(Pass("a"), "A").Write(version).Execute(_ => { });
        graph.AddPass(Pass("b"), "B").Write(version).Execute(_ => { });

        var error = Assert.Throws<InvalidOperationException>(() => graph.CompileForTest());
        Assert.Contains("multiple producers", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownPredecessorAndControlTarget_AreRejected()
    {
        var graph = new Rg();
        var resource = graph.CreateBufferForTest(Desc(), "resource");
        var slot = Slot("slot");
        graph.DeclareBuffer(slot, resource);
        graph.AddPass(Pass("writer"), "Writer")
            .Write(Version(slot, "v2"), predecessor: Version(slot, "v1"))
            .SideEffect()
            .Execute(_ => { });

        var predecessorError = Assert.Throws<InvalidOperationException>(() => graph.CompileForTest());
        Assert.Contains("unknown", predecessorError.Message, StringComparison.OrdinalIgnoreCase);

        var controlGraph = new Rg();
        controlGraph.AddPass(Pass("pass"), "Pass").DependsOn(Pass("missing")).SideEffect().Execute(_ => { });
        var controlError = Assert.Throws<InvalidOperationException>(() => controlGraph.CompileForTest());
        Assert.Contains("unknown", controlError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForeignGraphHandles_AreRejectedForBuffersAndTextures()
    {
        var first = new Rg();
        var second = new Rg();
        var buffer = first.CreateBufferForTest(Desc(), "buffer");
        var texture = first.CreateTextureForTest(new TextureDesc(4, 4, GpuFormat.Rgba8Unorm), "texture");

        Assert.Throws<ArgumentException>(() => second.AddPass("Buffer").Read(buffer));
        Assert.Throws<ArgumentException>(() => second.AddPass("Texture").Read(texture));
        Assert.Throws<ArgumentException>(() => second.DeclareBuffer(Slot("buffer"), buffer));
        Assert.Throws<ArgumentException>(() => second.DeclareTexture(Slot("texture"), texture));
    }

    [Fact]
    public void ExportAndSideEffect_AreCullingSinks()
    {
        var graph = new Rg();
        var exportedResource = graph.CreateBufferForTest(Desc(), "exported");
        var deadResource = graph.CreateBufferForTest(Desc(), "dead");
        var slot = Slot("exported-slot");
        var version = Version(slot, "v1");
        graph.DeclareBuffer(slot, exportedResource);
        graph.Export(version);

        graph.AddPass(Pass("export"), "ExportProducer").Write(version).Execute(_ => { });
        graph.AddPass(Pass("side-effect"), "SideEffect").SideEffect().Execute(_ => { });
        graph.AddPass(Pass("dead"), "Dead").Write(deadResource).Execute(_ => { });

        graph.CompileForTest();

        Assert.False(graph.IsPassCulled(0));
        Assert.False(graph.IsPassCulled(1));
        Assert.True(graph.IsPassCulled(2));
    }
}
