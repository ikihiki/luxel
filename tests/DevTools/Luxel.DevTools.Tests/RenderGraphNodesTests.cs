using Luxel.Controls;
using Luxel.Diagnostics;
using Luxel.NodeGraph;

namespace Luxel.Tests;

/// <summary>DiagRenderGraph → NodeGraphDoc 変換 (RenderGraphNodes、DevTools のレンダーグラフ可視化) の単体テスト。
/// パス=ノード / リソース依存 (書き手→読み手) = 辺、AutoLayout で整列済み。canvas 不要。</summary>
public class RenderGraphNodesTests
{
    private static DiagRenderGraph Sample()
    {
        DiagRenderGraphPass Pass(int i, string name, int[] reads, int[] writes)
            => new(i, name, "Graphics", false, reads, writes);
        DiagRenderGraphResource Res(int id, string name)
            => new(id, name, "Transient", false, 0, 0, 0, 0);

        return new DiagRenderGraph(
            [Pass(0, "Upload", [], [1]),
             Pass(1, "Shadow", [1], [2]),
             Pass(2, "Main", [1, 2], [3]),
             Pass(3, "Present", [3], [])],
            [Res(1, "vbuf"), Res(2, "shadow"), Res(3, "color")],
            PhysicalTransientCount: 3, ExecutedPassCount: 4);
    }

    [Fact]
    public void Build_PassesBecomeNodes_ResourceDepsBecomeEdges()
    {
        NodeGraphDoc doc = RenderGraphNodes.Build(Sample());

        Assert.Equal(4, doc.Nodes.Count);                    // パス = ノード
        Assert.Equal("Upload", doc.Node(0).Title);
        Assert.Equal("Present", doc.Node(3).Title);

        // 辺: r1(0→1, 0→2) + r2(1→2) + r3(2→3) = 4 本
        Assert.Equal(4, doc.Edges.Count);
        // Main (2) は vbuf/shadow の 2 入力 + color の 1 出力
        GraphNode main = doc.Node(2);
        Assert.Equal(2, main.Ports.Count(p => p.Dir == PortDir.In));
        Assert.Equal(1, main.Ports.Count(p => p.Dir == PortDir.Out));
    }

    [Fact]
    public void Build_LaysOutByDependency_LeftToRight()
    {
        NodeGraphDoc doc = RenderGraphNodes.Build(Sample());
        // 依存の下流ほど右 (Upload < Shadow < Main < Present)
        Assert.True(doc.Node(0).Pos.X < doc.Node(1).Pos.X);
        Assert.True(doc.Node(1).Pos.X < doc.Node(2).Pos.X);
        Assert.True(doc.Node(2).Pos.X < doc.Node(3).Pos.X);
    }

    [Fact]
    public void Build_MarksCulledPasses()
    {
        var rg = new DiagRenderGraph(
            [new DiagRenderGraphPass(0, "Dead", "Graphics", Culled: true, [], [])],
            [], 0, 0);
        Assert.Contains("culled", RenderGraphNodes.Build(rg).Node(0).Title);
    }

    [Fact]
    public void Build_TutorialPostProcess_ShowsSceneCopyAndPostDependencies()
    {
        var rg = new DiagRenderGraph(
            [new DiagRenderGraphPass(0, "DrawScene", "Graphics", false, [], [1]),
             new DiagRenderGraphPass(1, "SceneReadback", "Graphics", false, [1], [2]),
             new DiagRenderGraphPass(2, "PostProcess", "Compute", false, [2], [3])],
            [new DiagRenderGraphResource(1, "scene-color", "TransientTex", false, 0, 0, 1, 0),
             new DiagRenderGraphResource(2, "scene-pixels", "Transient", false, 0, 1, 2, 0),
             new DiagRenderGraphResource(3, "present-framebuffer", "External", false, -1, 2, -1, 0)],
            PhysicalTransientCount: 2, ExecutedPassCount: 3);

        NodeGraphDoc doc = RenderGraphNodes.Build(rg);
        Assert.Equal(["DrawScene", "SceneReadback", "PostProcess"], doc.Nodes.Select(n => n.Title));
        Assert.Equal(2, doc.Edges.Count);
    }

    [Fact]
    public void Build_EmptyGraph_NoNodes()
        => Assert.Empty(RenderGraphNodes.Build(new DiagRenderGraph([], [], 0, 0)).Nodes);
}
