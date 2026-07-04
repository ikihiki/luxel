using Luxel.Diagram;
using Xunit;

namespace Luxel.Tests;

/// <summary>DG: mermaid サブセットのパースとランクレイアウト (純ロジック)。</summary>
public class DiagramTests
{
    private static (float, float) Measure(string s) => (s.Length * 7f, 14f);

    [Fact]
    public void Parse_NodesShapesEdges()
    {
        DiagramSpec spec = MermaidParser.Parse("""
            flowchart TB
            a[箱] --> b(丸)
            b -->|ラベル| c{分岐}
            %% コメントは無視
            style a fill:#f9f
            """);
        Assert.False(spec.Horizontal);
        Assert.Equal(3, spec.Nodes.Count);
        Assert.Equal(DiagramShape.Rect, spec.Nodes[0].Shape);
        Assert.Equal(DiagramShape.Rounded, spec.Nodes[1].Shape);
        Assert.Equal(DiagramShape.Diamond, spec.Nodes[2].Shape);
        Assert.Equal("箱", spec.Nodes[0].Label);
        Assert.Equal(2, spec.Edges.Count);
        Assert.Equal("ラベル", spec.Edges[1].Label);
        Assert.Null(spec.Edges[0].Label);   // 対応外の style 行は無視されている
    }

    [Fact]
    public void Parse_BareId_And_Redeclare()
    {
        DiagramSpec spec = MermaidParser.Parse("graph LR\na --> b\na[アプリ] --> c");
        Assert.True(spec.Horizontal);
        Assert.Equal("アプリ", spec.Nodes.First(n => n.Id == "a").Label);   // 後宣言でラベル確定
        Assert.Equal("b", spec.Nodes.First(n => n.Id == "b").Label);        // 裸 id はラベル = id
    }

    [Fact]
    public void Layout_RanksProgressAlongMainAxis()
    {
        DiagramSpec spec = MermaidParser.Parse("flowchart LR\na --> b\nb --> c\na --> c");
        DiagramLayoutResult lay = DiagramLayout.Arrange(spec, Measure);
        float X(string id) => lay.Nodes.First(n => n.Node.Id == id).X;
        Assert.True(X("a") < X("b"));
        Assert.True(X("b") < X("c"));   // c はランク 2 (最長パス)
        Assert.True(lay.Width > 0 && lay.Height > 0);
        Assert.Equal(3, lay.Edges.Count);
    }

    [Fact]
    public void Layout_Cycle_DoesNotThrow()
    {
        DiagramSpec spec = MermaidParser.Parse("flowchart LR\na --> b\nb --> a");
        DiagramLayoutResult lay = DiagramLayout.Arrange(spec, Measure);
        Assert.Equal(2, lay.Nodes.Count);   // 循環でも配置される (逆辺は線のみ)
    }

    [Fact]
    public void Layout_Empty_IsZero()
    {
        DiagramLayoutResult lay = DiagramLayout.Arrange(MermaidParser.Parse(""), Measure);
        Assert.Equal(0, lay.Width);
        Assert.Empty(lay.Nodes);
    }
}
