using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Gallery.Stories;

/// <summary>Luxel.MathText (ブロック数式の組版) のストーリー — 描画回帰用。</summary>
[StoryMeta("Examples/Embeds/Math")]
public static class MathStories
{
    [Story]
    public static Widget Basic(StoryContext ctx) =>
        ctx.Snap(Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))[
            VStack(20)[
                Luxel.MathText.Factories.MathBlockView("""w = \frac{\alpha + \beta}{\sqrt{x^2 + y^2}}"""),
                Luxel.MathText.Factories.MathBlockView(
                    """M = \begin{bmatrix} m_{00} & m_{01} \\ m_{10} & m_{11} \end{bmatrix}"""),
                Luxel.MathText.Factories.MathBlockView("""\vec{p}' = \sum_{i} w_i M_i \vec{p}""")]]);
}
