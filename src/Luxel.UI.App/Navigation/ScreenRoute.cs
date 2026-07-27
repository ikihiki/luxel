using Luxel.UI;

namespace Luxel.UI.App;

internal sealed record ScreenRoute(string Path, Func<Navigation, Widget> Factory);
