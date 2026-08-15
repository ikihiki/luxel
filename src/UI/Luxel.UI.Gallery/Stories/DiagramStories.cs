using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>Luxel.Diagram (mermaid サブセット) のストーリー — 描画回帰用。</summary>
[StoryMeta("Examples/Embeds/Diagram")]
public static class DiagramStories
{
    [Story]
    public static Widget Basic(StoryContext ctx) =>
        ctx.Snap(Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(20))[
            Luxel.Diagram.Factories.DiagramBlock("""
                flowchart LR
                app[GalleryApp] --> host[UiHost]
                host --> canvas[RetainedCanvas]
                host -->|Load| res(Resources)
                canvas -->|dispatch| gpu{GPU}
                gpu --> vk[Vulkan]
                gpu --> dx[D3D12]
                """)]);
}
