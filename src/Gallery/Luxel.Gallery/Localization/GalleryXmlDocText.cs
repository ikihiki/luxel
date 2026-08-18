namespace Luxel.Gallery;

/// <summary>Resolves XML documentation identities to Gallery-owned Japanese display text.</summary>
public static class GalleryXmlDocText
{
    private static readonly IReadOnlyDictionary<string, string> Japanese =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Returns the registered Japanese text, or the original XML summary when no translation exists.</summary>
    public static string Resolve(string key, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        fallback ??= string.Empty;
        return Japanese.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    internal static IReadOnlyDictionary<string, string> Entries => Japanese;
}
