using System.Text;
using Luxel.Typography;

namespace Luxel.Terminal.UI;

/// <summary>A terminal-oriented font family. Nerd Font glyphs are preferred for private-use code points.</summary>
public sealed class TerminalFontSet : IDisposable
{
    private readonly VectorFont[] _fallbacks;
    private readonly bool _ownsFonts;
    private bool _disposed;

    public TerminalFontSet(VectorFont primary, IEnumerable<VectorFont>? fallbacks = null,
        VectorFont? nerdFont = null, bool ownsFonts = false)
    {
        Primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallbacks = fallbacks?.Where(f => f is not null && !ReferenceEquals(f, primary)).Distinct().ToArray() ?? [];
        NerdFont = nerdFont;
        _ownsFonts = ownsFonts;
        Resolver = new GlyphResolver(this);
    }

    public VectorFont Primary { get; }
    public IReadOnlyList<VectorFont> Fallbacks => _fallbacks;
    public VectorFont? NerdFont { get; }
    public GlyphResolver Resolver { get; }

    internal IEnumerable<VectorFont> Enumerate(bool nerdFirst)
    {
        if (nerdFirst && NerdFont is not null) yield return NerdFont;
        yield return Primary;
        foreach (VectorFont fallback in _fallbacks) yield return fallback;
        if (!nerdFirst && NerdFont is not null) yield return NerdFont;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_ownsFonts) return;
        var seen = new HashSet<VectorFont>(ReferenceEqualityComparer.Instance);
        foreach (VectorFont font in Enumerate(false))
            if (seen.Add(font)) font.Dispose();
    }
}

/// <summary>Classifies cell-joining Powerline glyphs that should be warped to the terminal cell bounds.</summary>
public static class TerminalGlyphWarpPolicy
{
    /// <summary>Powerline and Powerline Extra separator block. SCM/status icons at U+E0A0–U+E0AF are excluded.</summary>
    public static bool IsPowerlineSeparator(Rune rune) => rune.Value is >= 0xE0B0 and <= 0xE0D4;

    public static bool IsPowerlineSeparator(string text)
    {
        var runes = text.EnumerateRunes();
        if (!runes.MoveNext()) return false;
        Rune rune = runes.Current;
        return !runes.MoveNext() && IsPowerlineSeparator(rune);
    }
}

/// <summary>Resolves a complete terminal cell cluster to one font without splitting combining sequences.</summary>
public sealed class GlyphResolver
{
    private readonly TerminalFontSet _fonts;
    internal GlyphResolver(TerminalFontSet fonts) => _fonts = fonts;

    public VectorFont Resolve(string text)
    {
        if (string.IsNullOrEmpty(text)) return _fonts.Primary;
        bool nerd = text.EnumerateRunes().Any(r => IsNerdFontCodePoint(r.Value));
        foreach (VectorFont font in _fonts.Enumerate(nerd))
            if (Supports(font, text)) return font;
        return _fonts.Primary;
    }

    public static bool IsNerdFontCodePoint(int codePoint)
        => codePoint is >= 0xE000 and <= 0xF8FF
            or >= 0xF0000 and <= 0xFFFFD
            or >= 0x100000 and <= 0x10FFFD;

    private static bool Supports(VectorFont font, string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            int value = rune.Value;
            if (value is 0x200C or 0x200D or >= 0xFE00 and <= 0xFE0F) continue;
            if (!font.HasGlyph(value)) return false;
        }
        return true;
    }
}
