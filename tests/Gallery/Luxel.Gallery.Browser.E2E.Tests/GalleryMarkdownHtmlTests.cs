using GalleryBrowser;
using Luxel.Gallery;
using Luxel.UI;

namespace Luxel.Gallery.Browser.E2E.Tests;

public sealed class GalleryMarkdownHtmlTests
{
    [Fact]
    public void Control_api_embed_renders_semantic_table_and_respects_inherited_filter()
    {
        const string fullName = "Luxel.Tests.Browser.SampleControl";
        ControlApiRegistry.RegisterLocalized(new ControlApi("Luxel.Tests.Browser", "SampleControl", "<安全> な概要",
        [
            new ApiMember("caption<safe>", "string", "ctor", "表示文字"),
            new ApiMember("Clicked", "Action", "event", "クリック時"),
            new ApiMember("margin", "Thickness", "param", "共通余白", Inherited: true),
        ]));

        string ownMembers = Render(new TestEmbed("ControlApiTable", fullName, IncludeInherited: false));

        Assert.Contains("class=\"api-reference\"", ownMembers, StringComparison.Ordinal);
        Assert.Contains("<table class=\"api-table\">", ownMembers, StringComparison.Ordinal);
        Assert.Contains("<th scope=\"col\">名前</th>", ownMembers, StringComparison.Ordinal);
        Assert.Contains("caption&lt;safe&gt;", ownMembers, StringComparison.Ordinal);
        Assert.Contains("&lt;安全&gt; な概要", ownMembers, StringComparison.Ordinal);
        Assert.DoesNotContain("margin", ownMembers, StringComparison.Ordinal);
        Assert.Contains("Widget 共通パラメーターは省略しています", ownMembers, StringComparison.Ordinal);

        string inherited = Render(new TestEmbed("ControlApiTable", fullName, IncludeInherited: true));
        Assert.Contains("margin", inherited, StringComparison.Ordinal);
    }

    [Fact]
    public void Type_api_embed_uses_enum_value_section()
    {
        const string fullName = "Luxel.Tests.Browser.SampleMode";
        TypeApiRegistry.Register(new TypeApi("Luxel.Tests.Browser", "SampleMode", "enum", "表示モード",
        [
            new ApiMember("Compact", "SampleMode", "field", "省スペース表示"),
        ]));

        string html = Render(new TestEmbed("TypeApiTable", fullName));

        Assert.Contains("型 API", html, StringComparison.Ordinal);
        Assert.Contains("Luxel.Tests.Browser.SampleMode", html, StringComparison.Ordinal);
        Assert.Contains("<th scope=\"rowgroup\" colspan=\"3\">値</th>", html, StringComparison.Ordinal);
        Assert.Contains("Compact", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_widget_embed_keeps_a_japanese_semantic_fallback()
    {
        string html = Render(new TestEmbed("Widget", "preview<script>"));

        Assert.Contains("class=\"markdown-embed-unavailable\"", html, StringComparison.Ordinal);
        Assert.Contains("data-embed-kind=\"Widget\"", html, StringComparison.Ordinal);
        Assert.Contains("埋め込みを表示できません", html, StringComparison.Ordinal);
        Assert.Contains("preview&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("preview<script>", html, StringComparison.Ordinal);
    }

    private static string Render(IMarkdownEmbed embed)
    {
        var result = new StoryResult(64, 1);
        result.AppendLiteral("# テスト\n\n");
        result.AppendFormatted(embed);
        var story = new StoryInfo("Tests/Browser/Document", _ => result);
        return GalleryMarkdownHtml.Render(story, result);
    }

    private sealed record TestEmbed(string Kind, string? Reference, bool IncludeInherited = false) : IMarkdownEmbed
    {
        public Widget? Widget => null;
        public bool Inline => false;
        public Func<Widget>? WidgetFactory => null;
    }
}
