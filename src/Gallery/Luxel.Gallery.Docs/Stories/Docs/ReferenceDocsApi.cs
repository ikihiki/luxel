using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

internal static class ReferenceDocsApi
{
    internal static void Register(StoryCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        foreach (string ns in TypeApiRegistry.Namespaces)
        {
            string captured = ns;
            builder.Add(new StoryInfo($"Reference/{captured}",
                static _ => Spacer(), Source: "Generated API reference page.",
                ResultBuild: ctx => NamespacePage(ctx, captured)));
        }
    }

    private static StoryResult NamespacePage(StoryContext ctx, string ns)
    {
        IReadOnlyList<TypeApi> types = TypeApiRegistry.InNamespace(ns);
        var document = new DocString(512, types.Count);
        document.AppendLiteral($"# {ns}\n\n<!-- luxel-toc-placeholder -->\n\n");
        document.AppendLiteral("この名前空間の公開型 API です。ソースジェネレーターが参照アセンブリの XML doc コメントから生成します。\n");
        foreach (TypeApi type in types)
        {
            document.AppendLiteral($"\n## {type.Name}\n\n");
            document.AppendFormatted(new DocEmbed(
                global::Luxel.Gallery.UI.Kit.TypeApiTable($"{type.Namespace}.{type.Name}", width: 760f),
                DocEmbedKind.TypeApiTable, $"{type.Namespace}.{type.Name}"));
            document.AppendLiteral("\n");
        }
        return StoryResult.FromDocument(document.Markdown, document.Embeds);
    }
}
