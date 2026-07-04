using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;
using static Luxel.Gallery.Stories.DocsKit;

namespace Luxel.Gallery.Stories;

/// <summary>docs — UI/コントロール章。</summary>
public static class DocsUi
{
    [Story("Docs/Button", Width = 800, Height = 480, Order = 1)]
    public static Widget ButtonDocs(StoryContext ctx) => WithDocFonts(Docs(ctx, $"""
        # Button

        ボタンは **Variant × Intent × 状態** から配色を解決します。未指定のプロパティは
        テーマ値へフォールバックし、hover/press はトランジション (状態機械) で補間されます。

        ## Variant (形)

        > [!TIP]
        > 下の実例のすぐ下に `StorySource` で**実際のストーリーソース**を出しています
        > (ジェネレーターが焼き込むため、手書きコピーの乖離が起きません)。

        {StoryRef(ctx, "Button/Variants")}

        {StorySource("Button/Variants")}

        ## Intent (意味色)

        {StoryRef(ctx, "Button/Intents")}

        ## 使い方

        ```csharp
        Button(_ => ctx.Log("clicked"), "OK", variant: Variant.Tonal, intent: Intent.Success)
        ```

        コールバックの第一引数は**発火元の Button 自身** (sender-first 規約) です。
        入門は [GettingStarted](story:Docs/GettingStarted) へ。

        ## API

        {ApiTable("Button")}
        """, toc: true, fences: DocsFences));
}
