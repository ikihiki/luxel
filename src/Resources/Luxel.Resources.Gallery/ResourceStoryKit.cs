using Luxel.Controls;
using Luxel.UI;
using static Luxel.Controls.Kit;

namespace Luxel.Resources.Gallery.Stories;

internal static class ResourceStoryKit
{
    internal static Border Frame(Widget child) =>
        Border(background: Bind.From(() => UiTheme.T.Background), padding: new Thickness(24))
            [Center()[child]];
}
