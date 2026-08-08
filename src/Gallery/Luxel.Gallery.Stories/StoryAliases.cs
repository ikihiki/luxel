using System.Runtime.CompilerServices;

namespace Luxel.Gallery;

/// <summary>Registers dynamic API-reference providers. Story route aliases are intentionally not kept.</summary>
internal static class StoryAliases
{
    [ModuleInitializer]
    internal static void Register() => Stories.DocsApi.RegisterReferenceProvider();
}
