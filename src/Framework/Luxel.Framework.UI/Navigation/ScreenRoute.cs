using Luxel.UI;

namespace Luxel.Framework.UI;

internal sealed record ScreenRoute(string Path, Func<Navigation, Widget> Factory);
