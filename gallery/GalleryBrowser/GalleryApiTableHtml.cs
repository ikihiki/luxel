using System.Net;
using System.Text;
using Luxel.Gallery;
using Luxel.UI;

namespace GalleryBrowser;

/// <summary>Browser-only semantic renderer for documentation embeds backed by generated API metadata.</summary>
internal static class GalleryApiTableHtml
{
    public static bool TryRender(StoryMarkdownEmbed embed, out string html)
    {
        ArgumentNullException.ThrowIfNull(embed);
        html = embed.Kind switch
        {
            "ControlApiTable" => RenderControl(embed.Reference, embed.IncludeInherited),
            "TypeApiTable" => RenderType(embed.Reference),
            _ => string.Empty,
        };
        return html.Length > 0;
    }

    private static string RenderControl(string? reference, bool includeInherited)
    {
        if (string.IsNullOrWhiteSpace(reference)) return string.Empty;
        ControlApi? api = ControlApiRegistry.Find(reference);
        if (api is null) return Unavailable("ControlApiTable", reference,
            "生成されたコントロール API メタデータが見つかりません。");

        IEnumerable<ApiMember> members = api.Members.Where(member => includeInherited || !member.Inherited);
        return RenderTable("コントロール API", api.Name, api.Summary, null, members,
            [("ctor", "コンストラクタ引数"), ("event", "イベント"), ("param", "パラメーター")],
            includeInherited ? null : api.Members.Any(member => member.Inherited)
                ? "Widget 共通パラメーターは省略しています。継承メンバーを含むページではあわせて表示されます。"
                : null);
    }

    private static string RenderType(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return string.Empty;
        TypeApi? api = TypeApiRegistry.Find(reference);
        if (api is null) return Unavailable("TypeApiTable", reference,
            "生成された型 API メタデータが見つかりません。");

        return RenderTable("型 API", $"{api.Namespace}.{api.Name}", api.Summary, api.Kind, api.Members,
            [("ctor", "コンストラクタ"), ("method", "メソッド"), ("prop", "プロパティ"),
             ("event", "イベント"), ("field", api.Kind == "enum" ? "値" : "フィールド")], null);
    }

    private static string RenderTable(string label, string name, string summary, string? kind,
        IEnumerable<ApiMember> members, IReadOnlyList<(string Kind, string Label)> sections, string? note)
    {
        ApiMember[] all = members.ToArray();
        var html = new StringBuilder(2048);
        html.Append("<section class=\"api-reference\" data-api-reference=\"")
            .Append(E(name)).Append("\"><header class=\"api-reference-heading\"><div><span>")
            .Append(E(label)).Append("</span><h3><code>").Append(E(name)).Append("</code></h3></div>");
        if (!string.IsNullOrWhiteSpace(kind))
            html.Append("<span class=\"api-kind\">").Append(E(kind)).Append("</span>");
        html.Append("</header>");
        if (!string.IsNullOrWhiteSpace(summary))
            html.Append("<p class=\"api-summary\">").Append(E(summary)).Append("</p>");

        if (all.Length == 0)
        {
            html.Append("<p class=\"api-empty\">表示できる公開メンバーはありません。</p>");
        }
        else
        {
            html.Append("<div class=\"api-table-scroll\" tabindex=\"0\" role=\"region\" aria-label=\"")
                .Append(E(name)).Append(" API 表\"><table class=\"api-table\"><caption class=\"visually-hidden\">")
                .Append(E(name)).Append(" の API</caption><thead><tr><th scope=\"col\">名前</th><th scope=\"col\">型</th><th scope=\"col\">説明</th></tr></thead>");
            foreach ((string sectionKind, string sectionLabel) in sections)
            {
                ApiMember[] sectionMembers = all.Where(member => member.Kind == sectionKind).ToArray();
                if (sectionMembers.Length == 0) continue;
                html.Append("<tbody><tr class=\"api-section-row\"><th scope=\"rowgroup\" colspan=\"3\">")
                    .Append(E(sectionLabel)).Append("</th></tr>");
                foreach (ApiMember member in sectionMembers)
                {
                    string type = member.Stateable ? $"{member.Type}（状態対応）" : member.Type;
                    html.Append("<tr><th scope=\"row\"><code>").Append(E(member.Name))
                        .Append("</code></th><td><code>").Append(E(type))
                        .Append("</code></td><td>").Append(E(member.Description)).Append("</td></tr>");
                }
                html.Append("</tbody>");
            }
            html.Append("</table></div>");
        }
        if (!string.IsNullOrWhiteSpace(note))
            html.Append("<p class=\"api-note\">").Append(E(note)).Append("</p>");
        return html.Append("</section>").ToString();
    }

    public static string Unavailable(string kind, string? reference, string detail)
    {
        var html = new StringBuilder(320);
        html.Append("<aside class=\"markdown-embed-unavailable\" data-embed-kind=\"")
            .Append(E(kind)).Append("\"><strong>埋め込みを表示できません</strong><span>")
            .Append(E(detail));
        if (!string.IsNullOrWhiteSpace(reference))
            html.Append(" 対象: <code>").Append(E(reference)).Append("</code>");
        return html.Append("</span></aside>").ToString();
    }

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
