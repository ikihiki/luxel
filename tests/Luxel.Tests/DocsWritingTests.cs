using Luxel.Document;
using Xunit;

namespace Luxel.Tests;

/// <summary>DW5: コールアウト (GitHub alert 記法) / CJK 強調 / デッドリンク検証。</summary>
public class DocsWritingTests
{
    [Fact]
    public void Callout_ParsesKind_AndRoundTrips()
    {
        const string md = "> [!NOTE]\n> 補足です";
        RichDocument doc = Markdown.Parse(md);
        Block marker = doc.Blocks.First(b => b.CalloutMarker);
        Assert.Equal("NOTE", marker.Callout);
        Assert.Equal("NOTE", marker.Text);           // ラベル行
        Block body = doc.Blocks.First(b => b.Callout == "NOTE" && !b.CalloutMarker);
        Assert.Equal("補足です", body.Text);

        // round-trip: マーカーが記法へ戻り、再パースで同じ構造になる
        string outMd = Markdown.Serialize(doc);
        Assert.Contains("> [!NOTE]", outMd);
        RichDocument again = Markdown.Parse(outMd);
        Assert.Equal("NOTE", again.Blocks.First(b => b.CalloutMarker).Callout);
    }

    [Fact]
    public void Callout_Kinds_Warning()
    {
        RichDocument doc = Markdown.Parse("> [!WARNING]\n> 注意");
        Assert.Equal("WARNING", doc.Blocks.First(b => b.CalloutMarker).Callout);
    }

    [Fact]
    public void CjkEmphasis_BoldInsideJapanese()
    {
        // CommonMark 素の規則では「日本語**太字**が効かない」— UseCjkFriendlyEmphasis で効く
        RichDocument doc = Markdown.Parse("日本語**太字**です");
        Assert.Contains(doc.Blocks[0].Runs, r => r.Style.Bold && r.Text == "太字");
    }

    // ---- LinkCheck ----

    private static RichDocument Doc(string md) => Markdown.Parse(md);

    [Fact]
    public void LinkCheck_ValidAnchor_And_BrokenAnchor()
    {
        RichDocument doc = Doc("# T\n\n## 使い方\n\n[ok](#使い方) [ng](#存在しない)");
        List<string> broken = LinkCheck.FindBroken(doc.Blocks);
        Assert.Equal(["#存在しない"], broken);
    }

    [Fact]
    public void LinkCheck_StoryLinks_CheckedAgainstResolver()
    {
        RichDocument doc = Doc("[a](story:Docs/Button) [b](story:Nope/Nope)");
        List<string> broken = LinkCheck.FindBroken(doc.Blocks, p => p == "Docs/Button");
        Assert.Equal(["story:Nope/Nope"], broken);
    }

    [Fact]
    public void LinkCheck_ExternalLinks_Ignored()
    {
        RichDocument doc = Doc("[x](https://example.com)");
        Assert.Empty(LinkCheck.FindBroken(doc.Blocks, _ => false));
    }
}
