using System.Numerics;
using Luxel.Controls;
using Luxel.NodeGraph;
using Luxel.UI;
using Xunit;

namespace Luxel.Tests;

/// <summary>WB: IEditorDocument アダプタ (WS-D D1) — TextDocument のダーティ/保存点、
/// NodeGraphJson の往復。GPU 不要 (view は作らない)。</summary>
public class EditorDocumentTests
{
    [Fact]
    public void TextDocument_DirtyFollowsAcceptedSavedSnapshot()
    {
        using var doc = new TextDocument("code", "a.cs", _ => throw new NotSupportedException(), "v1");
        Assert.False(doc.Dirty.Value);

        doc.Text.Value = "v2";
        Assert.True(doc.Dirty.Value);

        string serialized = doc.Serialize();
        Assert.Equal("v2", serialized);
        Assert.True(doc.Dirty.Value); // serialization is pure until persistence succeeds
        doc.AcceptSavedSnapshot(serialized);
        Assert.False(doc.Dirty.Value);
        doc.Text.Value = "v3";
        Assert.True(doc.Dirty.Value);
        doc.Text.Value = "v2";          // 保存内容へ戻せばクリーン
        Assert.False(doc.Dirty.Value);
    }

    [Fact]
    public void TextDocument_LoadFrom_SetsTextAndBaseline()
    {
        using var doc = new TextDocument("markdown", "r.md", _ => throw new NotSupportedException());
        doc.LoadFrom("# hi");
        Assert.Equal("# hi", doc.Text.Value);
        Assert.False(doc.Dirty.Value);
        Assert.Equal("# hi", doc.Serialize());
    }

    [Fact]
    public void NodeGraphJson_RoundTrips()
    {
        var doc = NodeGraphDoc.Of(
            [new GraphNode(1, "source", "Input", new Vector2(30, 40),
                 [new NodePort(0, PortDir.Out, "v", "value", Multi: true)], Data: "seed=7"),
             new GraphNode(2, "sink", "Out", new Vector2(200, 90),
                 [new NodePort(0, PortDir.In, "v", "in")], Collapsed: true)],
            [new GraphEdge(10, new PortId(1, 0), new PortId(2, 0))]);

        NodeGraphDoc back = NodeGraphJson.Deserialize(NodeGraphJson.Serialize(doc));

        Assert.Equal(2, back.Nodes.Count);
        GraphNode n1 = back.Node(1);
        Assert.Equal(("source", "Input"), (n1.Kind, n1.Title));
        Assert.Equal(new Vector2(30, 40), n1.Pos);
        Assert.Equal("seed=7", n1.Data);
        Assert.True(n1.Ports[0].Multi);
        Assert.True(back.Node(2).Collapsed);
        GraphEdge e = Assert.Single(back.Edges);
        Assert.Equal((new PortId(1, 0), new PortId(2, 0)), (e.From, e.To));
        // 再直列化が一致 (決定的)
        Assert.Equal(NodeGraphJson.Serialize(doc), NodeGraphJson.Serialize(back));
    }
}
